using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// ngrok (and similar) terminate TLS and forward HTTP to localhost with X-Forwarded-* headers.
// Trust only loopback — remote clients cannot spoof forwarded headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                               | ForwardedHeaders.XForwardedProto
                               | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 2;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

// ── Windows Service support ──────────────────────────────────────────────────
// This single line makes the app run as a proper Windows Service.
// When run interactively (dotnet run), it behaves like a normal console app.
builder.Host.UseWindowsService(options =>
{
    // Must match Windows SCM service name (see deploy/windows/scripts/remote-sync-windows-services.ps1)
    options.ServiceName = "GuardianLensProxy";
});

// ── Read configuration ───────────────────────────────────────────────────────
var apiPort      = builder.Configuration.GetValue<int>("Proxy:ApiPort",      5000);
var proxyPort    = builder.Configuration.GetValue<int>("Proxy:ProxyPort",    80);
var frontendPath = builder.Configuration["Proxy:FrontendStaticPath"]
                   ?? Path.Combine(AppContext.BaseDirectory, "wwwroot", "app");
var acmeWebRoot = builder.Configuration["Proxy:AcmeWebRoot"];
var allowedOrigins = builder.Configuration.GetSection("Proxy:AllowedOrigins")
                             .Get<string[]>()
                  ?? new[] { "*" };

// ── Ports: HTTP on 80, HTTPS on 443 ─────────────────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP — always available
    options.Listen(IPAddress.Any, proxyPort);

    // HTTPS — enabled only when certificate is configured
    var certPath = builder.Configuration["Proxy:CertPath"];
    var certPass = builder.Configuration["Proxy:CertPassword"];
    if (!string.IsNullOrEmpty(certPath) && File.Exists(certPath))
    {
        options.Listen(IPAddress.Any, 443, listenOptions =>
        {
            listenOptions.UseHttps(certPath, certPass);
        });
    }
});

// ── CORS ─────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
    options.AddPolicy("AllowAll", policy =>
    {
        if (allowedOrigins.Contains("*"))
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }));

// ── YARP Reverse Proxy ───────────────────────────────────────────────────────
// Reads routing config from appsettings.json "ReverseProxy" section
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// ── Middleware pipeline ──────────────────────────────────────────────────────
// Must run first so Request.Scheme / Host reflect ngrok HTTPS (X-Forwarded-Proto / Host).
app.UseForwardedHeaders();

app.UseCors("AllowAll");

// Let's Encrypt HTTP-01 must stay on plain HTTP — never redirect /.well-known to HTTPS
var certPath2 = builder.Configuration["Proxy:CertPath"];
var certConfigured = !string.IsNullOrEmpty(certPath2) && File.Exists(certPath2);
if (certConfigured)
{
    app.UseWhen(
        ctx => !ctx.Request.Path.StartsWithSegments("/.well-known"),
        branch =>
        {
            branch.UseHttpsRedirection();
            branch.UseHsts();
        });
}

// ACME HTTP-01: win-acme --webroot = AcmeWebRoot; tokens are written under .well-known/acme-challenge/
// Mount only that path so MapFallbackToFile does not return index.html for challenge URLs.
if (!string.IsNullOrWhiteSpace(acmeWebRoot))
{
    var acmeChallengeDir = Path.Combine(acmeWebRoot, ".well-known", "acme-challenge");
    if (Directory.Exists(acmeChallengeDir))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(acmeChallengeDir),
            RequestPath = "/.well-known/acme-challenge"
        });
    }
}

// ── Serve React static files ─────────────────────────────────────────────────
// YARP handles /api/* routes. Everything else falls through to static files.
// This means both the API and the React frontend are served from ONE process.
if (Directory.Exists(frontendPath))
{
    var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendPath);

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider  = fileProvider,
        RequestPath   = "",
        // Long-term caching for hashed assets (JS/CSS with content hash in filename)
        OnPrepareResponse = ctx =>
        {
            var path = ctx.File.Name;
            if (path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".woff2"))
                ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
            else
                ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=0";
        }
    });

    // SPA fallback — any unmatched route serves index.html (React client-side routing)
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = fileProvider
    });
}
else
{
    app.Logger.LogWarning(
        "Frontend static path not found: {Path}. " +
        "React files not served. Set Proxy:FrontendStaticPath in appsettings.json.",
        frontendPath);
}

// ── YARP routes (/api/* → .NET API on port 5000) ─────────────────────────────
app.MapReverseProxy();

app.Logger.LogInformation(
    "GuardianLens Proxy started. HTTP port: {Port}. API backend: localhost:{ApiPort}",
    proxyPort, apiPort);

app.Run();
