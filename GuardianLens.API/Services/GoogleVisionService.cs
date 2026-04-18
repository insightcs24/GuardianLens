using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GuardianLens.API.Models;

namespace GuardianLens.API.Services;

/// <summary>
/// Reverse Image Search using Google Cloud Vision API (Web Detection feature).
/// 
/// How to get your API key:
///   1. Go to console.cloud.google.com
///   2. Create a project → Enable "Cloud Vision API"
///   3. APIs & Services → Credentials → Create API Key
///   4. Set the key with dotnet user-secrets, env var GoogleVision__ApiKey, or EC2 secrets — do not commit keys.
/// 
/// What it returns:
///   - fullMatchingImages:    Pages with the EXACT same image
///   - partialMatchingImages: Pages with cropped/modified versions
///   - webEntities:           Keywords associated with the image (e.g. "IPL", "Cricket")
///   - pagesWithMatchingImages: Web pages containing the image
/// </summary>
public interface IReverseImageSearchService
{
    Task<List<WebMatch>> SearchByImageAsync(byte[] imageBytes);
    Task<List<WebMatch>> SearchByImageBase64Async(string base64Image);
}

public class GoogleVisionService : IReverseImageSearchService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleVisionService> _logger;

    private const string VisionApiUrl = "https://vision.googleapis.com/v1/images:annotate";

    public GoogleVisionService(HttpClient http, IConfiguration config,
                                ILogger<GoogleVisionService> logger)
    {
        _http = http; _config = config; _logger = logger;
    }

    public async Task<List<WebMatch>> SearchByImageAsync(byte[] imageBytes)
        => await SearchByImageBase64Async(Convert.ToBase64String(imageBytes));

    public async Task<List<WebMatch>> SearchByImageBase64Async(string base64Image)
    {
        var apiKey = _config["GoogleVision:ApiKey"];

        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_GOOGLE_VISION_API_KEY")
        {
            _logger.LogWarning("Google Vision API key not configured — returning demo results");
            return GetDemoResults();
        }

        // Strip data URI prefix if present
        if (base64Image.Contains(','))
            base64Image = base64Image.Split(',')[1];

        // Build the Vision API request body
        var requestBody = new
        {
            requests = new[]
            {
                new
                {
                    image = new { content = base64Image },
                    features = new[]
                    {
                        new { type = "WEB_DETECTION", maxResults = 20 }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync($"{VisionApiUrl}?key={apiKey}", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            return ParseVisionResponse(responseJson);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Google Vision API error: {Message}", ex.Message);
            return new List<WebMatch>();
        }
    }

    private static List<WebMatch> ParseVisionResponse(string json)
    {
        var results = new List<WebMatch>();

        using var doc = JsonDocument.Parse(json);
        var webDetection = doc.RootElement
            .GetProperty("responses")[0]
            .GetProperty("webDetection");

        // Full matches (exact copies)
        if (webDetection.TryGetProperty("fullMatchingImages", out var fullMatches))
        {
            foreach (var match in fullMatches.EnumerateArray())
            {
                results.Add(new WebMatch
                {
                    Url = match.GetProperty("url").GetString() ?? "",
                    MatchType = MatchType.FullMatch,
                    Score = match.TryGetProperty("score", out var s) ? s.GetDouble() : 1.0
                });
            }
        }

        // Partial matches (cropped, modified)
        if (webDetection.TryGetProperty("partialMatchingImages", out var partialMatches))
        {
            foreach (var match in partialMatches.EnumerateArray())
            {
                results.Add(new WebMatch
                {
                    Url = match.GetProperty("url").GetString() ?? "",
                    MatchType = MatchType.PartialMatch,
                    Score = match.TryGetProperty("score", out var s) ? s.GetDouble() : 0.75
                });
            }
        }

        // Pages containing the image (gives us context + source URL)
        if (webDetection.TryGetProperty("pagesWithMatchingImages", out var pages))
        {
            foreach (var page in pages.EnumerateArray())
            {
                var pageUrl = page.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (pageUrl != null)
                {
                    results.Add(new WebMatch
                    {
                        Url = pageUrl,
                        MatchType = MatchType.PageWithImage,
                        Score = 0.8,
                        PageTitle = page.TryGetProperty("pageTitle", out var t)
                                    ? t.GetString() : null
                    });
                }
            }
        }

        return results;
    }

    // Demo results returned when no API key is configured
    private static List<WebMatch> GetDemoResults() => new()
    {
        new WebMatch { Url = "https://youtube.com/watch?v=demo001", MatchType = MatchType.FullMatch, Score = 0.98 },
        new WebMatch { Url = "https://t.me/sportleaks/5678", MatchType = MatchType.PartialMatch, Score = 0.87 },
        new WebMatch { Url = "https://reddit.com/r/cricket/demo_post", MatchType = MatchType.PageWithImage, Score = 0.82 },
    };
}

// ─── Models ────────────────────────────────────────────────────────────────

public class WebMatch
{
    public string Url { get; set; } = string.Empty;
    public MatchType MatchType { get; set; }
    public double Score { get; set; }
    public string? PageTitle { get; set; }

    /// <summary>Infer which platform this URL belongs to</summary>
    public string Platform => Url switch
    {
        var u when u.Contains("youtube.com") => "YouTube",
        var u when u.Contains("twitter.com") || u.Contains("x.com") => "Twitter",
        var u when u.Contains("instagram.com") => "Instagram",
        var u when u.Contains("t.me") || u.Contains("telegram") => "Telegram",
        var u when u.Contains("reddit.com") => "Reddit",
        var u when u.Contains("facebook.com") => "Facebook",
        _ => "Web"
    };
}

public enum MatchType { FullMatch, PartialMatch, PageWithImage }
