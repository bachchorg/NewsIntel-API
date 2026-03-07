using Microsoft.EntityFrameworkCore;
using NewsIntel.API.Data;
using NewsIntel.API.DTOs;
using NewsIntel.API.Models;
using System.Text.Json;

namespace NewsIntel.API.Services;

public class ArticleRepository
{
    private readonly NewsIntelDbContext _db;

    public ArticleRepository(NewsIntelDbContext db) => _db = db;

    public async Task<List<string>> GetSeenIdsAsync(Guid sessionId) =>
        await _db.Articles.Where(a => a.CrawlSessionId == sessionId).Select(a => a.ArticleId).ToListAsync();

    public async Task SaveArticlesAsync(Guid sessionId, IEnumerable<ArticleDto> articles)
    {
        foreach (var dto in articles)
        {
            var exists = await _db.Articles.AnyAsync(a => a.ArticleId == dto.ArticleId && a.CrawlSessionId == sessionId);
            if (exists) continue;

            _db.Articles.Add(new Article
            {
                ArticleId = dto.ArticleId,
                Source = dto.Source,
                SourceUrl = dto.SourceUrl,
                ArticleUrl = dto.ArticleUrl,
                Headline = dto.Headline,
                Subheadline = dto.Subheadline,
                AuthorsJson = JsonSerializer.Serialize(dto.Author),
                PublishedAt = dto.PublishedAt,
                CrawledAt = dto.CrawledAt,
                KeywordsMatchedJson = JsonSerializer.Serialize(dto.KeywordsMatched),
                Summary = dto.Summary,
                FullText = dto.FullText,
                Category = dto.Category,
                Sentiment = dto.Sentiment,
                TagsJson = JsonSerializer.Serialize(dto.Tags),
                Paywall = dto.Paywall,
                CrawlSessionId = sessionId
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<List<ArticleDto>> GetArticlesAsync(Guid sessionId, int page = 1, int pageSize = 50)
    {
        return await _db.Articles
            .Where(a => a.CrawlSessionId == sessionId)
            .OrderByDescending(a => a.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => ToDto(a))
            .ToListAsync();
    }

    public async Task<int> CountAsync(Guid sessionId) =>
        await _db.Articles.CountAsync(a => a.CrawlSessionId == sessionId);

    public async Task<List<ArticleDto>> GetAllAsync(Guid sessionId) =>
        await _db.Articles.Where(a => a.CrawlSessionId == sessionId).Select(a => ToDto(a)).ToListAsync();

    public async Task<AnalyticsSnapshot> GetAnalyticsAsync(Guid sessionId)
    {
        var articles = await _db.Articles.Where(a => a.CrawlSessionId == sessionId).ToListAsync();
        var total = articles.Count;

        var kwFreq = articles
            .SelectMany(a => JsonSerializer.Deserialize<List<string>>(a.KeywordsMatchedJson) ?? [])
            .GroupBy(k => k)
            .Select(g => new KeywordFrequencyDto(g.Key, g.Count()))
            .OrderByDescending(k => k.Count)
            .ToList();

        var srcDist = articles.GroupBy(a => a.Source)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();
        var srcDistDtos = srcDist.Select(x => new SourceDistributionDto(x.Source, x.Count, total > 0 ? Math.Round((double)x.Count / total * 100, 1) : 0)).ToList();

        var since = DateTime.UtcNow.AddHours(-6);
        var sentTrend = articles
            .Where(a => a.PublishedAt >= since)
            .GroupBy(a => new DateTime(a.PublishedAt.Year, a.PublishedAt.Month, a.PublishedAt.Day, a.PublishedAt.Hour, (a.PublishedAt.Minute / 30) * 30, 0))
            .OrderBy(g => g.Key)
            .Select(g => new SentimentTrendDto(
                g.Key.ToString("HH:mm"),
                g.Count(a => a.Sentiment == "positive"),
                g.Count(a => a.Sentiment == "neutral"),
                g.Count(a => a.Sentiment == "negative")
            )).ToList();

        return new AnalyticsSnapshot(kwFreq, srcDistDtos, sentTrend, total);
    }

    private static ArticleDto ToDto(Article a) => new(
        a.ArticleId, a.Source, a.SourceUrl, a.ArticleUrl, a.Headline, a.Subheadline,
        JsonSerializer.Deserialize<List<string>>(a.AuthorsJson) ?? [],
        a.PublishedAt, a.CrawledAt,
        JsonSerializer.Deserialize<List<string>>(a.KeywordsMatchedJson) ?? [],
        a.Summary, a.FullText, a.Category, a.Sentiment,
        JsonSerializer.Deserialize<List<string>>(a.TagsJson) ?? [],
        a.Paywall
    );
}
