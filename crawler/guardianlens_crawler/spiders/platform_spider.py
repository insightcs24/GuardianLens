"""
guardianlens_crawler/spiders/platform_spider.py

GuardianLens Web Crawler — scans multiple platforms for potential IP violations.

How it works:
  1. Receives a list of (asset_id, pHash, search_keywords) from the C# API
  2. Scrapes image URLs from platform search results pages
  3. Downloads each image and computes its pHash
  4. If similarity >= threshold → submits violation to C# API

Platforms covered:
  - YouTube (video thumbnail scraping)
  - Reddit (image posts)
  - Generic web (Google/Bing image search results)
  - [Playwright extension for JS-rendered pages — see PlaywrightMiddleware]

Run:
  python -m guardianlens_crawler.runner --asset-id 1 --phash A3F5C2B19E4D7A0F --keywords "IPL highlights"
"""

import scrapy
import requests
import logging
import json
from urllib.parse import urlencode, quote_plus
from ..phash import compute_phash, similarity

logger = logging.getLogger(__name__)

# Configurable thresholds
MATCH_THRESHOLD = 0.90       # 90%+ similarity = violation
REVIEW_THRESHOLD = 0.82      # 82-90% = flag for human review

# GuardianLens C# API base URL
API_BASE = "http://localhost:5000/api"


