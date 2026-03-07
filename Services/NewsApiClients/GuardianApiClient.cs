using System.Text.Json;
using Microsoft.Extensions.Options;
using NewsIntel.API.Configuration;
using NewsIntel.API.DTOs;
using NewsIntel.API.Services.Interfaces;

namespace NewsIntel.API.Services.NewsApiClients;

public class GuardianApiClient : INewsApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<GuardianApiClient> _logger;

    public string SourceName => "the-guardian";
    public string[] KnownAliases => ["the-guardian", "The Guardian"];

    public GuardianApiClient(HttpClient http, IOptions<NewsApiOptions> opts, ILogger<GuardianApiClient> logger)
    {
        _http = http;
        _apiKey = opts.Value.GuardianApiKey;
        _logger = logger;
        _http.BaseAddress = new Uri("https://content.guardianapis.com/");
    }

    public async Task<IEnumerable<ArticleDto>> FetchArticlesAsync(IEnumerable<string> keywords, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey)) return [];
        var results = new List<ArticleDto>();
        var query = string.Join(" OR ", keywords);
        var fromStr = (from ?? DateTime.UtcNow.AddDays(-7)).ToString("yyyy-MM-dd");

        try
        {
            var url = $"search?q={Uri.EscapeDataString(query)}&api-key={_apiKey}&from-date={fromStr}&show-fields=trailText&order-by=newest&page-size=50";
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return results;

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.GetProperty("response").GetProperty("results");

            foreach (var item in items.EnumerateArray())
            {
                var articleUrl = item.TryGetProperty("webUrl", out var wu) ? wu.GetString() ?? "" : "";
                var headline = item.TryGetProperty("webTitle", out var wt) ? wt.GetString() ?? "" : "";
                var pubDate = item.TryGetProperty("webPublicationDate", out var pd) && DateTime.TryParse(pd.GetString(), out var dt) ? dt : DateTime.UtcNow;
                string? summary = null;
                if (item.TryGetProperty("fields", out var fields) && fields.TryGetProperty("trailText", out var tt))
                    summary = tt.GetString();

                results.Add(new ArticleDto(
                    ArticleId: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(articleUrl))).ToLower()[..16],
                    Source: SourceName,
                    SourceUrl: "https://www.theguardian.com",
                    ArticleUrl: articleUrl,
                    Headline: headline,
                    Subheadline: null,
                    Author: [],
                    PublishedAt: pubDate.ToUniversalTime(),
                    CrawledAt: DateTime.UtcNow,
                    KeywordsMatched: keywords.Where(k => headline.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList(),
                    Summary: summary,
                    FullText: null,
                    Category: item.TryGetProperty("sectionName", out var sn) ? sn.GetString() : null,
                    Sentiment: "neutral",
                    Tags: [],
                    Paywall: false
                ));
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Guardian API error"); }

        return results;
    }
}
