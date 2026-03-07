namespace NewsIntel.API.Models;

public class Article
{
    public string ArticleId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string ArticleUrl { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string? Subheadline { get; set; }
    public string AuthorsJson { get; set; } = "[]";
    public DateTime PublishedAt { get; set; }
    public DateTime CrawledAt { get; set; }
    public string KeywordsMatchedJson { get; set; } = "[]";
    public string? Summary { get; set; }
    public string? FullText { get; set; }
    public string? Category { get; set; }
    public string Sentiment { get; set; } = "neutral";
    public string TagsJson { get; set; } = "[]";
    public bool Paywall { get; set; }
    public Guid CrawlSessionId { get; set; }
    public CrawlSession CrawlSession { get; set; } = null!;
}
