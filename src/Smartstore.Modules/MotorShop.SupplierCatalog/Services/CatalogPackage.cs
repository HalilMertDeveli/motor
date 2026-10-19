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
/// A zip file containing "products.json" and an "images/" folder.
/// </summary>
public sealed class CatalogPackage : IDisposable
{
    public const string ProductsEntryName = "products.json";
    public const string ImagesFolder = "images/";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _images;

    private CatalogPackage(ZipArchive archive, List<CatalogPackageProduct> products)
    {
        _archive = archive;
        Products = products;
        _images = archive.Entries
            .Where(x => x.Length > 0 && NormalizePath(x.FullName).Contains(ImagesFolder, StringComparison.OrdinalIgnoreCase))
            .ToDictionarySafe(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CatalogPackageProduct> Products { get; }

    public static CatalogPackage Open(Stream zipStream)
    {
        Guard.NotNull(zipStream);

        var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        try
        {
            // Accept products.json at the root or inside one top-level folder.
            var entry = archive.Entries
                .Where(x => x.Name.Equals(ProductsEntryName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FullName.Length)
                .FirstOrDefault()
                ?? throw new InvalidDataException($"The package does not contain '{ProductsEntryName}'.");

            using var stream = entry.Open();
            var products = JsonSerializer.Deserialize<List<CatalogPackageProduct>>(stream, _jsonOptions) ?? [];

            return new CatalogPackage(archive, products);
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
    public async Task<Stream> OpenImageAsync(string fileName)
    {
        if (fileName.IsEmpty() || !_images.TryGetValue(Path.GetFileName(fileName), out var entry))
        {
            return null;
        }

        // Zip entry streams are not seekable, but media de-duplication needs to rewind.
        var buffer = new MemoryStream((int)entry.Length);
        await using (var source = entry.Open())
        {
            await source.CopyToAsync(buffer);
        }

        buffer.Position = 0;
        return buffer;
    }

    public void Dispose()
        => _archive.Dispose();

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');
}
