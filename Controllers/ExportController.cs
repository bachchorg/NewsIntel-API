using Microsoft.AspNetCore.Mvc;
using NewsIntel.API.Services;
using System.Text;
using System.Text.Json;

namespace NewsIntel.API.Controllers;

[ApiController]
[Route("api/sessions/{sessionId:guid}")]
public class ExportController : ControllerBase
{
    private readonly ArticleRepository _articles;
    private readonly CrawlSessionService _sessions;

    public ExportController(ArticleRepository articles, CrawlSessionService sessions)
    {
        _articles = articles;
        _sessions = sessions;
    }

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv(Guid sessionId)
    {
        var articles = await _articles.GetAllAsync(sessionId);
        var sb = new StringBuilder();
        sb.AppendLine("article_id,source,headline,published_at,sentiment,keywords_matched,article_url,paywall");
        foreach (var a in articles)
        {
            var kw = string.Join("|", a.KeywordsMatched);
            sb.AppendLine($"\"{a.ArticleId}\",\"{a.Source}\",\"{a.Headline.Replace("\"", "\"\"")}\",\"{a.PublishedAt:O}\",\"{a.Sentiment}\",\"{kw}\",\"{a.ArticleUrl}\",{a.Paywall}");
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"session-{sessionId}-export.csv");
    }

    [HttpGet("export/json")]
    public async Task<IActionResult> ExportJson(Guid sessionId)
    {
        var articles = await _articles.GetAllAsync(sessionId);
        var json = JsonSerializer.Serialize(articles, new JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"session-{sessionId}-export.json");
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(Guid sessionId)
    {
        var session = await _sessions.GetByIdAsync(sessionId);
        if (session == null) return NotFound();
        var analytics = await _articles.GetAnalyticsAsync(sessionId);
        var articles = await _articles.GetArticlesAsync(sessionId, 1, 5);

        var summary = $"""
            NEWS INTELLIGENCE BRIEF
            Session: {session.Name} | Keywords: {string.Join(", ", session.Keywords.Select(k => k.Term))}
            Total Articles: {analytics.TotalArticles} across {analytics.SourceDistribution.Count} sources

            TOP STORIES:
            {string.Join("\n", articles.Take(5).Select((a, i) => $"{i + 1}. {a.Headline} — {a.Source} ({a.PublishedAt:g})\n   {a.Summary ?? "No summary available"}"))}

            KEYWORD TRENDS:
            {string.Join("\n", analytics.KeywordFrequencies.Take(5).Select(k => $"- \"{k.Keyword}\": {k.Count} articles"))}

            SOURCE BREAKDOWN:
            {string.Join("\n", analytics.SourceDistribution.Select(s => $"- {s.Source}: {s.Count} articles ({s.Percentage}%)"))}
            """;

        return Ok(new { summary, generatedAt = DateTime.UtcNow });
    }
}
