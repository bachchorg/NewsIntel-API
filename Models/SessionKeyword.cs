namespace NewsIntel.API.Models;

public class SessionKeyword
{
    public int Id { get; set; }
    public Guid CrawlSessionId { get; set; }
    public string Term { get; set; } = string.Empty;
    public string Logic { get; set; } = "or";
    public CrawlSession CrawlSession { get; set; } = null!;
}
