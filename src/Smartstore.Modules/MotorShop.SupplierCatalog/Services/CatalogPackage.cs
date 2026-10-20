using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotorShop.SupplierCatalog.Services;

/// <summary>
/// One product of a supplier catalog package (products.json).
/// </summary>
public class CatalogPackageProduct
{
    public string Sku { get; set; }
    public string Name { get; set; }

    /// <summary>
    /// Category path, levels separated by " > ".
    /// </summary>
    public string Category { get; set; }
    public string Brand { get; set; }
    public string CatalogCode { get; set; }
    public string Variant { get; set; }

    /// <summary>
    /// Retail price. <c>null</c> when the catalog prints no price.
    /// </summary>
    public decimal? Price { get; set; }
    public bool Campaign { get; set; }
    public List<string> Sizes { get; set; } = [];
    public List<string> Colors { get; set; } = [];
    public string Compatibility { get; set; }
    public string ShortDescription { get; set; }
    public string DescriptionHtml { get; set; }

    /// <summary>
    /// File names below the package's "images/" folder, main picture first.
    /// </summary>
    public List<string> Images { get; set; } = [];
    public int CatalogPage { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Hard limits for a catalog package. Uploads come from admins, but a damaged or crafted zip must not
/// exhaust memory or disk (zip bomb), so declared sizes are never trusted and every read is bounded.
/// </summary>
public sealed class CatalogPackageLimits
{
    public static readonly CatalogPackageLimits Default = new();

    public int MaxEntries { get; init; } = 20_000;
    public int MaxProducts { get; init; } = 50_000;
    public long MaxProductsJsonBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxImageBytes { get; init; } = 20L * 1024 * 1024;
    public long MaxTotalUncompressedBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Maximum ratio of uncompressed to compressed size of a single entry. JSON compresses well (about 10:1),
    /// images hardly at all; anything far above that is treated as a zip bomb.
    /// </summary>
    public double MaxCompressionRatio { get; init; } = 100;
}

/// <summary>
/// A zip file containing "products.json" and an "images/" folder.
/// </summary>
public sealed class CatalogPackage : IDisposable
{
    public const string ProductsEntryName = "products.json";
    public const string ImagesFolder = "images/";

    public static readonly IReadOnlySet<string> ImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly ZipArchive _archive;
    private readonly CatalogPackageLimits _limits;
    private readonly Dictionary<string, ZipArchiveEntry> _images;

    private CatalogPackage(ZipArchive archive, CatalogPackageLimits limits, List<CatalogPackageProduct> products, List<string> warnings)
    {
        _archive = archive;
        _limits = limits;
        Products = products;
        Warnings = warnings;
        _images = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries.Where(x => x.Length > 0))
        {
            var path = NormalizePath(entry.FullName);
            if (!path.Contains(ImagesFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsSafeEntryPath(path))
            {
                warnings.Add($"Ignored package entry with an unsafe path: '{entry.FullName}'.");
                continue;
            }

            if (!ImageExtensions.Contains(Path.GetExtension(entry.Name)))
            {
                warnings.Add($"Ignored package entry that is not an image: '{entry.FullName}'.");
                continue;
            }

            _images.TryAdd(entry.Name, entry);
        }
    }

    public IReadOnlyList<CatalogPackageProduct> Products { get; }

    /// <summary>
    /// Problems found while opening the package that do not stop the import.
    /// </summary>
    public List<string> Warnings { get; }

    public static CatalogPackage Open(Stream zipStream, CatalogPackageLimits limits = null)
    {
        Guard.NotNull(zipStream);
        limits ??= CatalogPackageLimits.Default;

        var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        try
        {
            Validate(archive, limits);

            // Accept products.json at the root or inside one top-level folder.
            var entry = archive.Entries
                .Where(x => x.Name.Equals(ProductsEntryName, StringComparison.OrdinalIgnoreCase) && IsSafeEntryPath(NormalizePath(x.FullName)))
                .OrderBy(x => x.FullName.Length)
                .FirstOrDefault()
                ?? throw new InvalidDataException($"The package does not contain '{ProductsEntryName}'.");

            List<CatalogPackageProduct> products;
            using (var source = entry.Open())
            using (var json = ReadBounded(source, limits.MaxProductsJsonBytes, ProductsEntryName))
            {
                products = JsonSerializer.Deserialize<List<CatalogPackageProduct>>(json, _jsonOptions) ?? [];
            }

            if (products.Count > limits.MaxProducts)
            {
                throw new InvalidDataException($"The package contains {products.Count} products, more than the allowed {limits.MaxProducts}.");
            }

            return new CatalogPackage(archive, limits, products, []);
        }
        catch (JsonException ex)
        {
            archive.Dispose();
            throw new InvalidDataException($"'{ProductsEntryName}' is not valid: {ex.Message}", ex);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an image of the package, or returns <c>null</c> when it is missing.
    /// The caller owns the returned stream.
    /// </summary>
    /// <exception cref="InvalidDataException">The image is larger than <see cref="CatalogPackageLimits.MaxImageBytes"/>.</exception>
    public async Task<Stream> OpenImageAsync(string fileName, CancellationToken cancelToken = default)
    {
        if (fileName.IsEmpty() || !_images.TryGetValue(Path.GetFileName(fileName), out var entry))
        {
            return null;
        }

        // Zip entry streams are not seekable, but media de-duplication needs to rewind.
        await using var source = entry.Open();
        return await ReadBoundedAsync(source, _limits.MaxImageBytes, entry.FullName, cancelToken);
    }

    public void Dispose()
        => _archive.Dispose();

    internal static void Validate(ZipArchive archive, CatalogPackageLimits limits)
    {
        if (archive.Entries.Count > limits.MaxEntries)
        {
            throw new InvalidDataException($"The package contains {archive.Entries.Count} entries, more than the allowed {limits.MaxEntries}.");
        }

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            total += entry.Length;
            if (total > limits.MaxTotalUncompressedBytes)
            {
                throw new InvalidDataException("The unpacked package is larger than allowed.");
            }

            // Tiny entries (folders, short files) can have any ratio; check only what matters.
            if (entry.Length > 1024 * 1024 && (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > limits.MaxCompressionRatio))
            {
                throw new InvalidDataException($"The package entry '{entry.FullName}' is compressed suspiciously well and was rejected.");
            }
        }
    }

    /// <summary>
    /// Rejects absolute paths and parent-directory segments. Nothing is written to disk from entry names,
    /// but such entries only occur in crafted archives.
    /// </summary>
    internal static bool IsSafeEntryPath(string normalizedPath)
    {
        if (normalizedPath.StartsWith('/') || normalizedPath.Contains(':'))
        {
            return false;
        }

        return !normalizedPath.Split('/').Any(x => x == "..");
    }

    internal static MemoryStream ReadBounded(Stream source, long maxBytes, string name)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                buffer.Dispose();
                throw new InvalidDataException($"'{name}' is larger than the allowed {maxBytes / (1024 * 1024)} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return buffer;
    }

    internal static async Task<MemoryStream> ReadBoundedAsync(Stream source, long maxBytes, string name, CancellationToken cancelToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancelToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                await buffer.DisposeAsync();
                throw new InvalidDataException($"'{name}' is larger than the allowed {maxBytes / (1024 * 1024)} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return buffer;
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');
}
