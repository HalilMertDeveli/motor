namespace MotorShop.AdminPanel.Services;

/// <summary>
/// What the panel accepts as a product photo. The file content decides, not the name or the declared type:
/// a renamed PDF or script is refused, a JPEG saved as ".png" is stored as what it really is.
/// </summary>
public static class ProductPhotoRules
{
    public const int MaxPhotos = 10;
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Bytes needed to recognize every supported format.
    /// </summary>
    public const int HeaderLength = 12;

    /// <returns>Resource key of the problem, or <c>null</c> when the file is a supported image.</returns>
    public static string Validate(string contentType, long length, ReadOnlySpan<byte> header)
    {
        if (length <= 0)
        {
            return "Photo.Empty";
        }

        if (length > MaxBytes)
        {
            return "Photo.TooLarge";
        }

        if (contentType.IsEmpty() || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || DetectExtension(header) == null)
        {
            return "Photo.Type";
        }

        return null;
    }

    /// <returns>File extension of the detected format (".jpg", ".png", ".webp", ".gif"), or <c>null</c>.</returns>
    public static string DetectExtension(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
        {
            return ".jpg";
        }

        if (h.Length >= 8 && h[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }

        if (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        if (h.Length >= 6 && (h[..6].SequenceEqual("GIF87a"u8) || h[..6].SequenceEqual("GIF89a"u8)))
        {
            return ".gif";
        }

        return null;
    }

    /// <summary>
    /// Reads the first bytes without consuming a non-seekable stream for good: callers open a new stream to save.
    /// </summary>
    public static byte[] ReadHeader(System.IO.Stream stream)
    {
        var buffer = new byte[HeaderLength];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                break;
            }
            read += n;
        }

        return buffer[..read];
    }
}
