using GuardianLens.API.Data;
using GuardianLens.API.Models;
using GuardianLens.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GuardianLens.API.Controllers;

// ═══════════════════════════════════════════════════════════════════════════
// ASSETS CONTROLLER  —  /api/assets
// ═══════════════════════════════════════════════════════════════════════════

[ApiController, Route("api/assets")]
public class AssetsController : ControllerBase
{
    private readonly IAssetService _assets;
    public AssetsController(IAssetService assets) => _assets = assets;

    /// <summary>List all registered protected assets</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _assets.GetAllAssetsAsync());

    /// <summary>Get one asset with its violations</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var asset = await _assets.GetAssetAsync(id);
        return asset is null ? NotFound() : Ok(asset);
    }

    /// <summary>
    /// Register a new asset — computes pHash, embeds invisible watermark.
    /// Send image as base64 in body.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] AssetUploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Base64Image))
            return BadRequest(new { error = "Base64Image is required" });

        var asset = await _assets.RegisterAssetAsync(request);
        return CreatedAtAction(nameof(Get), new { id = asset.Id }, asset);
    }

    /// <summary>Dashboard summary stats</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
        => Ok(await _assets.GetDashboardStatsAsync());
}

// ═══════════════════════════════════════════════════════════════════════════
// SCANS CONTROLLER  —  /api/scans
// ═══════════════════════════════════════════════════════════════════════════

[ApiController, Route("api/scans")]
public class ScansController : ControllerBase
{
    private readonly IScanService _scans;
    public ScansController(IScanService scans) => _scans = scans;

    /// <summary>Start a background scan for an asset</summary>
    [HttpPost("{assetId:int}")]
    public async Task<IActionResult> StartScan(int assetId)
    {
        var job = await _scans.StartScanAsync(assetId);
        return Accepted(job);
    }

    /// <summary>Get all scan jobs for an asset</summary>
    [HttpGet("{assetId:int}")]
    public async Task<IActionResult> GetJobs(int assetId)
        => Ok(await _scans.GetScanJobsAsync(assetId));

    /// <summary>Get ALL scan jobs across all assets (for the Scan Jobs page)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromServices] AppDbContext db)
        => Ok(await db.ScanJobs
                      .Include(j => j.DigitalAsset)
                      .OrderByDescending(j => j.CreatedAt)
                      .Take(50)
                      .ToListAsync());
}

// ═══════════════════════════════════════════════════════════════════════════
// VIOLATIONS CONTROLLER  —  /api/violations
// ═══════════════════════════════════════════════════════════════════════════

