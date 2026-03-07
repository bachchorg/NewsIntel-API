using Microsoft.EntityFrameworkCore;
using NewsIntel.API.BackgroundServices;
using NewsIntel.API.Configuration;
using NewsIntel.API.Data;
using NewsIntel.API.Hubs;
using NewsIntel.API.Services;
using NewsIntel.API.Services.Interfaces;
using NewsIntel.API.Services.NewsApiClients;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();

var connectionString = builder.Configuration.GetConnectionString("Default");
var pgConnection = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("Postgres");

if (!string.IsNullOrEmpty(pgConnection))
{
    // Production: PostgreSQL (Render.com provides DATABASE_URL as postgres:// or postgresql://)
    var connStr = pgConnection.StartsWith("postgres://") || pgConnection.StartsWith("postgresql://")
        ? ConvertPostgresUrl(pgConnection)
        : pgConnection;
    Console.WriteLine($"[Startup] Using PostgreSQL. Host parsed: {(connStr.Contains("Host=") ? "yes" : "raw")}");
    builder.Services.AddDbContext<NewsIntelDbContext>(opts => opts.UseNpgsql(connStr));
}
else
{
    // Development: SQLite
    builder.Services.AddDbContext<NewsIntelDbContext>(opts =>
        opts.UseSqlite(connectionString ?? "Data Source=newsintell.db"));
}

builder.Services.Configure<NewsApiOptions>(opts =>
{
    builder.Configuration.GetSection("NewsApi").Bind(opts);
    // Environment variables override config file (for production)
    opts.NewsApiOrgKey = Environment.GetEnvironmentVariable("NEWSAPI_ORG_KEY") ?? opts.NewsApiOrgKey;
    opts.NewsApiOrgKeys = Environment.GetEnvironmentVariable("NEWSAPI_ORG_KEYS") ?? opts.NewsApiOrgKeys;
    opts.GNewsApiKey = Environment.GetEnvironmentVariable("GNEWS_API_KEY") ?? opts.GNewsApiKey;
    opts.TheNewsApiKey = Environment.GetEnvironmentVariable("THENEWSAPI_KEY") ?? opts.TheNewsApiKey;
});

builder.Services.AddHttpClient<NewsApiOrgClient>();
builder.Services.AddHttpClient<RssNewsApiClient>();
builder.Services.AddHttpClient<GNewsApiClient>();
builder.Services.AddHttpClient<TheNewsApiClient>();
// Use factory delegates so IEnumerable<INewsApiClient> resolves typed HttpClient instances correctly
builder.Services.AddTransient<INewsApiClient>(sp => sp.GetRequiredService<NewsApiOrgClient>());
builder.Services.AddTransient<INewsApiClient>(sp => sp.GetRequiredService<RssNewsApiClient>());
builder.Services.AddTransient<INewsApiClient>(sp => sp.GetRequiredService<GNewsApiClient>());
builder.Services.AddTransient<INewsApiClient>(sp => sp.GetRequiredService<TheNewsApiClient>());

builder.Services.AddScoped<CrawlSessionService>();
builder.Services.AddScoped<ArticleRepository>();
builder.Services.AddScoped<ArticleEnrichmentService>();
builder.Services.AddScoped<ISentimentAnalyzer, SentimentAnalyzer>();
builder.Services.AddSingleton<DeduplicationService>();
builder.Services.AddSingleton<SpikeDetectionService>();
builder.Services.AddSingleton<RateLimitTracker>();
builder.Services.AddHostedService<CrawlBackgroundService>();

var allowedOrigins = new List<string> { "http://localhost:3000" };
var extraOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS");
if (!string.IsNullOrEmpty(extraOrigins))
    allowedOrigins.AddRange(extraOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

builder.Services.AddCors(opts =>
{
    opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins.ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

// Auto-create DB schema on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NewsIntelDbContext>();
    db.Database.EnsureCreated();
}

// Bind to PORT env var for Render.com
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    app.Urls.Add($"http://0.0.0.0:{port}");

app.UseCors();
app.MapControllers();
app.MapHub<NewsHub>("/hubs/news");

app.Run();

// Convert postgres:// or postgresql:// URL (from Render) to Npgsql connection string
static string ConvertPostgresUrl(string url)
{
    try
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':');
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');
        var username = userInfo.Length > 0 ? userInfo[0] : "";
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        var result = $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
        Console.WriteLine($"[Startup] Parsed DB connection: Host={host}, Port={port}, Database={database}, User={username}");
        return result;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Failed to parse DATABASE_URL: {ex.Message}");
        throw;
    }
}
