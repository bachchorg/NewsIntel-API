namespace NewsIntel.API.Configuration;

public class NewsApiOptions
{
    public string NytApiKey { get; set; } = string.Empty;
    public string GuardianApiKey { get; set; } = string.Empty;
    public string NewsApiOrgKey { get; set; } = string.Empty;
    /// <summary>Comma-separated list of NewsAPI.org keys for rotation</summary>
    public string NewsApiOrgKeys { get; set; } = string.Empty;
    public string GNewsApiKey { get; set; } = string.Empty;
    public string TheNewsApiKey { get; set; } = string.Empty;
}
