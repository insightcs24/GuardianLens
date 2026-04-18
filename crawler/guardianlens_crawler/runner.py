"""
guardianlens_crawler/runner.py

CLI entry point — run a GuardianLens scan from the terminal.

Usage:
    python -m guardianlens_crawler.runner --asset-id 1 --phash A3F5C2B19E4D7A0F --keywords "IPL 2024 highlights"
    
    Or with a real API call to get the asset:
    python -m guardianlens_crawler.runner --asset-id 1 --api http://localhost:5000
"""

import argparse
import requests
import logging
from scrapy.crawler import CrawlerProcess
from scrapy.utils.project import get_project_settings
from guardianlens_crawler.spiders.platform_spider import PlatformCrawler

logging.basicConfig(level=logging.INFO,
                    format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger(__name__)


def fetch_asset_from_api(asset_id: int, api_base: str) -> dict:
    """Fetch asset pHash and metadata from the C# API."""
    try:
        resp = requests.get(f"{api_base}/api/assets/{asset_id}", timeout=5)
        resp.raise_for_status()
        return resp.json()
    except requests.exceptions.ConnectionError:
        logger.warning("C# API not reachable — using demo mode")
        return None


def run_scan(asset_id: int, phash: str, keywords: str):
    """Start a Scrapy crawl for the given asset."""
    logger.info(f"Starting GuardianLens scan")
    logger.info(f"  Asset ID: {asset_id}")
    logger.info(f"  pHash:    {phash}")
    logger.info(f"  Keywords: {keywords}")
    logger.info("")

    settings = {
        "LOG_LEVEL": "INFO",
        "ROBOTSTXT_OBEY": True,
        "DOWNLOAD_DELAY": 1.0,
        "CONCURRENT_REQUESTS": 4,
        "DOWNLOADER_MIDDLEWARES": {
            "scrapy.downloadermiddlewares.useragent.UserAgentMiddleware": None,
            "guardianlens_crawler.middlewares.RotatingUserAgentMiddleware": 400,
        },
    }

    process = CrawlerProcess(settings=settings)
    process.crawl(PlatformCrawler,
                  asset_id=asset_id,
                  phash=phash,
                  keywords=keywords)
    process.start()  # blocks until crawl finishes


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="GuardianLens Web Crawler")
    parser.add_argument("--asset-id", type=int, required=True, help="Asset ID to scan for")
    parser.add_argument("--phash", type=str, help="pHash of the asset (optional if API available)")
    parser.add_argument("--keywords", type=str, required=True, help="Search keywords (e.g. 'IPL 2024 final highlights')")
    parser.add_argument("--api", type=str, default="http://localhost:5000", help="C# API base URL")

    args = parser.parse_args()
    phash = args.phash

    # If no pHash provided, fetch it from the C# API
    if not phash:
        asset = fetch_asset_from_api(args.asset_id, args.api)
        if asset:
            phash = asset.get("pHash")
            logger.info(f"Fetched pHash from API: {phash}")
        else:
            logger.error("No pHash provided and API is not reachable. Provide --phash manually.")
            exit(1)

    run_scan(args.asset_id, phash, args.keywords)
