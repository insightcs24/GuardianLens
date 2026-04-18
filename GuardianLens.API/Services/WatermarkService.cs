using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

namespace GuardianLens.API.Services;

/// <summary>
/// Invisible Watermarking using LSB (Least Significant Bit) steganography.
/// 
/// How it works:
///   Embed: Convert ownership token to bits, hide each bit in the 
///          least significant bit of a pixel's blue channel.
///          Invisible to the human eye — changes value by at most 1.
///   
///   Extract: Read back the LSBs from the same pixel positions.
///            If the extracted token matches our registry → ownership proven.
///
/// For production: Use DCT-domain watermarking (more robust to compression).
/// </summary>
public interface IWatermarkService
{
    /// <summary>Embed an invisible token into an image. Returns watermarked image bytes.</summary>
    byte[] EmbedWatermark(byte[] imageBytes, string token);

    /// <summary>Extract the embedded token from an image. Returns null if not found.</summary>
    string? ExtractWatermark(byte[] imageBytes);

    /// <summary>Generate a unique ownership token for an asset</summary>
    string GenerateToken(string organization, string assetId);
}

public class WatermarkService : IWatermarkService
{
    private const string Header = "GL:";   // GuardianLens header prefix
    private const int HeaderLengthBits = 32; // 4 bytes for message length

    public byte[] EmbedWatermark(byte[] imageBytes, string token)
    {
        using var image = Image.Load<Rgba32>(imageBytes);

        string message = Header + token;
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);

        // Prefix with 4-byte length header so we know how much to extract
        byte[] lengthBytes = BitConverter.GetBytes(messageBytes.Length);
        byte[] payload = lengthBytes.Concat(messageBytes).ToArray();

        int totalBits = payload.Length * 8;
        int availablePixels = image.Width * image.Height;

        if (totalBits > availablePixels)
            throw new InvalidOperationException(
                $"Image too small to embed {totalBits} bits. Need at least {totalBits}px.");

        int bitIndex = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && bitIndex < totalBits; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length && bitIndex < totalBits; x++)
                {
                    int byteIdx = bitIndex / 8;
                    int bitPos = bitIndex % 8;
                    int bit = (payload[byteIdx] >> bitPos) & 1;

                    // Embed in LSB of the Blue channel (least visible channel)
                    ref Rgba32 pixel = ref row[x];
                    pixel.B = (byte)((pixel.B & 0xFE) | bit);

                    bitIndex++;
                }
            }
        });

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    public string? ExtractWatermark(byte[] imageBytes)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);

            // Step 1: Extract 32 bits to get message length
            byte[] lengthBytes = ExtractBits(image, 0, 32);
            int messageLength = BitConverter.ToInt32(lengthBytes, 0);

            if (messageLength <= 0 || messageLength > 10000) return null;

            // Step 2: Extract message bits
            byte[] messageBytes = ExtractBits(image, 32, messageLength * 8);
            string message = Encoding.UTF8.GetString(messageBytes);

            return message.StartsWith(Header) ? message[Header.Length..] : null;
        }
        catch
        {
            return null;
        }
    }

    public string GenerateToken(string organization, string assetId)
    {
        // Generate a cryptographically unique token
        string raw = $"{organization}:{assetId}:{DateTime.UtcNow.Ticks}";
        var hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(raw));
        return Convert.ToBase64String(hash)[..24];  // 24-char token
    }

    private static byte[] ExtractBits(Image<Rgba32> image, int startBit, int count)
    {
        byte[] result = new byte[(count + 7) / 8];
        int bitIndex = startBit;
        int extractedBits = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && extractedBits < count; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length && extractedBits < count; x++)
                {
                    if (bitIndex > 0) { bitIndex--; x++; continue; }

                    int bit = row[x].B & 1;
                    int byteIdx = extractedBits / 8;
                    int bitPos = extractedBits % 8;
                    result[byteIdx] |= (byte)(bit << bitPos);
                    extractedBits++;
                }
            }
        });

        return result;
    }
}
