using System.ComponentModel.DataAnnotations;

namespace GuardianLens.API.Models;

// ─── Core Domain Models ──────────────────────────────────────────────────────

/// <summary>A registered, protected digital asset (image or video clip)</summary>
public class DigitalAsset
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string FilePath { get; set; } = string.Empty;

    public AssetType Type { get; set; }

    /// <summary>Perceptual hash (pHash) of the media for similarity matching</summary>
    [Required]
    public string PHash { get; set; } = string.Empty;

    /// <summary>Invisible watermark token embedded in the file</summary>
    public string? WatermarkToken { get; set; }

    public string? Sport { get; set; }
    public string? Organization { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // ─── Blockchain provenance ───────────────────────────────────────────────
    /// <summary>Polygon/EVM transaction hash written at registration time</summary>
    public string? BlockchainTxHash { get; set; }

    /// <summary>On-chain commitment hash (SHA-256 of pHash+token+org) stored in the contract</summary>
    public string? BlockchainCommitmentHash { get; set; }

    /// <summary>Block number when the registration was mined</summary>
    public long? BlockchainBlockNumber { get; set; }

    /// <summary>UTC timestamp of the on-chain registration (from block.timestamp)</summary>
    public DateTime? BlockchainTimestamp { get; set; }

    /// <summary>Chain name: "Polygon Mumbai" / "Polygon Mainnet" / "Simulated"</summary>
    public string? BlockchainNetwork { get; set; }

    // Navigation
    public List<Violation> Violations { get; set; } = new();
    public List<ScanJob> ScanJobs { get; set; } = new();
}

/// <summary>A detected unauthorized use of a protected asset</summary>
public class Violation
{
    public int Id { get; set; }
    public int DigitalAssetId { get; set; }
    public DigitalAsset? DigitalAsset { get; set; }

    [Required]
    public string InfringingUrl { get; set; } = string.Empty;

    public string? Platform { get; set; }          // e.g. "YouTube", "Twitter"
    public double MatchConfidence { get; set; }     // 0.0 – 1.0
    public ViolationStatus Status { get; set; } = ViolationStatus.Detected;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? TakedownReference { get; set; }

    /// <summary>On-chain transaction recording this DMCA evidence</summary>
    public string? BlockchainEvidenceTx { get; set; }

    // Thumbnail of the offending media (base64 or URL)
    public string? ThumbnailUrl { get; set; }
}

/// <summary>Background scan job that crawls for violations</summary>
public class ScanJob
{
    public int Id { get; set; }
    public int DigitalAssetId { get; set; }
    public DigitalAsset? DigitalAsset { get; set; }

    public ScanStatus Status { get; set; } = ScanStatus.Queued;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public int UrlsScanned { get; set; }
    public int ViolationsFound { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Comma-separated list of platforms scanned</summary>
    public string Platforms { get; set; } = "YouTube,Twitter,Instagram,Telegram";
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>
/// Payload for POST /api/assets
/// Changed from positional record to class — settable properties are required
/// for reliable System.Text.Json model binding when enum fields are present.
/// </summary>
public class AssetUploadRequest
{
    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Sport { get; set; } = string.Empty;

    [Required]
    public string Organization { get; set; } = string.Empty;

    public AssetType Type { get; set; } = AssetType.Image;

    /// <summary>Full data URI or raw base64. Both formats accepted.</summary>
    [Required]
    public string Base64Image { get; set; } = string.Empty;
}

public record ScanRequest(int AssetId, string[] Platforms);

public record TakedownRequest(int ViolationId, string Reason);

public record DashboardStats(
    int TotalAssets,
    int ActiveViolations,
    int TakedownsSent,
    int ScansRunToday,
    List<PlatformBreakdown> ByPlatform,
    List<RecentViolationDto> RecentViolations
);

public record PlatformBreakdown(string Platform, int Count, string Color);
public record RecentViolationDto(
    int Id, string AssetTitle, string Platform,
    double Confidence, string Status, DateTime DetectedAt, string InfringingUrl
);

// ─── Enums ───────────────────────────────────────────────────────────────────

public enum AssetType { Image, VideoClip, Highlight, Broadcast }

public enum ViolationStatus
{
    Detected,
    UnderReview,
    TakedownSent,
    Resolved,
    Disputed,
    FalsePositive
}

public enum ScanStatus { Queued, Running, Completed, Failed }
