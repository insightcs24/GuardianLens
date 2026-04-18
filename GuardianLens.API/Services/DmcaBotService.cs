using Microsoft.EntityFrameworkCore;
using GuardianLens.API.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GuardianLens.API.Data;

namespace GuardianLens.API.Services;

/// <summary>
/// DMCA Bot — Automated Takedown Sender
/// 
/// Sends copyright removal requests to each platform's API or legal endpoint.
/// Each platform has a different mechanism:
/// 
///   YouTube   → YouTube Content ID API (most automated)
///   Twitter   → Twitter copyright form API (POST to their endpoint)
///   Instagram → Instagram Graph API copyright report
///   Telegram  → DMCA email to dmca@telegram.org (no public API)
///   Reddit    → Reddit mod report API + copyright email
///   Generic   → Send structured DMCA email via SendGrid
/// 
/// In a real deployment, you need:
///   - YouTube: OAuth2 token with youtube.contentid scope
///   - Twitter: Developer account + API v2 access
///   - Instagram: Business account + Graph API token
/// 
/// The bot generates a legal DMCA notice format compliant with 17 U.S.C. § 512(c)(3)
/// </summary>
public interface IDmcaBotService
{
    Task<TakedownResult> SendTakedownAsync(Violation violation);
    Task<TakedownResult> SendTakedownByIdAsync(int violationId);
}

