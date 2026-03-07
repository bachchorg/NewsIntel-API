namespace NewsIntel.API.Models;

public class SessionSource
{
    public int Id { get; set; }
    public Guid CrawlSessionId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public CrawlSession CrawlSession { get; set; } = null!;
}
