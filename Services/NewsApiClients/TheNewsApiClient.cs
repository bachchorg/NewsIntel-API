using System.Text.Json;
using Microsoft.Extensions.Options;
using NewsIntel.API.Configuration;
using NewsIntel.API.DTOs;
using NewsIntel.API.Services.Interfaces;

namespace NewsIntel.API.Services.NewsApiClients;

/// <summary>
/// TheNewsAPI.com client — free tier: 3 requests/day (very limited).
/// Polls every 10 minutes to conserve quota.
/// </summary>
public class TheNewsApiClient : INewsApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly RateLimitTracker _rateLimits;
    private readonly ILogger<TheNewsApiClient> _logger;

    public string SourceName => "thenewsapi";
    public string[] KnownAliases => ["thenewsapi", "TheNewsAPI"];
    public int MinPollIntervalSeconds => 300; // 5 minutes

    public TheNewsApiClient(HttpClient http, IOptions<NewsApiOptions> opts, RateLimitTracker rateLimits, ILogger<TheNewsApiClient> logger)
    {
        _http = http;
        _apiKey = opts.Value.TheNewsApiKey;
        _rateLimits = rateLimits;
        _logger = logger;
        _http.BaseAddress = new Uri("https://api.thenewsapi.com/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NewsIntel/1.0");
    }

    public async Task<IEnumerable<ArticleDto>> FetchArticlesAsync(
        IEnumerable<string> keywords, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey)) return [];

        var info = _rateLimits.GetInfo(SourceName);
        if (info is { IsExhausted: true, ResetsAt: not null } && info.ResetsAt > DateTime.UtcNow)
        {
            _logger.LogWarning("TheNewsAPI rate limit exhausted, resets at {ResetsAt}", info.ResetsAt);
            return [];
        }

        var results = new List<ArticleDto>();
        var query = string.Join("|", keywords); // TheNewsAPI uses | for OR
        var fromStr = (from ?? DateTime.UtcNow.AddDays(-7)).ToString("yyyy-MM-dd");
        var toStr = (to ?? DateTime.UtcNow).ToString("yyyy-MM-dd");

        try
        {
            var url = $"v1/news/all?api_token={_apiKey}&search={Uri.EscapeDataString(query)}&published_after={fromStr}&published_before={toStr}&language=en&limit=50";
            var resp = await _http.GetAsync(url, ct);

            if ((int)resp.StatusCode == 429 || (int)resp.StatusCode == 402)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("TheNewsAPI {Status}: {Body}", (int)resp.StatusCode, errorBody);
                _rateLimits.MarkExhausted(SourceName, dailyLimit: 100, resetsAt: DateTime.UtcNow.Date.AddDays(1));
                return results;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("TheNewsAPI {Status}: {Body}", (int)resp.StatusCode, errorBody);
                return results;
            }

            _rateLimits.RecordRequest(SourceName, dailyLimit: 100);

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            foreach (var item in data.EnumerateArray())
            {
                var articleUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var headline = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var description = item.TryGetProperty("description", out var d) ? d.GetString() : null;
                var snippet = item.TryGetProperty("snippet", out var sn) ? sn.GetString() : null;
                var sourceName = item.TryGetProperty("source", out var src) ? src.GetString() ?? "TheNewsAPI" : "TheNewsAPI";
                var pubDate = item.TryGetProperty("published_at", out var pd) && DateTime.TryParse(pd.GetString(), out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow;

                if (string.IsNullOrEmpty(articleUrl)) continue;

                var text = $"{headline} {description} {snippet}";
                var matched = keywords.Where(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();

                results.Add(new ArticleDto(
                    ArticleId: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(articleUrl))).ToLower()[..16],
                    Source: sourceName,
                    SourceUrl: "thenewsapi.com",
                    ArticleUrl: articleUrl,
                    Headline: headline,
                    Subheadline: null,
                    Author: [],
                    PublishedAt: pubDate,
                    CrawledAt: DateTime.UtcNow,
                    KeywordsMatched: matched.Count > 0 ? matched : keywords.Take(1).ToList(),
                    Summary: description ?? snippet,
                    FullText: null,
                    Category: null,
                    Sentiment: "neutral",
                    Tags: [],
                    Paywall: false
                ));
            }

            _logger.LogInformation("TheNewsAPI fetched {Count} articles", results.Count);
        }
        catch (Exception ex) { _logger.LogError(ex, "TheNewsAPI error"); }

        return results;
    }
}
