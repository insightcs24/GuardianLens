using GuardianLens.API.Data;
using GuardianLens.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GuardianLens.API.Services;

// ─── Match Index ─────────────────────────────────────────────────────────────

public interface IMatchIndexService
{
    void AddToIndex(int assetId, string pHash);
    List<(int AssetId, double Similarity)> FindMatches(string queryHash, double threshold = 0.90);
    void RebuildIndex(IEnumerable<(int id, string hash)> assets);
}

public class MatchIndexService : IMatchIndexService
{
    private readonly Dictionary<int, string> _index = new();
    private readonly FingerprintService _fp = new();

    public void AddToIndex(int assetId, string pHash) => _index[assetId] = pHash;

    public List<(int AssetId, double Similarity)> FindMatches(string queryHash, double threshold = 0.90)
        => _index
            .Select(kv => (kv.Key, Similarity: _fp.Similarity(queryHash, kv.Value)))
            .Where(x => x.Similarity >= threshold)
            .OrderByDescending(x => x.Similarity)
            .ToList();

    public void RebuildIndex(IEnumerable<(int id, string hash)> assets)
    {
        _index.Clear();
        foreach (var (id, hash) in assets) _index[id] = hash;
    }
}

// ─── Asset Service ────────────────────────────────────────────────────────────

public interface IAssetService
{
    Task<DigitalAsset> RegisterAssetAsync(AssetUploadRequest request);
    Task<DigitalAsset?> GetAssetAsync(int id);
    Task<List<DigitalAsset>> GetAllAssetsAsync();
    Task<DashboardStats> GetDashboardStatsAsync();
}

public class AssetService : IAssetService
{
    private readonly AppDbContext _db;
    private readonly IFingerprintService _fp;
    private readonly IWatermarkService _wm;
    private readonly IMatchIndexService _index;
    private readonly IBlockchainService _chain;
    private readonly IServiceScopeFactory _scopeFactory;

    public AssetService(AppDbContext db, IFingerprintService fp,
                        IWatermarkService wm, IMatchIndexService index,
                        IBlockchainService chain, IServiceScopeFactory scopeFactory)
    { _db = db; _fp = fp; _wm = wm; _index = index; _chain = chain; _scopeFactory = scopeFactory; }

    public async Task<DigitalAsset> RegisterAssetAsync(AssetUploadRequest request)
    {
        var imageBytes = Convert.FromBase64String(
            request.Base64Image.Contains(',')
                ? request.Base64Image.Split(',')[1]
                : request.Base64Image);

        string pHash = _fp.ComputePHash(imageBytes);
        string token = _wm.GenerateToken(request.Organization, Guid.NewGuid().ToString("N")[..8]);
        byte[] watermarkedBytes = _wm.EmbedWatermark(imageBytes, token);

        string dir = Path.Combine("wwwroot", "assets");
        Directory.CreateDirectory(dir);
        string fileName = $"{Guid.NewGuid():N}.png";
        string filePath = Path.Combine(dir, fileName);
        await File.WriteAllBytesAsync(filePath, watermarkedBytes);

        var asset = new DigitalAsset
        {
            Title = request.Title, Sport = request.Sport,
            Organization = request.Organization, Type = request.Type,
            PHash = pHash, WatermarkToken = token, FilePath = filePath
        };

        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        _index.AddToIndex(asset.Id, pHash);

        // ── Blockchain provenance — mint pHash commitment to Polygon ──────────
        // Fire-and-forget: runs AFTER the HTTP response is returned to the client.
        // Asset is saved to DB first, blockchain is best-effort.
        var assetId = asset.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                var receipt = await _chain.RegisterAssetAsync(
                    pHash, token, request.Organization);

                // Fresh DB scope for background update (correct DI pattern)
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var a  = await db.Assets.FindAsync(assetId);
                if (a is not null)
                {
                    a.BlockchainTxHash         = receipt.TxHash;
                    a.BlockchainCommitmentHash = receipt.CommitmentHash;
                    a.BlockchainBlockNumber    = receipt.BlockNumber;
                    a.BlockchainTimestamp      = receipt.Timestamp;
                    a.BlockchainNetwork        = receipt.Network;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // Blockchain failure NEVER blocks asset registration
                Console.WriteLine($"[Blockchain] Background registration failed: {ex.Message}");
            }
        });

        return asset;
    }

    public async Task<DigitalAsset?> GetAssetAsync(int id)
        => await _db.Assets.Include(a => a.Violations).FirstOrDefaultAsync(a => a.Id == id);

    public async Task<List<DigitalAsset>> GetAllAssetsAsync()
        => await _db.Assets.Include(a => a.Violations).ToListAsync();

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        var today = DateTime.UtcNow.Date;
        int totalAssets = await _db.Assets.CountAsync(a => a.IsActive);
        int activeViolations = await _db.Violations.CountAsync(v =>
            v.Status == ViolationStatus.Detected || v.Status == ViolationStatus.UnderReview);
        int takedownsSent = await _db.Violations.CountAsync(v =>
            v.Status == ViolationStatus.TakedownSent || v.Status == ViolationStatus.Resolved);
        int scansToday = await _db.ScanJobs.CountAsync(s => s.CreatedAt >= today);

        var byPlatform = await _db.Violations.Where(v => v.Platform != null)
            .GroupBy(v => v.Platform!)
            .Select(g => new { Platform = g.Key, Count = g.Count() })
            .ToListAsync();

        var colors = new[] { "1a6cf0", "e05a1e", "0fa06e", "9b45d4", "d4891a", "e24b4a" };
        var platformBreakdown = byPlatform
            .Select((p, i) => new PlatformBreakdown(p.Platform, p.Count, colors[i % colors.Length]))
            .ToList();

        var recent = await _db.Violations.Include(v => v.DigitalAsset)
            .OrderByDescending(v => v.DetectedAt).Take(10)
            .Select(v => new RecentViolationDto(
                v.Id, v.DigitalAsset!.Title, v.Platform ?? "Unknown",
                v.MatchConfidence, v.Status.ToString(), v.DetectedAt, v.InfringingUrl))
            .ToListAsync();

        return new DashboardStats(totalAssets, activeViolations, takedownsSent,
                                   scansToday, platformBreakdown, recent);
    }
}

