using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NewsIntel.API.Data;
using NewsIntel.API.Hubs;
using NewsIntel.API.Models;
using NewsIntel.API.Services;
using NewsIntel.API.Services.Interfaces;
using NewsIntel.API.Services.NewsApiClients;

namespace NewsIntel.API.BackgroundServices;

public class CrawlBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NewsHub> _hub;
    private readonly DeduplicationService _dedup;
    private readonly SpikeDetectionService _spikes;
    private readonly RateLimitTracker _rateLimits;
    private readonly ILogger<CrawlBackgroundService> _logger;

    // Track per-client-per-session last poll time
    private readonly ConcurrentDictionary<string, DateTime> _clientLastPolled = new();

    public CrawlBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHubContext<NewsHub> hub,
        DeduplicationService dedup,
        SpikeDetectionService spikes,
        RateLimitTracker rateLimits,
        ILogger<CrawlBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _dedup = dedup;
        _spikes = spikes;
        _rateLimits = rateLimits;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CrawlBackgroundService starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessActiveSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in crawl loop");
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessActiveSessionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<CrawlSessionService>();
        var articleRepo = scope.ServiceProvider.GetRequiredService<ArticleRepository>();
        var enrichment = scope.ServiceProvider.GetRequiredService<ArticleEnrichmentService>();
        var apiClients = scope.ServiceProvider.GetRequiredService<IEnumerable<INewsApiClient>>();

        var activeSessions = await sessionService.GetActiveSessionsAsync();

        foreach (var session in activeSessions)
        {
            if (ct.IsCancellationRequested) break;

            // Check if it's time to poll (respect session-level poll interval)
            if (session.LastPolledAt.HasValue &&
                (DateTime.UtcNow - session.LastPolledAt.Value).TotalSeconds < session.PollIntervalSeconds)
                continue;

            await CrawlSessionAsync(session, sessionService, articleRepo, enrichment, apiClients, ct);
        }
    }

    private async Task CrawlSessionAsync(
        CrawlSession session,
        CrawlSessionService sessionService,
        ArticleRepository articleRepo,
        ArticleEnrichmentService enrichment,
        IEnumerable<INewsApiClient> apiClients,
        CancellationToken ct)
    {
        _logger.LogInformation("Crawling session {Id} ({State})", session.Id, session.State);

        var keywords = session.Keywords.Where(k => k.Logic != "not").Select(k => k.Term).ToList();
        if (!keywords.Any()) return;

        // Init dedup from DB if not loaded yet
        var seenIds = await articleRepo.GetSeenIdsAsync(session.Id);
        _dedup.InitSession(session.Id, seenIds);

        var from = session.LastPolledAt ?? session.DateRangeFrom ?? DateTime.UtcNow.AddDays(-1);
        var to = session.DateRangeTo ?? DateTime.UtcNow;

        var enabledSources = session.Sources.Where(s => s.IsEnabled).Select(s => s.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchedClients = apiClients.Where(c => c.KnownAliases.Any(alias => enabledSources.Contains(alias))).ToList();

        // Filter clients that are ready to poll (respect per-client MinPollIntervalSeconds)
        var readyClients = matchedClients.Where(client =>
        {
            var key = $"{session.Id}:{client.SourceName}";
            if (_clientLastPolled.TryGetValue(key, out var lastPoll))
            {
                return (DateTime.UtcNow - lastPoll).TotalSeconds >= client.MinPollIntervalSeconds;
            }
            return true; // First poll
        }).ToList();

        if (readyClients.Count == 0)
        {
            _logger.LogDebug("Session {Id}: no clients ready to poll yet", session.Id);
            return;
        }

        // Fan out to ready clients
        var fetchTasks = readyClients.Select(client => client.FetchArticlesAsync(keywords, from, to, ct));
        var rawBatches = await Task.WhenAll(fetchTasks);

        // Mark clients as polled
        foreach (var client in readyClients)
        {
            _clientLastPolled[$"{session.Id}:{client.SourceName}"] = DateTime.UtcNow;
        }

        var allFetched = rawBatches.SelectMany(b => b).ToList();
        var newArticles = allFetched
            .Where(a => !_dedup.IsSeen(a.ArticleId, session.Id))
            .DistinctBy(a => a.ArticleId)
            .Select(a => enrichment.Enrich(a, keywords))
            .ToList();

        _logger.LogInformation("Session {Id}: {Fetched} fetched from {Clients} clients, {New} new after dedup",
            session.Id, allFetched.Count, readyClients.Count, newArticles.Count);

        if (newArticles.Any())
        {
            foreach (var a in newArticles) _dedup.MarkSeen(a.ArticleId, session.Id);
            await articleRepo.SaveArticlesAsync(session.Id, newArticles);

            // Push new articles via SignalR
            await _hub.Clients.Group(session.Id.ToString()).SendAsync("NewArticles", newArticles, ct);

            // Spike detection
            var spikes = _spikes.DetectSpikes(session.Id, newArticles, keywords);
            foreach (var spike in spikes)
                await _hub.Clients.Group(session.Id.ToString()).SendAsync("SpikeAlert", spike, ct);

            _logger.LogInformation("Session {Id}: pushed {Count} new articles", session.Id, newArticles.Count);
        }

        // Push rate limit warnings for exhausted sources
        var exhaustedSources = _rateLimits.GetAll().Where(r => r.IsExhausted).ToList();
        if (exhaustedSources.Any())
        {
            await _hub.Clients.Group(session.Id.ToString()).SendAsync("RateLimitWarning", exhaustedSources, ct);
        }

        // Push analytics update
        var analytics = await articleRepo.GetAnalyticsAsync(session.Id);
        await _hub.Clients.Group(session.Id.ToString()).SendAsync("AnalyticsUpdated", analytics, ct);

        // Update last polled time
        await sessionService.UpdateLastPolledAsync(session.Id, to);

        // Transition Backfilling -> Live if from date has caught up
        if (session.State == SessionState.Backfilling && from >= DateTime.UtcNow.AddMinutes(-5))
        {
            await sessionService.TransitionToLiveAsync(session.Id);
            await _hub.Clients.Group(session.Id.ToString()).SendAsync("SessionStateChanged", new { sessionId = session.Id.ToString(), state = "Live" }, ct);
        }
    }
}
