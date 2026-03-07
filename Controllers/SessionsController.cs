using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NewsIntel.API.DTOs;
using NewsIntel.API.Hubs;
using NewsIntel.API.Services;

namespace NewsIntel.API.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly CrawlSessionService _sessions;
    private readonly ArticleRepository _articles;
    private readonly IHubContext<NewsHub> _hub;
    private readonly DeduplicationService _dedup;

    public SessionsController(CrawlSessionService sessions, ArticleRepository articles, IHubContext<NewsHub> hub, DeduplicationService dedup)
    {
        _sessions = sessions;
        _articles = articles;
        _hub = hub;
        _dedup = dedup;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _sessions.GetAllAsync());

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var s = await _sessions.GetByIdAsync(id);
        return s == null ? NotFound() : Ok(s);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateSessionRequest req)
    {
        var s = await _sessions.CreateAsync(req);
        return CreatedAtAction(nameof(Get), new { id = s.Id }, s);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateSessionRequest req)
    {
        var s = await _sessions.UpdateAsync(id, req);
        if (s == null) return NotFound();
        // Clear dedup cache so re-crawl with new settings can find articles again
        _dedup.ClearSession(id);
        return Ok(s);
    }

    [HttpPut("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id)
    {
        if (!await _sessions.StartAsync(id)) return NotFound();
        var s = await _sessions.GetByIdAsync(id);
        await _hub.Clients.Group(id.ToString()).SendAsync("SessionStateChanged", new { sessionId = id.ToString(), state = s!.State });
        return NoContent();
    }

    [HttpPut("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id)
    {
        if (!await _sessions.PauseAsync(id)) return NotFound();
        await _hub.Clients.Group(id.ToString()).SendAsync("SessionStateChanged", new { sessionId = id.ToString(), state = "Paused" });
        return NoContent();
    }

    [HttpPut("{id:guid}/stop")]
    public async Task<IActionResult> Stop(Guid id)
    {
        if (!await _sessions.StopAsync(id)) return NotFound();
        await _hub.Clients.Group(id.ToString()).SendAsync("SessionStateChanged", new { sessionId = id.ToString(), state = "Stopped" });
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _sessions.DeleteAsync(id)) return NotFound();
        return NoContent();
    }

    [HttpGet("{id:guid}/analytics")]
    public async Task<IActionResult> GetAnalytics(Guid id) => Ok(await _articles.GetAnalyticsAsync(id));

    [HttpGet("rate-limits")]
    public IActionResult GetRateLimits([FromServices] RateLimitTracker rateLimits) => Ok(rateLimits.GetAll());
}
