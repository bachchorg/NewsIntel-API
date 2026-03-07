using System.Collections.Concurrent;

namespace NewsIntel.API.Services;

public class RateLimitTracker
{
    private readonly ConcurrentDictionary<string, RateLimitInfo> _limits = new();

    public void RecordRequest(string sourceName, int? remainingRequests = null, int? dailyLimit = null, DateTime? resetsAt = null)
    {
        _limits.AddOrUpdate(sourceName,
            _ => new RateLimitInfo(sourceName, remainingRequests, dailyLimit, DateTime.UtcNow, resetsAt, false),
            (_, existing) => existing with
            {
                RemainingRequests = remainingRequests ?? existing.RemainingRequests,
                DailyLimit = dailyLimit ?? existing.DailyLimit,
                LastRequestAt = DateTime.UtcNow,
                ResetsAt = resetsAt ?? existing.ResetsAt,
                IsExhausted = false
            });
    }

    public void MarkExhausted(string sourceName, int? dailyLimit = null, DateTime? resetsAt = null)
    {
        _limits.AddOrUpdate(sourceName,
            _ => new RateLimitInfo(sourceName, 0, dailyLimit, DateTime.UtcNow, resetsAt, true),
            (_, existing) => existing with
            {
                RemainingRequests = 0,
                DailyLimit = dailyLimit ?? existing.DailyLimit,
                LastRequestAt = DateTime.UtcNow,
                ResetsAt = resetsAt ?? existing.ResetsAt,
                IsExhausted = true
            });
    }

    public RateLimitInfo? GetInfo(string sourceName) =>
        _limits.TryGetValue(sourceName, out var info) ? info : null;

    public List<RateLimitInfo> GetAll() => _limits.Values.ToList();
}

public record RateLimitInfo(
    string SourceName,
    int? RemainingRequests,
    int? DailyLimit,
    DateTime LastRequestAt,
    DateTime? ResetsAt,
    bool IsExhausted
);
