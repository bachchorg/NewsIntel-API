using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NewsIntel.API.Services;

public class DeduplicationService
{
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _sessionSeen = new();

    public string ComputeArticleId(string url)
    {
        var normalized = NormalizeUrl(url);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLower()[..16];
    }

    public bool IsSeen(string articleId, Guid sessionId)
    {
        var set = _sessionSeen.GetOrAdd(sessionId, _ => new HashSet<string>());
        return set.Contains(articleId);
    }

    public void MarkSeen(string articleId, Guid sessionId)
    {
        var set = _sessionSeen.GetOrAdd(sessionId, _ => new HashSet<string>());
        set.Add(articleId);
    }

    public void InitSession(Guid sessionId, IEnumerable<string> existingIds)
    {
        var set = _sessionSeen.GetOrAdd(sessionId, _ => new HashSet<string>());
        foreach (var id in existingIds) set.Add(id);
    }

    public void ClearSession(Guid sessionId) => _sessionSeen.TryRemove(sessionId, out _);

    private static string NormalizeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var query = uri.Query;
            query = Regex.Replace(query, @"[?&]utm_[^&]*", "");
            return (uri.Scheme + "://" + uri.Host + uri.AbsolutePath + query).ToLower().TrimEnd('/');
        }
        catch { return url.ToLower(); }
    }
}
