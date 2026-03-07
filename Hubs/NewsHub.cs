using Microsoft.AspNetCore.SignalR;
using NewsIntel.API.Services;

namespace NewsIntel.API.Hubs;

public class NewsHub : Hub
{
    private readonly CrawlSessionService _sessionService;

    public NewsHub(CrawlSessionService sessionService) => _sessionService = sessionService;

    public async Task JoinSession(string sessionId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

    public async Task LeaveSession(string sessionId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);

    public async Task AddKeyword(string sessionId, string term, string logic)
    {
        var kw = await _sessionService.AddKeywordAsync(Guid.Parse(sessionId), term, logic);
        if (kw != null)
        {
            var keywords = await _sessionService.GetKeywordsAsync(Guid.Parse(sessionId));
            await Clients.Group(sessionId).SendAsync("KeywordsUpdated", keywords);
        }
    }

    public async Task RemoveKeyword(string sessionId, int keywordId)
    {
        await _sessionService.RemoveKeywordAsync(keywordId);
        var keywords = await _sessionService.GetKeywordsAsync(Guid.Parse(sessionId));
        await Clients.Group(sessionId).SendAsync("KeywordsUpdated", keywords);
    }

    public async Task PauseSource(string sessionId, string sourceName)
    {
        await _sessionService.ToggleSourceAsync(Guid.Parse(sessionId), sourceName, false);
        await Clients.Group(sessionId).SendAsync("SourcesUpdated", sourceName, false);
    }

    public async Task ResumeSource(string sessionId, string sourceName)
    {
        await _sessionService.ToggleSourceAsync(Guid.Parse(sessionId), sourceName, true);
        await Clients.Group(sessionId).SendAsync("SourcesUpdated", sourceName, true);
    }
}
