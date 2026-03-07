using Microsoft.EntityFrameworkCore;
using NewsIntel.API.Models;

namespace NewsIntel.API.Data;

public class NewsIntelDbContext : DbContext
{
    public NewsIntelDbContext(DbContextOptions<NewsIntelDbContext> options) : base(options) { }

    public DbSet<CrawlSession> Sessions => Set<CrawlSession>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<SessionKeyword> Keywords => Set<SessionKeyword>();
    public DbSet<SessionSource> Sources => Set<SessionSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Article>().HasKey(a => new { a.ArticleId, a.CrawlSessionId });
        modelBuilder.Entity<Article>().HasIndex(a => new { a.CrawlSessionId, a.PublishedAt });
        modelBuilder.Entity<CrawlSession>().HasMany(s => s.Keywords).WithOne(k => k.CrawlSession).HasForeignKey(k => k.CrawlSessionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CrawlSession>().HasMany(s => s.Sources).WithOne(s => s.CrawlSession).HasForeignKey(s => s.CrawlSessionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CrawlSession>().HasMany(s => s.Articles).WithOne(a => a.CrawlSession).HasForeignKey(a => a.CrawlSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
