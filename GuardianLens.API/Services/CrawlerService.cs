using GuardianLens.API.Models;
using System.Text.Json;

namespace GuardianLens.API.Services;

/// <summary>
/// Crawler service that searches for potential violations.
/// 
/// Production implementation would:
///   1. Query Google Custom Search API for reverse image search
///   2. Scrape YouTube, Twitter, Instagram APIs for media matching
///   3. Monitor known piracy sites via Playwright headless browser
///   4. Compare found images using pHash matching
/// 
/// This prototype simulates API calls with realistic delay + result structure.
/// </summary>
public interface ICrawlerService
{
    Task<List<PotentialMatch>> SearchForMatchesAsync(string pHash, string[] platforms);
}

public class CrawlerService : ICrawlerService
{
    private readonly HttpClient _http;
    private readonly IFingerprintService _fp;
    private readonly ILogger<CrawlerService> _logger;

    // Simulated violation URLs for the demo (in production, these come from real crawl)
    private static readonly string[] DemoViolationUrls =
    {
        "https://youtube.com/watch?v=DEMO_pirated_clip_1",
        "https://twitter.com/pirate_acc/status/123456789",
        "https://t.me/sportsclipleaks/456",
        "https://reddit.com/r/sports/comments/demo_post",
        "https://instagram.com/p/ABCDEF_demo",
    };

    private static readonly string[] DemoPlatforms =
        { "YouTube", "Twitter", "Telegram", "Reddit", "Instagram" };

    public CrawlerService(HttpClient http, IFingerprintService fp,
                          ILogger<CrawlerService> logger)
    {
        _http = http; _fp = fp; _logger = logger;
    }

    public async Task<List<PotentialMatch>> SearchForMatchesAsync(
        string pHash, string[] platforms)
    {
        _logger.LogInformation("Starting scan for pHash: {PHash}", pHash);
        var results = new List<PotentialMatch>();

        foreach (var platform in platforms)
        {
            try
            {
                var matches = await ScanPlatformAsync(platform, pHash);
                results.AddRange(matches);
                await Task.Delay(500);  // Throttle between platforms
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Platform scan failed for {Platform}: {Error}",
                                    platform, ex.Message);
            }
        }

        return results;
    }

    private async Task<List<PotentialMatch>> ScanPlatformAsync(
        string platform, string originalHash)
    {
        // In production: call platform-specific APIs
        // e.g., YouTube Data API, Twitter v2 media search, etc.

        await Task.Delay(200 + new Random().Next(300));  // Simulate network latency

        var rng = new Random(originalHash.GetHashCode() ^ platform.GetHashCode());

        if (rng.NextDouble() > 0.65)  // 35% chance of finding a violation per platform
            return new List<PotentialMatch>();

        int platformIdx = Array.IndexOf(DemoPlatforms, platform);
        if (platformIdx < 0) platformIdx = 0;

        double confidence = 0.85 + rng.NextDouble() * 0.14;  // 85-99% confidence

        return new List<PotentialMatch>
        {
            new PotentialMatch
            {
                Platform = platform,
                Url = DemoViolationUrls[platformIdx % DemoViolationUrls.Length],
                MatchConfidence = Math.Round(confidence, 2),
                DetectedHash = MutatePHash(originalHash, rng, (int)((1 - confidence) * 64))
            }
        };
    }

    /// <summary>Simulate a slightly different pHash (as if content was re-encoded)</summary>
    private static string MutatePHash(string pHash, Random rng, int bitsToFlip)
    {
        ulong hash = Convert.ToUInt64(pHash, 16);
        for (int i = 0; i < bitsToFlip; i++)
        {
            int bit = rng.Next(64);
            hash ^= 1UL << bit;
        }
        return hash.ToString("X16");
    }
}

public record PotentialMatch
{
    public string Platform { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public double MatchConfidence { get; init; }
    public string DetectedHash { get; init; } = string.Empty;
}
