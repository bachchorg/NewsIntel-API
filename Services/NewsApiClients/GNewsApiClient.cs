using System.Text.Json;
using Microsoft.Extensions.Options;
using NewsIntel.API.Configuration;
using NewsIntel.API.DTOs;
using NewsIntel.API.Services.Interfaces;

namespace NewsIntel.API.Services.NewsApiClients;

/// <summary>
/// GNews.io API client — free tier: 100 requests/day.
/// Polls every 10 minutes to conserve quota.
/// </summary>
public class GNewsApiClient : INewsApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly RateLimitTracker _rateLimits;
    private readonly ILogger<GNewsApiClient> _logger;

    public string SourceName => "gnews";
    public string[] KnownAliases => ["gnews", "GNews"];
    public int MinPollIntervalSeconds => 300; // 5 minutes

    public GNewsApiClient(HttpClient http, IOptions<NewsApiOptions> opts, RateLimitTracker rateLimits, ILogger<GNewsApiClient> logger)
    {
        _http = http;
        _apiKey = opts.Value.GNewsApiKey;
        _rateLimits = rateLimits;
        _logger = logger;
        _http.BaseAddress = new Uri("https://gnews.io/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NewsIntel/1.0");
    }

    public async Task<IEnumerable<ArticleDto>> FetchArticlesAsync(
        IEnumerable<string> keywords, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey)) return [];

        var info = _rateLimits.GetInfo(SourceName);
        if (info is { IsExhausted: true, ResetsAt: not null } && info.ResetsAt > DateTime.UtcNow)
        {
            _logger.LogWarning("GNews rate limit exhausted, resets at {ResetsAt}", info.ResetsAt);
            return [];
        }

        var results = new List<ArticleDto>();
        var query = string.Join(" OR ", keywords);
        var fromStr = (from ?? DateTime.UtcNow.AddDays(-7)).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var toStr = (to ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ");

        try
        {
            var url = $"api/v4/search?q={Uri.EscapeDataString(query)}&from={fromStr}&to={toStr}&lang=en&max=100&token={_apiKey}";
            var resp = await _http.GetAsync(url, ct);

            if ((int)resp.StatusCode == 429 || (int)resp.StatusCode == 403)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("GNews {Status}: {Body}", (int)resp.StatusCode, errorBody);
                _rateLimits.MarkExhausted(SourceName, dailyLimit: 100, resetsAt: DateTime.UtcNow.Date.AddDays(1));
                return results;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("GNews {Status}: {Body}", (int)resp.StatusCode, errorBody);
                return results;
            }

            _rateLimits.RecordRequest(SourceName, dailyLimit: 100);

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var articles = doc.RootElement.GetProperty("articles");

            foreach (var item in articles.EnumerateArray())
            {
                var articleUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var headline = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var description = item.TryGetProperty("description", out var d) ? d.GetString() : null;
                var sourceName = item.TryGetProperty("source", out var src) && src.TryGetProperty("name", out var sn) ? sn.GetString() ?? "GNews" : "GNews";
                var pubDate = item.TryGetProperty("publishedAt", out var pd) && DateTime.TryParse(pd.GetString(), out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow;

                if (string.IsNullOrEmpty(articleUrl)) continue;

                var text = $"{headline} {description}";
                var matched = keywords.Where(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();

                results.Add(new ArticleDto(
                    ArticleId: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(articleUrl))).ToLower()[..16],
                    Source: sourceName,
                    SourceUrl: "gnews.io",
                    ArticleUrl: articleUrl,
                    Headline: headline,
                    Subheadline: null,
                    Author: [],
                    PublishedAt: pubDate,
                    CrawledAt: DateTime.UtcNow,
                    KeywordsMatched: matched.Count > 0 ? matched : keywords.Take(1).ToList(),
                    Summary: description,
                    FullText: null,
                    Category: null,
                    Sentiment: "neutral",
                    Tags: [],
                    Paywall: false
                ));
            }

            _logger.LogInformation("GNews fetched {Count} articles", results.Count);
        }
        catch (Exception ex) { _logger.LogError(ex, "GNews error"); }

        return results;
    }
}
