using GuardianLens.API.Data;
using GuardianLens.API.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Run correctly as a Windows Service (SCM name must match deploy scripts)
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "GuardianLensAPI";
});

// ─── Core Services ────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // Fix 1: Break circular references (DigitalAsset ↔ Violation ↔ DigitalAsset)
        opts.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;

        // Fix 2: Serialize enums as strings ("Highlight" not 2) so frontend receives readable values
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());

        // Extra: skip null properties to keep responses lean
        opts.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "GuardianLens API",
        Version     = "v1",
        Description = "Digital Asset Protection Platform — pHash + Watermark + DMCA Automation"
    });
});

builder.Services.AddCors(options =>
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyHeader().AllowAnyMethod()));

// ─── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default") ?? "Data Source=guardianlens.db"));

// ─── Domain Services (Scoped — new instance per HTTP request) ─────────────────
builder.Services.AddScoped<IFingerprintService,         FingerprintService>();
builder.Services.AddScoped<IWatermarkService,           WatermarkService>();
builder.Services.AddScoped<IAssetService,               AssetService>();
builder.Services.AddScoped<IScanService,                ScanService>();
builder.Services.AddScoped<IAlertEngineService,         AlertEngineService>();
builder.Services.AddScoped<IDmcaBotService,             DmcaBotService>();
builder.Services.AddScoped<IReverseImageSearchService,  GoogleVisionService>();
builder.Services.AddScoped<IBlockchainService, BlockchainService>();

// ─── Singleton Services (one instance for app lifetime) ───────────────────────
// MatchIndexService holds in-memory pHash lookup table — must be singleton
builder.Services.AddSingleton<IMatchIndexService, MatchIndexService>();

// ─── Notification Service (singleton with its own HttpClient) ─────────────────
builder.Services.AddScoped<INotificationService, NotificationService>();

// ─── Named HttpClients for each external service ─────────────────────────────
builder.Services.AddHttpClient<ICrawlerService,             CrawlerService>();
builder.Services.AddHttpClient<BlockchainService>();
builder.Services.AddHttpClient<IReverseImageSearchService,  GoogleVisionService>();
builder.Services.AddHttpClient<IDmcaBotService,             DmcaBotService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<NotificationService>();

var app = builder.Build();

// ─── Startup: migrate DB + seed demo data + rebuild pHash index ───────────────
using (var scope = app.Services.CreateScope())
{
    var db      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var index   = app.Services.GetRequiredService<IMatchIndexService>();
    var logger  = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    db.Database.EnsureCreated();
    DbSeeder.Seed(db);

    // Pre-load all registered asset hashes into the in-memory similarity index
    var assets = db.Assets.Select(a => new { a.Id, a.PHash }).ToList();
    index.RebuildIndex(assets.Select(a => (a.Id, a.PHash)));
    logger.LogInformation("pHash index loaded with {Count} assets", assets.Count);
}

// ─── Middleware pipeline ──────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "GuardianLens API v1");
    c.RoutePrefix = "swagger";   // http://localhost:5000/swagger
});

app.UseCors("AllowFrontend");
app.UseAuthorization();
app.MapControllers();

app.Run();
