using NewsIntel.API.DTOs;
using NewsIntel.API.Services.Interfaces;

namespace NewsIntel.API.Services;

public class ArticleEnrichmentService
{
    private readonly ISentimentAnalyzer _sentiment;

    public ArticleEnrichmentService(ISentimentAnalyzer sentiment) => _sentiment = sentiment;

    public ArticleDto Enrich(ArticleDto raw, IEnumerable<string> sessionKeywords)
    {
        var text = $"{raw.Headline} {raw.Summary}";
        var sentiment = _sentiment.Analyze(text);
        var matched = sessionKeywords.Where(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
        var category = ClassifyCategory(text);
        return raw with { Sentiment = sentiment, KeywordsMatched = matched, Category = category ?? raw.Category };
    }

    private static string? ClassifyCategory(string text)
    {
        var lower = text.ToLower();
        if (lower.Contains("election") || lower.Contains("president") || lower.Contains("senate") || lower.Contains("congress")) return "Politics";
        if (lower.Contains("fed") || lower.Contains("rate") || lower.Contains("stock") || lower.Contains("market") || lower.Contains("economy")) return "Finance";
        if (lower.Contains("ai") || lower.Contains("tech") || lower.Contains("software") || lower.Contains("apple") || lower.Contains("google")) return "Technology";
        if (lower.Contains("health") || lower.Contains("covid") || lower.Contains("vaccine") || lower.Contains("medical")) return "Health";
        if (lower.Contains("war") || lower.Contains("military") || lower.Contains("nato") || lower.Contains("ukraine")) return "World";
        return null;
    }
}
