using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GuardianLens.API.Services;

/// <summary>
/// Perceptual Hash (pHash) implementation using Discrete Cosine Transform (DCT).
/// Produces a 64-bit hash that is robust to:
///   - JPEG re-compression and quality changes
///   - Minor color/brightness adjustments
///   - Resizing and slight cropping
///   - Watermark overlays
/// </summary>
public interface IFingerprintService
{
    /// <summary>Compute pHash from image bytes</summary>
    string ComputePHash(byte[] imageBytes);

    /// <summary>Compute pHash from base64-encoded image</summary>
    string ComputePHashFromBase64(string base64);

    /// <summary>Hamming distance between two pHash strings (0 = identical)</summary>
    int HammingDistance(string hash1, string hash2);

    /// <summary>Similarity score 0.0 – 1.0 (1.0 = identical)</summary>
    double Similarity(string hash1, string hash2);

    /// <summary>Returns true if images are likely the same content (threshold: 90%)</summary>
    bool IsMatch(string hash1, string hash2, double threshold = 0.90);
}

public class FingerprintService : IFingerprintService
{
    // pHash works by:
    // 1. Resize image to 32x32 grayscale
    // 2. Compute 2D DCT
    // 3. Take the top-left 8x8 DCT coefficients (low frequencies)
    // 4. Compare each coefficient to the mean → 64-bit hash

    private const int ResizeSize = 32;
    private const int DctSize = 8;

    public string ComputePHash(byte[] imageBytes)
    {
        using var image = Image.Load<L8>(imageBytes);  // L8 = grayscale

        // Step 1: Resize to 32x32
        image.Mutate(x => x.Resize(ResizeSize, ResizeSize));

        // Step 2: Extract pixel values as double array
        var pixels = new double[ResizeSize, ResizeSize];
        for (int y = 0; y < ResizeSize; y++)
            for (int x = 0; x < ResizeSize; x++)
                pixels[y, x] = image[x, y].PackedValue;

        // Step 3: Apply 2D DCT
        var dct = ComputeDCT2D(pixels);

        // Step 4: Extract top-left 8x8 block (low frequency components)
        var lowFreq = new double[DctSize * DctSize];
        int idx = 0;
        for (int y = 0; y < DctSize; y++)
            for (int x = 0; x < DctSize; x++)
                lowFreq[idx++] = dct[y, x];

        // Step 5: Compute mean (skip DC component at [0,0])
        double mean = lowFreq.Skip(1).Average();

        // Step 6: Build 64-bit hash: 1 if coefficient >= mean, else 0
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
        {
            if (lowFreq[i] >= mean)
                hash |= (1UL << i);
        }

        return hash.ToString("X16");  // 16-char hex string
    }

    public string ComputePHashFromBase64(string base64)
    {
        // Strip data URI prefix if present
        if (base64.Contains(','))
            base64 = base64.Split(',')[1];

        var bytes = Convert.FromBase64String(base64);
        return ComputePHash(bytes);
    }

    public int HammingDistance(string hash1, string hash2)
    {
        if (hash1.Length != hash2.Length)
            throw new ArgumentException("Hash lengths must match");

        ulong h1 = Convert.ToUInt64(hash1, 16);
        ulong h2 = Convert.ToUInt64(hash2, 16);
        ulong xor = h1 ^ h2;

        // Count set bits (Brian Kernighan's algorithm)
        int count = 0;
        while (xor != 0) { xor &= xor - 1; count++; }
        return count;
    }

    public double Similarity(string hash1, string hash2)
    {
        int distance = HammingDistance(hash1, hash2);
        return 1.0 - (distance / 64.0);
    }

    public bool IsMatch(string hash1, string hash2, double threshold = 0.90)
        => Similarity(hash1, hash2) >= threshold;

    // ─── DCT Implementation ───────────────────────────────────────────────────

    private static double[,] ComputeDCT2D(double[,] input)
    {
        int N = input.GetLength(0);
        var temp = new double[N, N];
        var output = new double[N, N];

        // Apply 1D DCT to each row
        for (int y = 0; y < N; y++)
        {
            var row = new double[N];
            for (int x = 0; x < N; x++) row[x] = input[y, x];
            var dctRow = DCT1D(row);
            for (int x = 0; x < N; x++) temp[y, x] = dctRow[x];
        }

        // Apply 1D DCT to each column of the result
        for (int x = 0; x < N; x++)
        {
            var col = new double[N];
            for (int y = 0; y < N; y++) col[y] = temp[y, x];
            var dctCol = DCT1D(col);
            for (int y = 0; y < N; y++) output[y, x] = dctCol[y];
        }

        return output;
    }

    private static double[] DCT1D(double[] input)
    {
        int N = input.Length;
        var output = new double[N];
        double factor = Math.PI / (2.0 * N);

        for (int k = 0; k < N; k++)
        {
            double sum = 0;
            for (int n = 0; n < N; n++)
                sum += input[n] * Math.Cos((2 * n + 1) * k * factor);

            output[k] = sum * (k == 0
                ? Math.Sqrt(1.0 / N)          // DC normalization
                : Math.Sqrt(2.0 / N));         // AC normalization
        }

        return output;
    }
}