class PlatformCrawler(scrapy.Spider):
    """
    Multi-platform crawler that searches for unauthorized use of registered sports media.
    
    Usage from Python:
        from scrapy.crawler import CrawlerProcess
        process = CrawlerProcess(get_project_settings())
        process.crawl(PlatformCrawler, 
                      asset_id=1,
                      phash="A3F5C2B19E4D7A0F",
                      keywords="IPL 2024 final highlights")
        process.start()
    """
    name = "platform_crawler"
    custom_settings = {
        "ROBOTSTXT_OBEY": True,
        "DOWNLOAD_DELAY": 1.5,          # Polite crawling — 1.5s between requests
        "RANDOMIZE_DOWNLOAD_DELAY": True,
        "CONCURRENT_REQUESTS": 4,
        "DOWNLOADER_MIDDLEWARES": {
            "scrapy.downloadermiddlewares.useragent.UserAgentMiddleware": None,
            "guardianlens_crawler.middlewares.RotatingUserAgentMiddleware": 400,
        },
        "LOG_LEVEL": "INFO",
    }

    def __init__(self, asset_id, phash, keywords, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.asset_id = int(asset_id)
        self.target_phash = phash
        self.keywords = keywords
        self.violations_found = 0
        self.images_checked = 0

    def start_requests(self):
        """Generate initial requests for each platform."""
        kw = quote_plus(self.keywords)

        # 1. Reddit image search
        yield scrapy.Request(
            f"https://www.reddit.com/search.json?q={kw}&type=link&t=week",
            callback=self.parse_reddit,
            headers={"User-Agent": "GuardianLensBot/1.0 (IP protection crawler)"},
            meta={"platform": "Reddit"}
        )

        # 2. YouTube thumbnail search via YouTube Data API v3
        # In production: replace YOUR_API_KEY with a real key from Google Cloud Console
        yt_params = urlencode({
            "part": "snippet",
            "q": self.keywords,
            "type": "video",
            "maxResults": 25,
            "key": "YOUR_YOUTUBE_API_KEY"
        })
        yield scrapy.Request(
            f"https://www.googleapis.com/youtube/v3/search?{yt_params}",
            callback=self.parse_youtube,
            meta={"platform": "YouTube"},
            errback=self.handle_youtube_error
        )

        # 3. Bing Image Search (fallback when no API key)
        yield scrapy.Request(
            f"https://www.bing.com/images/search?q={kw}+site:youtube.com+OR+site:twitter.com",
            callback=self.parse_bing_images,
            meta={"platform": "Bing"}
        )

    # ─── Platform parsers ──────────────────────────────────────────────────────

    def parse_reddit(self, response):
        """Parse Reddit JSON API response for image posts."""
        try:
            data = json.loads(response.text)
            posts = data.get("data", {}).get("children", [])
            logger.info(f"Reddit: found {len(posts)} posts for '{self.keywords}'")

            for post in posts:
                post_data = post.get("data", {})
                # Get preview image if available
                preview = post_data.get("preview", {})
                images = preview.get("images", [])
                if images:
                    img_url = images[0].get("source", {}).get("url", "").replace("&amp;", "&")
                    if img_url:
                        yield scrapy.Request(
                            img_url,
                            callback=self.check_image,
                            meta={
                                "platform": "Reddit",
                                "source_url": f"https://reddit.com{post_data.get('permalink', '')}",
                                "title": post_data.get("title", ""),
                            }
                        )
        except Exception as e:
            logger.error(f"Reddit parse error: {e}")

    def parse_youtube(self, response):
        """Parse YouTube Data API v3 search results."""
        try:
            data = json.loads(response.text)
            items = data.get("items", [])
            logger.info(f"YouTube: found {len(items)} videos for '{self.keywords}'")

            for item in items:
                video_id = item.get("id", {}).get("videoId")
                snippet = item.get("snippet", {})
                # Get the high-res thumbnail
                thumbnail_url = snippet.get("thumbnails", {}).get("high", {}).get("url")

                if video_id and thumbnail_url:
                    yield scrapy.Request(
                        thumbnail_url,
                        callback=self.check_image,
                        meta={
                            "platform": "YouTube",
                            "source_url": f"https://youtube.com/watch?v={video_id}",
                            "title": snippet.get("title", ""),
                        }
                    )
        except Exception as e:
            logger.error(f"YouTube parse error: {e}")

    def handle_youtube_error(self, failure):
        logger.warning(f"YouTube API unavailable (no API key?): {failure.value}")
        # Fall back to thumbnail heuristic with known video IDs
        # In hackathon demo: just show that the code path exists

    def parse_bing_images(self, response):
        """Parse Bing Image Search HTML for image URLs."""
        # Bing image URLs are in data-src attributes of img tags
        image_links = response.css("img.mimg::attr(src)").getall()
        source_urls = response.css("a.iusc::attr(m)").getall()

        logger.info(f"Bing: found {len(image_links)} images for '{self.keywords}'")

        for i, img_url in enumerate(image_links[:20]):  # Check top 20
            if img_url.startswith("http"):
                source_url = source_urls[i] if i < len(source_urls) else img_url
                yield scrapy.Request(
                    img_url,
                    callback=self.check_image,
                    meta={
                        "platform": "Web",
                        "source_url": source_url,
                        "title": f"Image result {i+1}",
                    }
                )

    # ─── Core image check ──────────────────────────────────────────────────────

    def check_image(self, response):
        """
        Download image, compute pHash, compare against registered asset.
        If match found → report violation to C# API.
        """
        self.images_checked += 1
        platform = response.meta.get("platform", "Unknown")
        source_url = response.meta.get("source_url", response.url)

        if "image" not in response.headers.get("Content-Type", b"").decode():
            return

        try:
            found_hash = compute_phash(response.body)
            sim = similarity(self.target_phash, found_hash)

            logger.debug(f"[{platform}] {source_url[:60]}... → {sim*100:.1f}% match")

            if sim >= MATCH_THRESHOLD:
                logger.warning(f"🚨 VIOLATION DETECTED [{platform}] {sim*100:.1f}% | {source_url}")
                self._report_violation(
                    platform=platform,
                    infringing_url=source_url,
                    confidence=sim,
                    status="Detected"
                )
                self.violations_found += 1

            elif sim >= REVIEW_THRESHOLD:
                logger.info(f"⚠ REVIEW NEEDED [{platform}] {sim*100:.1f}% | {source_url}")
                self._report_violation(
                    platform=platform,
                    infringing_url=source_url,
                    confidence=sim,
                    status="UnderReview"
                )

        except Exception as e:
            logger.error(f"Image check failed for {source_url}: {e}")

    def _report_violation(self, platform, infringing_url, confidence, status):
        """POST violation to the GuardianLens C# API."""
        try:
            resp = requests.post(
                f"{API_BASE}/violations/report",
                json={
                    "assetId": self.asset_id,
                    "platform": platform,
                    "infringingUrl": infringing_url,
                    "matchConfidence": round(confidence, 4),
                    "status": status,
                    "detectedBycrawler": True
                },
                timeout=5
            )
            if resp.status_code in (200, 201):
                logger.info(f"✅ Violation reported to API: {infringing_url[:50]}...")
            else:
                logger.warning(f"API reported {resp.status_code}: {resp.text[:100]}")
        except requests.exceptions.ConnectionError:
            # API not running (offline mode) — log locally instead
            logger.info(f"[OFFLINE] Would report: {platform} | {confidence*100:.1f}% | {infringing_url}")

    def closed(self, reason):
        logger.info(
            f"\n{'='*60}\n"
            f"Scan complete for asset {self.asset_id}\n"
            f"  Images checked:    {self.images_checked}\n"
            f"  Violations found:  {self.violations_found}\n"
            f"  Keywords:          {self.keywords}\n"
            f"{'='*60}"
        )