[ApiController, Route("api/violations")]
public class ViolationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDmcaBotService _dmca;
    private readonly IAlertEngineService _alert;
    private readonly IFingerprintService _fp;
    private readonly IMatchIndexService _index;

    public ViolationsController(AppDbContext db, IDmcaBotService dmca,
        IAlertEngineService alert, IFingerprintService fp, IMatchIndexService index)
    { _db = db; _dmca = dmca; _alert = alert; _fp = fp; _index = index; }

    /// <summary>List all violations, optionally filtered by status</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ViolationStatus? status = null)
    {
        var q = _db.Violations.Include(v => v.DigitalAsset).AsQueryable();
        if (status.HasValue) q = q.Where(v => v.Status == status.Value);
        return Ok(await q.OrderByDescending(v => v.DetectedAt).ToListAsync());
    }

    /// <summary>
    /// Send DMCA takedown — calls the correct platform API automatically.
    /// Returns a reference number for tracking.
    /// </summary>
    [HttpPost("{id:int}/takedown")]
    public async Task<IActionResult> SendTakedown(int id)
    {
        var result = await _dmca.SendTakedownByIdAsync(id);
        return result.Success
            ? Ok(new { message = "Takedown sent", reference = result.Reference,
                        platform = result.Platform, method = result.Method })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>Dismiss a violation as false positive</summary>
    [HttpPost("{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id)
    {
        var v = await _db.Violations.FindAsync(id);
        if (v is null) return NotFound();
        v.Status = ViolationStatus.FalsePositive;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Dismissed" });
    }

    /// <summary>
    /// Called by the Python crawler to report a match it found.
    /// Also called by the Google Vision service.
    /// </summary>
    [HttpPost("report")]
    public async Task<IActionResult> ReportFromCrawler([FromBody] CrawlerReport report)
    {
        var match = new MatchResult
        {
            AssetId       = report.AssetId,
            Platform      = report.Platform,
            InfringingUrl = report.InfringingUrl,
            Confidence    = report.MatchConfidence,
            FoundByMethod = report.DetectedByCrawler ? "Crawler" : "Manual"
        };

        // Run through the full alert pipeline (auto-takedown if ≥97%)
        await _alert.ProcessMatchAsync(match);
        return Ok(new { message = "Reported and processed" });
    }

    /// <summary>
    /// Verify ownership — upload an image and check if it matches any registered asset.
    /// Used for the demo "Verify" page in the React dashboard.
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(
        [FromBody] string base64Image,
        [FromServices] IReverseImageSearchService vision)
    {
        if (string.IsNullOrWhiteSpace(base64Image))
            return BadRequest(new { error = "base64Image required" });

        // pHash match against in-memory index
        string queryHash = _fp.ComputePHashFromBase64(base64Image);
        var matches = _index.FindMatches(queryHash, threshold: 0.85);

        if (!matches.Any())
            return Ok(new { isMatch = false, message = "No registered asset match found." });

        var top = matches.First();
        var asset = await _db.Assets.FindAsync(top.AssetId);

        return Ok(new
        {
            isMatch        = true,
            confidence     = Math.Round(top.Similarity * 100, 1),
            assetId        = asset?.Id,
            assetTitle     = asset?.Title,
            organization   = asset?.Organization,
            watermarkToken = asset?.WatermarkToken,
            queryHash,
            matchedHash    = asset?.PHash
        });
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ALERTS CONTROLLER  —  /api/alerts
// ═══════════════════════════════════════════════════════════════════════════

[ApiController, Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly IAlertEngineService _alert;
    public AlertsController(IAlertEngineService alert) => _alert = alert;

    /// <summary>Recent alert events (auto-takedowns, high alerts, watchlist entries)</summary>
    [HttpGet]
    public async Task<IActionResult> GetRecent([FromQuery] int count = 20)
        => Ok(await _alert.GetRecentAlertsAsync(count));

    /// <summary>
    /// Manually submit a match result through the alert engine.
    /// Useful for testing thresholds or integrating external scan results.
    /// </summary>
    [HttpPost("process")]
    public async Task<IActionResult> Process([FromBody] MatchResult match)
    {
        await _alert.ProcessMatchAsync(match);
        return Ok(new { message = "Match processed through alert engine" });
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// VISION CONTROLLER  —  /api/vision
// ═══════════════════════════════════════════════════════════════════════════

[ApiController, Route("api/vision")]
public class VisionController : ControllerBase
{
    private readonly IReverseImageSearchService _vision;
    private readonly IAlertEngineService _alert;
    private readonly AppDbContext _db;

    public VisionController(IReverseImageSearchService vision,
                             IAlertEngineService alert, AppDbContext db)
    { _vision = vision; _alert = alert; _db = db; }

    /// <summary>
    /// Run Google Vision reverse image search for an asset.
    /// Returns matching pages and submits any high-confidence finds to the alert engine.
    /// </summary>
    [HttpPost("{assetId:int}/search")]
    public async Task<IActionResult> Search(int assetId)
    {
        var asset = await _db.Assets.FindAsync(assetId);
        if (asset is null) return NotFound();

        // Load the registered asset image bytes
        byte[]? imageBytes = null;
        if (System.IO.File.Exists(asset.FilePath))
            imageBytes = await System.IO.File.ReadAllBytesAsync(asset.FilePath);

        if (imageBytes is null)
            return BadRequest(new { error = "Asset file not found on disk" });

        var matches = await _vision.SearchByImageAsync(imageBytes);

        // Submit high-confidence Vision matches through alert pipeline
        var tasks = matches
            .Where(m => m.Score >= 0.90)
            .Select(m => _alert.ProcessMatchAsync(new MatchResult
            {
                AssetId       = assetId,
                Platform      = m.Platform,
                InfringingUrl = m.Url,
                Confidence    = m.Score,
                FoundByMethod = "GoogleVision"
            }));
        await Task.WhenAll(tasks);

        return Ok(new
        {
            assetId,
            totalFound     = matches.Count,
            highConfidence = matches.Count(m => m.Score >= 0.90),
            results        = matches
        });
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// SUPPORTING DTOs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Payload sent by the Python crawler when it finds a match</summary>
public record CrawlerReport(
    int    AssetId,
    string Platform,
    string InfringingUrl,
    double MatchConfidence,
    bool   DetectedByCrawler = true
);
// ═══════════════════════════════════════════════════════════════════════════
// BLOCKCHAIN CONTROLLER  —  /api/blockchain
// ═══════════════════════════════════════════════════════════════════════════

[ApiController, Route("api/blockchain")]
public class BlockchainController : ControllerBase
{
    private readonly IBlockchainService _chain;
    private readonly AppDbContext _db;

    public BlockchainController(IBlockchainService chain, AppDbContext db)
    { _chain = chain; _db = db; }

    /// <summary>Get blockchain service status and network info</summary>
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        isConfigured = _chain.IsConfigured,
        network      = _chain.NetworkName,
        mode         = _chain.IsConfigured ? "Live" : "Simulation",
        message      = _chain.IsConfigured
            ? $"Connected to {_chain.NetworkName}"
            : "Running in simulation mode. Add Blockchain keys to appsettings.json for live minting."
    });

    /// <summary>Manually mint/re-mint an asset that wasn't captured on first registration</summary>
    [HttpPost("mint/{assetId:int}")]
    public async Task<IActionResult> MintAsset(int assetId)
    {
        var asset = await _db.Assets.FindAsync(assetId);
        if (asset is null) return NotFound();

        var receipt = await _chain.RegisterAssetAsync(
            asset.PHash, asset.WatermarkToken ?? "", asset.Organization ?? "");

        asset.BlockchainTxHash         = receipt.TxHash;
        asset.BlockchainCommitmentHash = receipt.CommitmentHash;
        asset.BlockchainBlockNumber    = receipt.BlockNumber;
        asset.BlockchainTimestamp      = receipt.Timestamp;
        asset.BlockchainNetwork        = receipt.Network;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message        = "Asset minted on blockchain",
            txHash         = receipt.TxHash,
            explorerUrl    = receipt.ExplorerUrl,
            blockNumber    = receipt.BlockNumber,
            network        = receipt.Network,
            isSimulated    = receipt.IsSimulated,
            commitmentHash = receipt.CommitmentHash
        });
    }

    /// <summary>Verify ownership of an asset on-chain (read-only, free)</summary>
    [HttpGet("verify/{assetId:int}")]
    public async Task<IActionResult> VerifyOnChain(int assetId)
    {
        var asset = await _db.Assets.FindAsync(assetId);
        if (asset is null) return NotFound();

        var proof = await _chain.VerifyOwnershipAsync(
            asset.PHash, asset.WatermarkToken ?? "", asset.Organization ?? "");

        return Ok(new
        {
            assetId        = assetId,
            assetTitle     = asset.Title,
            commitmentHash = proof.CommitmentHash,
            exists         = proof.Exists,
            organisation   = proof.Organization,
            registeredAt   = proof.RegisteredAt,
            network        = proof.Network,
            explorerUrl    = proof.ExplorerUrl,
            isSimulated    = proof.IsSimulated,
            error          = proof.Error
        });
    }
}
