using NewsIntel.API.DTOs;

namespace NewsIntel.API.Services.Interfaces;

public interface INewsApiClient
{
    string SourceName { get; }
    /// <summary>All names this client responds to (for matching session source names)</summary>
    string[] KnownAliases => [SourceName];
    /// <summary>Minimum seconds between polls for this client (to avoid rate limits)</summary>
    int MinPollIntervalSeconds => 30;
    Task<IEnumerable<ArticleDto>> FetchArticlesAsync(IEnumerable<string> keywords, DateTime? from, DateTime? to, CancellationToken ct = default);
}
