using System.Text.Json;
using Microsoft.Extensions.Options;
using NewsIntel.API.Configuration;
using NewsIntel.API.DTOs;
using NewsIntel.API.Services.Interfaces;

namespace NewsIntel.API.Services.NewsApiClients;

public class NytApiClient : INewsApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<NytApiClient> _logger;
    private readonly SemaphoreSlim _throttle = new(1, 1);

    public string SourceName => "nytimes";
    public string[] KnownAliases => ["nytimes", "New York Times"];

    public NytApiClient(HttpClient http, IOptions<NewsApiOptions> opts, ILogger<NytApiClient> logger)
    {
        _http = http;
        _apiKey = opts.Value.NytApiKey;
        _logger = logger;
        _http.BaseAddress = new Uri("https://api.nytimes.com/");
    }

    public async Task<IEnumerable<ArticleDto>> FetchArticlesAsync(IEnumerable<string> keywords, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey)) return [];
        var results = new List<ArticleDto>();
        var query = string.Join(" OR ", keywords.Select(k => $"\"{k}\""));
        var fromStr = (from ?? DateTime.UtcNow.AddDays(-7)).ToString("yyyyMMdd");
        var toStr = (to ?? DateTime.UtcNow).ToString("yyyyMMdd");

        try
        {
            await _throttle.WaitAsync(ct);
            var url = $"svc/search/v2/articlesearch.json?q={Uri.EscapeDataString(query)}&api-key={_apiKey}&begin_date={fromStr}&end_date={toStr}&sort=newest&page=0";
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return results;

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var docs = doc.RootElement.GetProperty("response").GetProperty("docs");

            foreach (var item in docs.EnumerateArray())
            {
                var articleUrl = item.TryGetProperty("web_url", out var wu) ? wu.GetString() ?? "" : "";
                var headline = item.TryGetProperty("headline", out var hl) && hl.TryGetProperty("main", out var hm) ? hm.GetString() ?? "" : "";
                var pubDate = item.TryGetProperty("pub_date", out var pd) && DateTime.TryParse(pd.GetString(), out var dt) ? dt : DateTime.UtcNow;
                var summary = item.TryGetProperty("abstract", out var ab) ? ab.GetString() : null;
                var section = item.TryGetProperty("section_name", out var sn) ? sn.GetString() : null;

                results.Add(new ArticleDto(
                    ArticleId: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(articleUrl))).ToLower()[..16],
                    Source: SourceName,
                    SourceUrl: "https://www.nytimes.com",
                    ArticleUrl: articleUrl,
                    Headline: headline,
                    Subheadline: null,
                    Author: [],
                    PublishedAt: pubDate.ToUniversalTime(),
                    CrawledAt: DateTime.UtcNow,
                    KeywordsMatched: keywords.Where(k => headline.Contains(k, StringComparison.OrdinalIgnoreCase) || (summary ?? "").Contains(k, StringComparison.OrdinalIgnoreCase)).ToList(),
                    Summary: summary,
                    FullText: null,
                    Category: section,
                    Sentiment: "neutral",
                    Tags: [],
                    Paywall: true
                ));
            }
            await Task.Delay(6100, ct); // NYT rate limit: 10/min
        }
        catch (Exception ex) { _logger.LogError(ex, "NYT API error"); }
        finally { _throttle.Release(); }

        return results;
    }
}
