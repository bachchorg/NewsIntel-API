namespace NewsIntel.API.Models;

public enum SessionState { Idle, Backfilling, Live, Paused, Stopped }

public class CrawlSession
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SessionState State { get; set; } = SessionState.Idle;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }
    public DateTime? DateRangeFrom { get; set; }
    public DateTime? DateRangeTo { get; set; }
    public DateTime? LastPolledAt { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public ICollection<SessionKeyword> Keywords { get; set; } = new List<SessionKeyword>();
    public ICollection<SessionSource> Sources { get; set; } = new List<SessionSource>();
    public ICollection<Article> Articles { get; set; } = new List<Article>();
}
