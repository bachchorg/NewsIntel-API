using Microsoft.AspNetCore.Mvc;
using NewsIntel.API.Services;

namespace NewsIntel.API.Controllers;

[ApiController]
[Route("api/sessions/{sessionId:guid}/articles")]
public class ArticlesController : ControllerBase
{
    private readonly ArticleRepository _articles;

    public ArticlesController(ArticleRepository articles) => _articles = articles;

    [HttpGet]
    public async Task<IActionResult> Get(Guid sessionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var articles = await _articles.GetArticlesAsync(sessionId, page, pageSize);
        var total = await _articles.CountAsync(sessionId);
        return Ok(new { articles, total, page, pageSize });
    }
}
