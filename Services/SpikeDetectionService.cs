using System.Collections.Concurrent;
using NewsIntel.API.DTOs;

namespace NewsIntel.API.Services;

public class SpikeDetectionService
{
    private readonly ConcurrentDictionary<(Guid sessionId, string keyword), Queue<DateTime>> _timestamps = new();

    public IEnumerable<SpikeAlertDto> DetectSpikes(Guid sessionId, IEnumerable<ArticleDto> newArticles, IEnumerable<string> keywords)
    {
        var now = DateTime.UtcNow;
        var alerts = new List<SpikeAlertDto>();

        foreach (var keyword in keywords)
        {
            var key = (sessionId, keyword);
            var queue = _timestamps.GetOrAdd(key, _ => new Queue<DateTime>());

            // Add new article timestamps for this keyword
            foreach (var article in newArticles.Where(a => a.KeywordsMatched.Contains(keyword, StringComparer.OrdinalIgnoreCase)))
                queue.Enqueue(article.PublishedAt);

            // Purge older than 2 hours
            while (queue.Count > 0 && queue.Peek() < now.AddHours(-2))
                queue.Dequeue();

            var previousWindow = queue.Count(t => t >= now.AddHours(-2) && t < now.AddHours(-1));
            var currentWindow = queue.Count(t => t >= now.AddHours(-1));

            if (previousWindow > 0 && currentWindow > previousWindow * 3.0)
            {
                alerts.Add(new SpikeAlertDto(
                    keyword, previousWindow, currentWindow,
                    Math.Round((double)(currentWindow - previousWindow) / previousWindow * 100, 1),
                    now
                ));
            }
        }
        return alerts;
    }

    public void ClearSession(Guid sessionId)
    {
        var keysToRemove = _timestamps.Keys.Where(k => k.sessionId == sessionId).ToList();
        foreach (var k in keysToRemove) _timestamps.TryRemove(k, out _);
    }
}
