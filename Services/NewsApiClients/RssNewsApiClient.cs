using System.Xml;
using NewsIntel.API.DTOs;
using NewsIntel.API.Services.Interfaces;

namespace NewsIntel.API.Services.NewsApiClients;

/// <summary>
/// Fetches news from public RSS feeds of CNN, Reuters, Fox News, and other major outlets.
/// No API key required — reads XML directly from each outlet's RSS endpoint.
/// Inspired by riad-azz/next-news-api but self-hosted (no dependency on external JSON wrapper).
/// </summary>
public class RssNewsApiClient : INewsApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<RssNewsApiClient> _logger;

    public string SourceName => "rss-news";
    public string[] KnownAliases => ["rss-news"];

    private static readonly (string Name, string FeedUrl)[] RssFeeds =
    [
        // CNN — RSS feeds are stale (stopped updating ~2024). CNN content comes via NewsAPI.
        // AP — No public RSS feed. AP content comes via NewsAPI.

        // New York Times — public RSS feeds (no key needed)
        ("New York Times",  "https://rss.nytimes.com/services/xml/rss/nyt/HomePage.xml"),
        ("NYT World",       "https://rss.nytimes.com/services/xml/rss/nyt/World.xml"),
        ("NYT Politics",    "https://rss.nytimes.com/services/xml/rss/nyt/Politics.xml"),
        ("NYT Business",    "https://rss.nytimes.com/services/xml/rss/nyt/Business.xml"),

        // The Guardian — public RSS
        ("The Guardian",    "https://www.theguardian.com/world/rss"),

        // Washington Post — public RSS
        ("Washington Post", "https://feeds.washingtonpost.com/rss/world"),

        // Fox News
        ("Fox News",        "https://moxie.foxnews.com/google-publisher/latest.xml"),
        ("Fox Politics",    "https://moxie.foxnews.com/google-publisher/politics.xml"),
        ("Fox World",       "https://moxie.foxnews.com/google-publisher/world.xml"),

        // NPR
        ("NPR",             "https://feeds.npr.org/1001/rss.xml"),
        ("NPR World",       "https://feeds.npr.org/1004/rss.xml"),
        ("NPR Politics",    "https://feeds.npr.org/1014/rss.xml"),

        // BBC
        ("BBC News",        "https://feeds.bbci.co.uk/news/rss.xml"),
        ("BBC World",       "https://feeds.bbci.co.uk/news/world/rss.xml"),
        ("BBC Business",    "https://feeds.bbci.co.uk/news/business/rss.xml"),
        ("BBC Tech",        "https://feeds.bbci.co.uk/news/technology/rss.xml"),

        // CBS News
        ("CBS News",        "https://www.cbsnews.com/latest/rss/main"),

        // NBC News
        ("NBC News",        "https://feeds.nbcnews.com/nbcnews/public/news"),

        // Other
        ("Al Jazeera",      "https://www.aljazeera.com/xml/rss/all.xml"),
        ("ABC News",        "https://abcnews.go.com/abcnews/topstories"),
        ("CBC News",        "https://rss.cbc.ca/lineup/topstories.xml"),
        ("Deutsche Welle",  "https://rss.dw.com/rdf/rss-en-all"),
    ];

    public RssNewsApiClient(HttpClient http, ILogger<RssNewsApiClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NewsIntel/1.0");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<IEnumerable<ArticleDto>> FetchArticlesAsync(
        IEnumerable<string> keywords, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var results = new List<ArticleDto>();
        var keywordList = keywords.ToList();
        // RSS feeds return a snapshot of recent articles (not paginated by time).
        // Use a fixed 7-day lookback instead of the poll cursor — dedup prevents duplicates.
        var rssFrom = DateTime.UtcNow.AddDays(-7);

        var tasks = RssFeeds.Select(feed => FetchFeedAsync(feed.Name, feed.FeedUrl, keywordList, rssFrom, to, ct));
        var batches = await Task.WhenAll(tasks);

        foreach (var batch in batches)
            results.AddRange(batch);

        _logger.LogInformation("RSS News fetched {Count} articles matching keywords from {FeedCount} feeds",
            results.Count, RssFeeds.Length);
        return results;
    }

    private async Task<List<ArticleDto>> FetchFeedAsync(
        string sourceName, string feedUrl, List<string> keywords,
        DateTime fromDate, DateTime? to, CancellationToken ct)
    {
        var articles = new List<ArticleDto>();
        try
        {
            var xml = await _http.GetStringAsync(feedUrl, ct);
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            // Handle both RSS 2.0 (<item>) and Atom (<entry>) formats
            var items = doc.GetElementsByTagName("item");
            if (items.Count == 0)
                items = doc.GetElementsByTagName("entry");

            _logger.LogInformation("RSS {Source}: found {Count} items in feed", sourceName, items.Count);

            foreach (XmlNode item in items)
            {
                var title = item.SelectSingleNode("title")?.InnerText?.Trim() ?? "";
                // Extract link: try <link> text, then <guid>, then <link href="">
                var linkNode = item.SelectSingleNode("link");
                var link = linkNode?.InnerText?.Trim() ?? "";
                if (string.IsNullOrEmpty(link))
                    link = item.SelectSingleNode("guid")?.InnerText?.Trim() ?? "";
                if (string.IsNullOrEmpty(link))
                    link = linkNode?.Attributes?["href"]?.Value ?? "";
                var description = item.SelectSingleNode("description")?.InnerText?.Trim()
                    ?? item.SelectSingleNode("summary")?.InnerText?.Trim();
                var pubDateStr = item.SelectSingleNode("pubDate")?.InnerText
                    ?? item.SelectSingleNode("published")?.InnerText
                    ?? item.SelectSingleNode("updated")?.InnerText;

                if (string.IsNullOrEmpty(link)) continue;

                var pubDate = DateTime.TryParse(pubDateStr, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow;
                if (pubDate < fromDate) continue;
                if (to.HasValue && pubDate > to.Value) continue;

                var text = $"{title} {description}";
                var matched = keywords.Where(k =>
                    text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matched.Count == 0) continue;

                var articleId = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(link))).ToLower()[..16];

                articles.Add(new ArticleDto(
                    ArticleId: articleId,
                    Source: sourceName,
                    SourceUrl: feedUrl,
                    ArticleUrl: link,
                    Headline: title,
                    Subheadline: null,
                    Author: [],
                    PublishedAt: pubDate,
                    CrawledAt: DateTime.UtcNow,
                    KeywordsMatched: matched,
                    Summary: description,
                    FullText: null,
                    Category: null,
                    Sentiment: "neutral",
                    Tags: [],
                    Paywall: false
                ));
            }

            _logger.LogInformation("RSS {Source}: {Matched} articles matched keywords", sourceName, articles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RSS feed error for {Source} ({Url})", sourceName, feedUrl);
        }
        return articles;
    }
}