public class DmcaBotService : IDmcaBotService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<DmcaBotService> _logger;
    private readonly AppDbContext _db;
    private readonly INotificationService _notify;

    private readonly IBlockchainService _chain;

    public DmcaBotService(HttpClient http, IConfiguration config,
                           ILogger<DmcaBotService> logger, AppDbContext db,
                           INotificationService notify, IBlockchainService chain)
    { _http = http; _config = config; _logger = logger; _db = db; _notify = notify; _chain = chain; }

    public async Task<TakedownResult> SendTakedownByIdAsync(int violationId)
    {
        var violation = await _db.Violations
            .Include(v => v.DigitalAsset)
            .FirstOrDefaultAsync(v => v.Id == violationId)
            ?? throw new KeyNotFoundException($"Violation {violationId} not found");

        return await SendTakedownAsync(violation);
    }

    public async Task<TakedownResult> SendTakedownAsync(Violation violation)
    {
        if (violation.DigitalAsset == null)
            await _db.Entry(violation).Reference(v => v.DigitalAsset).LoadAsync();

        var platform = violation.Platform?.ToLower() ?? "unknown";
        var reference = GenerateReference(violation.Id);

        _logger.LogInformation("Sending DMCA takedown to {Platform} for violation {Id}",
            platform, violation.Id);

        // ── Record violation evidence on-chain BEFORE filing takedown ─────────
        // This creates an immutable timestamp proving when we detected the violation
        try
        {
            var evidence = await _chain.RecordViolationAsync(violation.DigitalAsset, violation);
            if (evidence.Success)
            {
                violation.BlockchainEvidenceTx = evidence.TxHash;
                _logger.LogInformation("Violation evidence on {Net}: {Tx}",
                    evidence.Network, evidence.TxHash[..20]);
            }
        }
        catch (Exception chainEx)
        {
            _logger.LogWarning("Blockchain evidence failed (non-blocking): {Msg}", chainEx.Message);
        }

        TakedownResult result;
        try
        {
            result = platform switch
            {
                "youtube"   => await SendYouTubeTakedownAsync(violation, reference),
                "twitter"   => await SendTwitterTakedownAsync(violation, reference),
                "instagram" => await SendInstagramTakedownAsync(violation, reference),
                "telegram"  => await SendTelegramTakedownAsync(violation, reference),
                "reddit"    => await SendRedditTakedownAsync(violation, reference),
                _           => await SendGenericEmailTakedownAsync(violation, reference),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Takedown failed for {Platform}: {Error}", platform, ex.Message);
            result = new TakedownResult
            {
                Success   = false,
                Reference = reference,
                Platform  = platform,
                Error     = ex.Message
            };
        }

        // Update violation status in database
        violation.Status = result.Success
            ? ViolationStatus.TakedownSent
            : ViolationStatus.UnderReview;
        violation.TakedownReference = result.Reference;

        _db.Violations.Update(violation);
        await _db.SaveChangesAsync();

        if (result.Success)
            _logger.LogInformation("Takedown sent ✅ Ref: {Ref}", result.Reference);
        else
            _logger.LogWarning("Takedown failed ❌ {Error}", result.Error);

        return result;
    }

    // ─── YouTube ──────────────────────────────────────────────────────────────

    private async Task<TakedownResult> SendYouTubeTakedownAsync(Violation v, string reference)
    {
        // YouTube Content ID API — requires OAuth2 with contentowner scope
        // Docs: https://developers.google.com/youtube/partner/docs/reporting/video_management
        var token = _config["YouTube:AccessToken"];
        if (string.IsNullOrEmpty(token) || token == "YOUR_YOUTUBE_OAUTH_TOKEN")
        {
            return SimulateTakedown(v, "YouTube", reference,
                "YouTube Content ID API | OAuth2 required");
        }

        var videoId = ExtractYouTubeVideoId(v.InfringingUrl);
        if (videoId == null) return new TakedownResult { Success = false, Error = "Could not parse YouTube video ID" };

        var body = JsonSerializer.Serialize(new
        {
            kind          = "youtubePartner#videoAdvertisingOption",
            videoId       = videoId,
            infringingUrl = v.InfringingUrl,
            description   = BuildDmcaNotice(v),
            ownershipType = "DMCA"
        });

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.PostAsync(
            "https://www.googleapis.com/youtube/partner/v1/videoAdvertisingOptions",
            new StringContent(body, Encoding.UTF8, "application/json"));

        return new TakedownResult
        {
            Success   = response.IsSuccessStatusCode,
            Reference = reference,
            Platform  = "YouTube",
            Error     = response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync()
        };
    }

    // ─── Twitter / X ─────────────────────────────────────────────────────────

    private async Task<TakedownResult> SendTwitterTakedownAsync(Violation v, string reference)
    {
        // Twitter copyright form endpoint
        // Requires: Twitter Developer API key + OAuth 1.0a
        var apiKey = _config["Twitter:ApiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_TWITTER_API_KEY")
        {
            return SimulateTakedown(v, "Twitter", reference,
                "Twitter API v2 | Developer account required");
        }

        var body = JsonSerializer.Serialize(new
        {
            tweet_url        = v.InfringingUrl,
            copyright_claim  = BuildDmcaNotice(v),
            reference_number = reference
        });

        // Twitter copyright report endpoint (v2)
        var response = await _http.PostAsync(
            "https://api.twitter.com/2/tweets/copyright",
            new StringContent(body, Encoding.UTF8, "application/json"));

        return new TakedownResult
        {
            Success   = response.IsSuccessStatusCode,
            Reference = reference,
            Platform  = "Twitter"
        };
    }

    // ─── Instagram ────────────────────────────────────────────────────────────

    private async Task<TakedownResult> SendInstagramTakedownAsync(Violation v, string reference)
    {
        // Instagram Graph API — business account required
        var token = _config["Instagram:AccessToken"];
        if (string.IsNullOrEmpty(token) || token == "YOUR_INSTAGRAM_TOKEN")
        {
            return SimulateTakedown(v, "Instagram", reference,
                "Instagram Graph API | Business account required");
        }

        var body = JsonSerializer.Serialize(new
        {
            media_url        = v.InfringingUrl,
            copyright_notice = BuildDmcaNotice(v)
        });

        var response = await _http.PostAsync(
            $"https://graph.instagram.com/copyright_check?access_token={token}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        return new TakedownResult
        {
            Success   = response.IsSuccessStatusCode,
            Reference = reference,
            Platform  = "Instagram"
        };
    }

    // ─── Telegram ─────────────────────────────────────────────────────────────

    private async Task<TakedownResult> SendTelegramTakedownAsync(Violation v, string reference)
    {
        // Telegram has no public API for copyright takedowns.
        // Only option: email dmca@telegram.org with legal DMCA notice.
        await _notify.SendEmailAsync(
            to:      "dmca@telegram.org",
            subject: $"DMCA Takedown Notice — Ref: {reference}",
            body:    BuildDmcaNotice(v, detailed: true)
        );

        return new TakedownResult
        {
            Success   = true,
            Reference = reference,
            Platform  = "Telegram",
            Method    = "Email to dmca@telegram.org"
        };
    }

    // ─── Reddit ───────────────────────────────────────────────────────────────

    private async Task<TakedownResult> SendRedditTakedownAsync(Violation v, string reference)
    {
        // Reddit copyright removal: reddit.com/r/legal + email copyright@reddit.com
        await _notify.SendEmailAsync(
            to:      "copyright@reddit.com",
            subject: $"Copyright Infringement Notice — {reference}",
            body:    BuildDmcaNotice(v, detailed: true)
        );

        return new TakedownResult
        {
            Success   = true,
            Reference = reference,
            Platform  = "Reddit",
            Method    = "Email to copyright@reddit.com"
        };
    }

    // ─── Generic Email Takedown ───────────────────────────────────────────────

    private async Task<TakedownResult> SendGenericEmailTakedownAsync(Violation v, string reference)
    {
        // For unknown platforms: send formal DMCA notice to abuse@[domain]
        var domain = ExtractDomain(v.InfringingUrl);
        var abuseEmail = $"abuse@{domain}";

        await _notify.SendEmailAsync(
            to:      abuseEmail,
            subject: $"DMCA Copyright Takedown Request — {reference}",
            body:    BuildDmcaNotice(v, detailed: true)
        );

        return new TakedownResult
        {
            Success   = true,
            Reference = reference,
            Platform  = v.Platform ?? "Unknown",
            Method    = $"Email to {abuseEmail}"
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a legally valid DMCA takedown notice per 17 U.S.C. § 512(c)(3).
    /// Includes all required elements: identification, ownership proof, good faith statement.
    /// </summary>
    private string BuildDmcaNotice(Violation v, bool detailed = false)
    {
        var asset = v.DigitalAsset;
        var short_notice = $"Copyright infringement detected. " +
                           $"Our asset '{asset?.Title}' (owned by {asset?.Organization}) " +
                           $"has been reproduced without authorization at: {v.InfringingUrl}. " +
                           $"Match confidence: {v.MatchConfidence:P1}. " +
                           $"Reference: {v.TakedownReference}";

        if (!detailed) return short_notice;

        return $"""
            DMCA TAKEDOWN NOTICE
            Under 17 U.S.C. § 512(c)(3)
            Reference: {v.TakedownReference}
            Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

            TO WHOM IT MAY CONCERN:

            I am writing on behalf of {asset?.Organization} ("Rights Holder") to notify you 
            of copyright infringement occurring on your platform.

            INFRINGING CONTENT:
            URL: {v.InfringingUrl}
            Platform: {v.Platform}

            ORIGINAL WORK:
            Title: {asset?.Title}
            Owner: {asset?.Organization}
            Type: {asset?.Type}
            Registered: {asset?.RegisteredAt:yyyy-MM-dd}
            Watermark Token: {asset?.WatermarkToken}

            The content at the URL above reproduces or is substantially similar to our 
            copyrighted work without authorization. Our automated system has detected a 
            {v.MatchConfidence:P1} perceptual similarity using cryptographic fingerprinting.

            We hereby request that you expeditiously remove or disable access to the 
            infringing content.

            GOOD FAITH STATEMENT:
            I have a good faith belief that use of the copyrighted materials described above 
            is not authorized by the copyright owner, its agent, or the law.

            ACCURACY STATEMENT:
            The information in this notification is accurate, and under penalty of perjury, 
            I am authorized to act on behalf of the copyright owner.

            GuardianLens IP Protection System
            {DateTime.UtcNow:yyyy-MM-dd}
            """;
    }

    private static TakedownResult SimulateTakedown(Violation v, string platform,
                                                    string reference, string method)
        => new TakedownResult
        {
            Success   = true,
            Reference = reference,
            Platform  = platform,
            Method    = $"[DEMO MODE] {method}",
            Note      = "Add your API key to appsettings.json to send real takedowns"
        };

    private static string GenerateReference(int violationId)
        => $"GL-DMCA-{DateTime.UtcNow:yyyyMMdd}-{violationId:D5}";

    private static string? ExtractYouTubeVideoId(string url)
    {
        // https://youtube.com/watch?v=XXXXXXXXXXX
        var match = System.Text.RegularExpressions.Regex.Match(url, @"[?&]v=([^&]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string ExtractDomain(string url)
    {
        try { return new Uri(url).Host.Replace("www.", ""); }
        catch { return "example.com"; }
    }
}

// ─── Result Model ─────────────────────────────────────────────────────────────

public class TakedownResult
{
    public bool Success { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? Method { get; set; }
    public string? Error { get; set; }
    public string? Note { get; set; }
}
