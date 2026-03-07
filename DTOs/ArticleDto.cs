namespace NewsIntel.API.DTOs;

public record ArticleDto(
    string ArticleId,
    string Source,
    string SourceUrl,
    string ArticleUrl,
    string Headline,
    string? Subheadline,
    List<string> Author,
    DateTime PublishedAt,
    DateTime CrawledAt,
    List<string> KeywordsMatched,
    string? Summary,
    string? FullText,
    string? Category,
    string Sentiment,
    List<string> Tags,
    bool Paywall
);
