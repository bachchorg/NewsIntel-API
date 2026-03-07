using System.Text.Json;
using Microsoft.Extensions.Options;
using NewsIntel.API.Configuration;
using NewsIntel.API.DTOs;
using NewsIntel.API.Services.Interfaces;

namespace NewsIntel.API.Services.NewsApiClients;

public class NewsApiOrgClient : INewsApiClient
{
    private readonly HttpClient _http;
    private readonly string[] _apiKeys;
    private readonly RateLimitTracker _rateLimits;
    private readonly ILogger<NewsApiOrgClient> _logger;
    private int _currentKeyIndex;
    private readonly object _keyLock = new();

    public string SourceName => "newsapi";
    public string[] KnownAliases => ["newsapi", "NewsAPI"];
    public int MinPollIntervalSeconds => 300; // 5 minutes

    public NewsApiOrgClient(HttpClient http, IOptions<NewsApiOptions> opts, RateLimitTracker rateLimits, ILogger<NewsApiOrgClient> logger)
    {
        _http = http;
        _rateLimits = rateLimits;
        _logger = logger;
        _http.BaseAddress = new Uri("https://newsapi.org/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NewsIntel/1.0");

        // Prefer comma-separated keys list, fall back to single key
        var keysStr = opts.Value.NewsApiOrgKeys;
        if (!string.IsNullOrWhiteSpace(keysStr))
        {
            _apiKeys = keysStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else if (!string.IsNullOrEmpty(opts.Value.NewsApiOrgKey))
        {
            _apiKeys = [opts.Value.NewsApiOrgKey];
        }
        else
        {
            _apiKeys = [];
        }

        _logger.LogInformation("NewsAPI.org initialized with {Count} API key(s)", _apiKeys.Length);
    }

    private string? GetCurrentKey()
    {
        lock (_keyLock)
        {
            return _apiKeys.Length > 0 ? _apiKeys[_currentKeyIndex] : null;
        }
    }

    private string? RotateToNextKey()
    {
        lock (_keyLock)
        {
            if (_apiKeys.Length <= 1) return null;
            _currentKeyIndex = (_currentKeyIndex + 1) % _apiKeys.Length;
            _logger.LogWarning("NewsAPI.org rotating to key #{Index}", _currentKeyIndex + 1);
            return _apiKeys[_currentKeyIndex];
        }
    }

    public async Task<IEnumerable<ArticleDto>> FetchArticlesAsync(IEnumerable<string> keywords, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var apiKey = GetCurrentKey();
        if (apiKey == null) return [];

        var info = _rateLimits.GetInfo(SourceName);
        if (info is { IsExhausted: true, ResetsAt: not null } && info.ResetsAt > DateTime.UtcNow)
        {
            _logger.LogWarning("NewsAPI.org all keys exhausted, resets at {ResetsAt}", info.ResetsAt);
            return [];
        }

        var results = new List<ArticleDto>();
        var keywordList = keywords.ToList();
        var query = string.Join(" OR ", keywordList);
        var fromStr = (from ?? DateTime.UtcNow.AddDays(-7)).ToString("yyyy-MM-ddTHH:mm:ssZ");

        try
        {
            var url = $"v2/everything?q={Uri.EscapeDataString(query)}&from={fromStr}&language=en&sortBy=publishedAt&pageSize=100";

            // Try current key, rotate on 429
            var resp = await SendWithKey(url, apiKey, ct);

            if ((int)resp.StatusCode == 429)
            {
                // Try rotating through remaining keys
                for (var i = 1; i < _apiKeys.Length; i++)
                {
                    var nextKey = RotateToNextKey();
                    if (nextKey == null) break;

                    resp = await SendWithKey(url, nextKey, ct);
                    if ((int)resp.StatusCode != 429) break;
                }

                // If still 429 after all keys, mark exhausted
                if ((int)resp.StatusCode == 429)
                {
                    var errorBody = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("NewsAPI.org all {Count} keys exhausted: {Body}", _apiKeys.Length, errorBody);
                    _rateLimits.MarkExhausted(SourceName, dailyLimit: 100 * _apiKeys.Length, resetsAt: DateTime.UtcNow.Date.AddDays(1));
                    return results;
                }
            }

            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("NewsAPI.org {Status}: {Body}", (int)resp.StatusCode, errorBody);
                return results;
            }

            _rateLimits.RecordRequest(SourceName, dailyLimit: 100 * _apiKeys.Length);

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var articles = doc.RootElement.GetProperty("articles");

            foreach (var item in articles.EnumerateArray())
            {
                var articleUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var headline = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var sourceName = item.TryGetProperty("source", out var src) && src.TryGetProperty("name", out var sn) ? sn.GetString() ?? SourceName : SourceName;
                var pubDate = item.TryGetProperty("publishedAt", out var pd) && DateTime.TryParse(pd.GetString(), out var dt) ? dt : DateTime.UtcNow;
                var summary = item.TryGetProperty("description", out var desc) ? desc.GetString() : null;
                var author = item.TryGetProperty("author", out var au) && au.GetString() != null ? new List<string> { au.GetString()! } : new List<string>();

                if (string.IsNullOrEmpty(articleUrl) || articleUrl == "https://removed.com") continue;

                results.Add(new ArticleDto(
                    ArticleId: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(articleUrl))).ToLower()[..16],
                    Source: sourceName,
                    SourceUrl: "",
                    ArticleUrl: articleUrl,
                    Headline: headline,
                    Subheadline: null,
                    Author: author,
                    PublishedAt: pubDate.ToUniversalTime(),
                    CrawledAt: DateTime.UtcNow,
                    KeywordsMatched: keywordList.Where(k => headline.Contains(k, StringComparison.OrdinalIgnoreCase) || (summary ?? "").Contains(k, StringComparison.OrdinalIgnoreCase)).ToList(),
                    Summary: summary,
                    FullText: null,
                    Category: null,
                    Sentiment: "neutral",
                    Tags: [],
                    Paywall: false
                ));
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "NewsAPI.org error"); }

        return results;
    }

    private async Task<HttpResponseMessage> SendWithKey(string url, string apiKey, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Api-Key", apiKey);
        return await _http.SendAsync(request, ct);
    }
}
