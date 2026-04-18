"""
guardianlens_crawler/phash.py

Python pHash engine — mirrors the C# FingerprintService.cs DCT implementation exactly.
Used by the crawler to fingerprint images found on the web before submitting
them to the C# API's /verify endpoint.
"""

import math
import struct
from io import BytesIO
from PIL import Image


def compute_phash(image_source) -> str:
    """
    Compute a 64-bit perceptual hash (pHash) using DCT.
    
    Args:
        image_source: bytes, BytesIO, file path, or URL string
    
    Returns:
        16-char hex string e.g. "A3F5C2B19E4D7A0F"
    
    The hash is robust to:
      - JPEG re-compression
      - Minor resizing / cropping
      - Brightness / contrast shifts
      - Watermark overlays
    """
    # Load image
    if isinstance(image_source, bytes):
        img = Image.open(BytesIO(image_source))
    elif isinstance(image_source, BytesIO):
        img = Image.open(image_source)
    else:
        img = Image.open(image_source)

    # Step 1: Resize to 32×32 grayscale
    img = img.convert("L").resize((32, 32), Image.Resampling.LANCZOS)
    pixels = list(img.getdata())

    # Build 32×32 2D array
    grid = [[pixels[y * 32 + x] for x in range(32)] for y in range(32)]

    # Step 2: 2D DCT via separable 1D DCTs
    dct_2d = _dct2d(grid, 32)

    # Step 3: Extract top-left 8×8 block (low frequencies)
    low_freq = [dct_2d[y][x] for y in range(8) for x in range(8)]

    # Step 4: Mean of AC components (skip DC at index 0)
    mean = sum(low_freq[1:]) / len(low_freq[1:])

    # Step 5: Build 64-bit hash
    hash_val = 0
    for i, val in enumerate(low_freq):
        if val >= mean:
            hash_val |= (1 << i)

    return format(hash_val, '016X')


def hamming_distance(hash1: str, hash2: str) -> int:
    """Count differing bits between two hex hash strings."""
    h1 = int(hash1, 16)
    h2 = int(hash2, 16)
    xor = h1 ^ h2
    count = 0
    while xor:
        xor &= xor - 1
        count += 1
    return count


def similarity(hash1: str, hash2: str) -> float:
    """Return similarity 0.0–1.0 (1.0 = identical)."""
    return 1.0 - (hamming_distance(hash1, hash2) / 64.0)


def is_match(hash1: str, hash2: str, threshold: float = 0.90) -> bool:
    return similarity(hash1, hash2) >= threshold


# ─── Internal DCT implementation ─────────────────────────────────────────────

def _dct1d(signal: list) -> list:
    """1D Type-II DCT with orthonormal normalization."""
    N = len(signal)
    factor = math.pi / (2.0 * N)
    result = []
    for k in range(N):
        s = sum(signal[n] * math.cos((2 * n + 1) * k * factor) for n in range(N))
        norm = math.sqrt(1.0 / N) if k == 0 else math.sqrt(2.0 / N)
        result.append(s * norm)
    return result


def _dct2d(grid: list, N: int) -> list:
    """2D DCT via row-then-column separable 1D DCTs."""
    # Apply DCT to each row
    temp = [_dct1d(grid[y]) for y in range(N)]
    # Apply DCT to each column
    result = [[0.0] * N for _ in range(N)]
    for x in range(N):
        col = [temp[y][x] for y in range(N)]
        dct_col = _dct1d(col)
        for y in range(N):
            result[y][x] = dct_col[y]
    return result
