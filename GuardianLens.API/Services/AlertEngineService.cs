using GuardianLens.API.Data;
using GuardianLens.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GuardianLens.API.Services;

/// <summary>
/// Alert Engine — evaluates incoming matches and fires the right response.
/// 
/// Three escalation levels:
///   AUTO-TAKEDOWN  (≥ 97%):  File DMCA immediately, no human needed
///   HIGH ALERT     (≥ 90%):  Create violation, notify rights manager NOW
///   WATCH LIST     (≥ 82%):  Log for review — might be legitimate fair use
/// 
/// This service is called by the scan pipeline after every pHash match found.
/// </summary>
public interface IAlertEngineService
{
    Task ProcessMatchAsync(MatchResult match);
    Task<List<AlertEvent>> GetRecentAlertsAsync(int count = 20);
}

public class AlertEngineService : IAlertEngineService
{
    // Configurable thresholds — set in appsettings.json
    private readonly double _autoTakedownThreshold;
    private readonly double _highAlertThreshold;
    private readonly double _watchlistThreshold;

    private readonly AppDbContext _db;
    private readonly IDmcaBotService _dmca;
    private readonly INotificationService _notify;
    private readonly ILogger<AlertEngineService> _logger;

    public AlertEngineService(
        AppDbContext db,
        IDmcaBotService dmca,
        INotificationService notify,
        IConfiguration config,
        ILogger<AlertEngineService> logger)
    {
        _db = db; _dmca = dmca; _notify = notify; _logger = logger;

        // Read thresholds from config (with sensible defaults)
        _autoTakedownThreshold = config.GetValue<double>("AlertEngine:AutoTakedownThreshold", 0.97);
        _highAlertThreshold    = config.GetValue<double>("AlertEngine:HighAlertThreshold", 0.90);
        _watchlistThreshold    = config.GetValue<double>("AlertEngine:WatchlistThreshold", 0.82);
    }

    public async Task ProcessMatchAsync(MatchResult match)
    {
        _logger.LogInformation(
            "Processing match: Asset={AssetId} Platform={Platform} Confidence={Confidence:P1}",
            match.AssetId, match.Platform, match.Confidence);

        // ── Level 3: Auto-Takedown ────────────────────────────────────────────
        if (match.Confidence >= _autoTakedownThreshold)
        {
            _logger.LogWarning("AUTO-TAKEDOWN triggered for {Url}", match.InfringingUrl);

            var violation = await CreateViolationAsync(match, ViolationStatus.TakedownSent);

            // Fire DMCA immediately — no human review needed at this confidence
            await _dmca.SendTakedownAsync(violation);

            // Notify rights manager (high severity)
            await _notify.SendAlertAsync(new AlertPayload
            {
                AssetId    = match.AssetId,
                ViolationId = violation.Id,
                Message    = $"AUTO-TAKEDOWN filed: {match.Confidence:P0} match on {match.Platform}",
                Severity   = AlertSeverity.Critical,
                Url        = match.InfringingUrl
            });

            await LogAlertEventAsync(violation.Id, "AutoTakedown",
                $"Auto-DMCA filed for {match.InfringingUrl}");
        }

        // ── Level 2: High Alert ───────────────────────────────────────────────
        else if (match.Confidence >= _highAlertThreshold)
        {
            _logger.LogWarning("HIGH ALERT: {Confidence:P1} match on {Platform}",
                match.Confidence, match.Platform);

            var violation = await CreateViolationAsync(match, ViolationStatus.Detected);

            // Notify immediately — rights manager must review
            await _notify.SendAlertAsync(new AlertPayload
            {
                AssetId     = match.AssetId,
                ViolationId = violation.Id,
                Message     = $"VIOLATION DETECTED: {match.Confidence:P0} on {match.Platform} — review required",
                Severity    = AlertSeverity.High,
                Url         = match.InfringingUrl
            });

            await LogAlertEventAsync(violation.Id, "HighAlert",
                $"{match.Confidence:P0} match requires review");
        }

        // ── Level 1: Watchlist ────────────────────────────────────────────────
        else if (match.Confidence >= _watchlistThreshold)
        {
            _logger.LogInformation("WATCHLIST: {Confidence:P1} match on {Platform} — queued for review",
                match.Confidence, match.Platform);

            var violation = await CreateViolationAsync(match, ViolationStatus.UnderReview);

            // Lower severity notification — daily digest, not immediate
            await _notify.SendAlertAsync(new AlertPayload
            {
                AssetId     = match.AssetId,
                ViolationId = violation.Id,
                Message     = $"Low-confidence match ({match.Confidence:P0}) added to watchlist",
                Severity    = AlertSeverity.Low,
                Url         = match.InfringingUrl
            });
        }

        else
        {
            _logger.LogDebug("Match below watchlist threshold ({Conf:P1}) — ignored", match.Confidence);
        }
    }

    private async Task<Violation> CreateViolationAsync(MatchResult match, ViolationStatus status)
    {
        // Check for duplicate — don't create same violation twice
        var existing = await _db.Violations
            .FirstOrDefaultAsync(v => v.InfringingUrl == match.InfringingUrl
                                   && v.DigitalAssetId == match.AssetId);

        if (existing != null)
        {
            _logger.LogDebug("Duplicate violation skipped: {Url}", match.InfringingUrl);
            return existing;
        }

        var violation = new Violation
        {
            DigitalAssetId  = match.AssetId,
            Platform        = match.Platform,
            InfringingUrl   = match.InfringingUrl,
            MatchConfidence = match.Confidence,
            Status          = status,
            DetectedAt      = DateTime.UtcNow,
            ThumbnailUrl    = match.ThumbnailUrl
        };

        _db.Violations.Add(violation);
        await _db.SaveChangesAsync();
        return violation;
    }

    private async Task LogAlertEventAsync(int violationId, string type, string message)
    {
        _db.AlertEvents.Add(new AlertEvent
        {
            ViolationId = violationId,
            EventType   = type,
            Message     = message,
            OccurredAt  = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<AlertEvent>> GetRecentAlertsAsync(int count = 20)
        => await _db.AlertEvents
                    .OrderByDescending(a => a.OccurredAt)
                    .Take(count)
                    .ToListAsync();
}

// ─── Supporting Models ────────────────────────────────────────────────────────

/// <summary>Result from a pHash scan or Vision API reverse search</summary>
public class MatchResult
{
    public int AssetId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string InfringingUrl { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? FoundByMethod { get; set; }   // "pHash", "GoogleVision", "Crawler"
}

public class AlertPayload
{
    public int AssetId { get; set; }
    public int ViolationId { get; set; }
    public string Message { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Url { get; set; } = string.Empty;
}

public enum AlertSeverity { Low, Medium, High, Critical }

/// <summary>Persisted log entry for every alert that fired</summary>
public class AlertEvent
{
    public int Id { get; set; }
    public int ViolationId { get; set; }
    public string EventType { get; set; } = string.Empty;   // "AutoTakedown", "HighAlert", etc.
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
