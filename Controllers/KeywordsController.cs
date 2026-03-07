using Microsoft.AspNetCore.Mvc;
using NewsIntel.API.DTOs;
using NewsIntel.API.Services;

namespace NewsIntel.API.Controllers;

[ApiController]
[Route("api/sessions/{sessionId:guid}/keywords")]
public class KeywordsController : ControllerBase
{
    private readonly CrawlSessionService _sessions;

    public KeywordsController(CrawlSessionService sessions) => _sessions = sessions;

    [HttpGet]
    public async Task<IActionResult> Get(Guid sessionId) => Ok(await _sessions.GetKeywordsAsync(sessionId));

    [HttpPost]
    public async Task<IActionResult> Add(Guid sessionId, [FromBody] KeywordEntry entry)
    {
        var kw = await _sessions.AddKeywordAsync(sessionId, entry.Term, entry.Logic);
        return kw == null ? NotFound() : Ok(kw);
    }

    [HttpDelete("{keywordId:int}")]
    public async Task<IActionResult> Remove(Guid sessionId, int keywordId)
    {
        if (!await _sessions.RemoveKeywordAsync(keywordId)) return NotFound();
        return NoContent();
    }
}