// ─── Scan Service (FIXED — IServiceScopeFactory for background DB access) ─────

public interface IScanService
{
    Task<ScanJob> StartScanAsync(int assetId);
    Task<List<ScanJob>> GetScanJobsAsync(int assetId);
}

public class ScanService : IScanService
{
    private readonly AppDbContext _db;
    private readonly IMatchIndexService _index;
    private readonly IServiceScopeFactory _scopeFactory;

    public ScanService(AppDbContext db, IMatchIndexService index,
                       IServiceScopeFactory scopeFactory)
    { _db = db; _index = index; _scopeFactory = scopeFactory; }

    public async Task<ScanJob> StartScanAsync(int assetId)
    {
        var asset = await _db.Assets.FindAsync(assetId)
            ?? throw new KeyNotFoundException($"Asset {assetId} not found");

        var job = new ScanJob { DigitalAssetId = assetId, Status = ScanStatus.Running };
        _db.ScanJobs.Add(job);
        await _db.SaveChangesAsync();

        // Fire-and-forget background scan with its own DI scope
        _ = Task.Run(() => RunScanAsync(job.Id, asset.Id));
        return job;
    }

    public async Task<List<ScanJob>> GetScanJobsAsync(int assetId)
        => await _db.ScanJobs.Where(j => j.DigitalAssetId == assetId)
                              .OrderByDescending(j => j.CreatedAt).ToListAsync();

    private async Task RunScanAsync(int jobId, int assetId)
    {
        // Own DI scope so background task has its own DbContext — avoids concurrency issues
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await Task.Delay(TimeSpan.FromSeconds(3)); // Simulate crawl

        var rng = new Random();
        var platforms = new[] { "YouTube", "Twitter", "Instagram", "Telegram", "Reddit" };
        var found = platforms.Where(_ => rng.NextDouble() > 0.6).ToArray();

        foreach (var platform in found)
        {
            db.Violations.Add(new Violation
            {
                DigitalAssetId = assetId,
                Platform = platform,
                InfringingUrl = $"https://{platform.ToLower()}.com/pirated_{rng.Next(1000, 9999)}",
                MatchConfidence = Math.Round(0.87 + rng.NextDouble() * 0.12, 2),
                Status = ViolationStatus.Detected,
                DetectedAt = DateTime.UtcNow
            });
        }

        var job = await db.ScanJobs.FindAsync(jobId);
        if (job is not null)
        {
            job.Status = ScanStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.UrlsScanned = 400 + rng.Next(900);
            job.ViolationsFound = found.Length;
        }

        await db.SaveChangesAsync();
    }
}
