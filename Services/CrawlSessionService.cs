using Microsoft.EntityFrameworkCore;
using NewsIntel.API.Data;
using NewsIntel.API.DTOs;
using NewsIntel.API.Models;

namespace NewsIntel.API.Services;

public class CrawlSessionService
{
    private readonly NewsIntelDbContext _db;

    public CrawlSessionService(NewsIntelDbContext db) => _db = db;

    public async Task<List<SessionDto>> GetAllAsync()
    {
        var sessions = await _db.Sessions.Include(s => s.Keywords).Include(s => s.Sources).OrderByDescending(s => s.CreatedAt).ToListAsync();
        return sessions.Select(ToDto).ToList();
    }

    public async Task<SessionDto?> GetByIdAsync(Guid id)
    {
        var s = await _db.Sessions.Include(s => s.Keywords).Include(s => s.Sources).FirstOrDefaultAsync(s => s.Id == id);
        return s == null ? null : ToDto(s);
    }

    public async Task<SessionDto> CreateAsync(CreateSessionRequest req)
    {
        var session = new CrawlSession
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            State = SessionState.Idle,
            CreatedAt = DateTime.UtcNow,
            DateRangeFrom = req.DateRangeFrom,
            DateRangeTo = req.DateRangeTo,
            PollIntervalSeconds = req.PollIntervalSeconds,
            Keywords = req.Keywords.Select(k => new SessionKeyword { Term = k.Term, Logic = k.Logic }).ToList(),
            Sources = req.Sources.Select(s => new SessionSource { SourceName = s, IsEnabled = true }).ToList()
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();
        return ToDto(session);
    }

    public async Task<bool> StartAsync(Guid id)
    {
        var s = await _db.Sessions.FindAsync(id);
        if (s == null) return false;
        s.State = s.DateRangeFrom.HasValue ? SessionState.Backfilling : SessionState.Live;
        s.StartedAt = DateTime.UtcNow;
        s.LastPolledAt = s.DateRangeFrom ?? DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> PauseAsync(Guid id)
    {
        var s = await _db.Sessions.FindAsync(id);
        if (s == null) return false;
        s.State = SessionState.Paused;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> StopAsync(Guid id)
    {
        var s = await _db.Sessions.FindAsync(id);
        if (s == null) return false;
        s.State = SessionState.Stopped;
        s.StoppedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var s = await _db.Sessions.FindAsync(id);
        if (s == null) return false;
        _db.Sessions.Remove(s);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<SessionDto?> UpdateAsync(Guid id, UpdateSessionRequest req)
    {
        var s = await _db.Sessions.Include(s => s.Keywords).Include(s => s.Sources).FirstOrDefaultAsync(s => s.Id == id);
        if (s == null) return null;

        if (req.Name != null) s.Name = req.Name;
        if (req.PollIntervalSeconds.HasValue) s.PollIntervalSeconds = req.PollIntervalSeconds.Value;

        // Track whether date range changed so we can reset the poll cursor
        var dateRangeChanged = s.DateRangeFrom != req.DateRangeFrom || s.DateRangeTo != req.DateRangeTo;
        s.DateRangeFrom = req.DateRangeFrom;
        s.DateRangeTo = req.DateRangeTo;

        if (req.Keywords != null)
        {
            _db.Keywords.RemoveRange(s.Keywords);
            s.Keywords = req.Keywords.Select(k => new SessionKeyword { CrawlSessionId = id, Term = k.Term, Logic = k.Logic }).ToList();
        }

        // Reset poll cursor when keywords or date range change so the session re-crawls
        if (req.Keywords != null || dateRangeChanged)
        {
            s.LastPolledAt = null;
        }

        if (req.Sources != null)
        {
            _db.Sources.RemoveRange(s.Sources);
            s.Sources = req.Sources.Select(src => new SessionSource { CrawlSessionId = id, SourceName = src, IsEnabled = true }).ToList();
        }

        await _db.SaveChangesAsync();
        return ToDto(s);
    }

    public async Task<SessionKeywordDto?> AddKeywordAsync(Guid sessionId, string term, string logic)
    {
        var s = await _db.Sessions.FindAsync(sessionId);
        if (s == null) return null;
        var kw = new SessionKeyword { CrawlSessionId = sessionId, Term = term, Logic = logic };
        _db.Keywords.Add(kw);
        await _db.SaveChangesAsync();
        return new SessionKeywordDto(kw.Id, kw.Term, kw.Logic);
    }

    public async Task<bool> RemoveKeywordAsync(int keywordId)
    {
        var kw = await _db.Keywords.FindAsync(keywordId);
        if (kw == null) return false;
        _db.Keywords.Remove(kw);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<SessionKeywordDto>> GetKeywordsAsync(Guid sessionId)
    {
        var kws = await _db.Keywords.Where(k => k.CrawlSessionId == sessionId).ToListAsync();
        return kws.Select(k => new SessionKeywordDto(k.Id, k.Term, k.Logic)).ToList();
    }

    public async Task<bool> ToggleSourceAsync(Guid sessionId, string sourceName, bool enabled)
    {
        var src = await _db.Sources.FirstOrDefaultAsync(s => s.CrawlSessionId == sessionId && s.SourceName == sourceName);
        if (src == null) return false;
        src.IsEnabled = enabled;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<CrawlSession>> GetActiveSessionsAsync() =>
        await _db.Sessions.Include(s => s.Keywords).Include(s => s.Sources)
            .Where(s => s.State == SessionState.Live || s.State == SessionState.Backfilling)
            .ToListAsync();

    public async Task UpdateLastPolledAsync(Guid id, DateTime polledAt)
    {
        var s = await _db.Sessions.FindAsync(id);
        if (s != null) { s.LastPolledAt = polledAt; await _db.SaveChangesAsync(); }
    }

    public async Task TransitionToLiveAsync(Guid id)
    {
        var s = await _db.Sessions.FindAsync(id);
        if (s != null) { s.State = SessionState.Live; await _db.SaveChangesAsync(); }
    }

    private static SessionDto ToDto(CrawlSession s) => new(
        s.Id, s.Name, s.State.ToString(), s.CreatedAt, s.StartedAt, s.StoppedAt,
        s.DateRangeFrom, s.DateRangeTo, s.PollIntervalSeconds,
        s.Keywords.Select(k => new SessionKeywordDto(k.Id, k.Term, k.Logic)).ToList(),
        s.Sources.Select(src => new SessionSourceDto(src.Id, src.SourceName, src.IsEnabled)).ToList()
    );
}
