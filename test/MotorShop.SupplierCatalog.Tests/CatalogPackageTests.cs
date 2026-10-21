using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MotorShop.SupplierCatalog.Services;
using NUnit.Framework;

namespace MotorShop.SupplierCatalog.Tests;

[TestFixture]
public class CatalogPackageTests
{
    const string ProductsJson = """
        [
          { "sku": "MS-1", "name": "Kask", "category": "Ekipman > Kask", "brand": "Test", "price": "1499.90", "images": ["ms-1.jpg"] },
          { "sku": "MS-2", "name": "Eldiven", "category": "Ekipman > Eldiven", "images": ["missing.jpg"] }
        ]
        """;

    private static MemoryStream CreateZip(Action<ZipArchive> fill)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            fill(archive);
        }

        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive archive, string name, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = archive.CreateEntry(name, level);
        using var target = entry.Open();
        target.Write(content);
    }

    private static void Add(ZipArchive archive, string name, string content)
        => Add(archive, name, Encoding.UTF8.GetBytes(content));

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    [Test]
    public async Task Opens_products_and_images_and_ignores_unsafe_or_non_image_entries()
    {
        using var zip = CreateZip(a =>
        {
            Add(a, "motosev/products.json", ProductsJson);
            Add(a, "motosev/images/ms-1.jpg", RandomBytes(2048));
            Add(a, "motosev/images/readme.txt", "not an image");
            Add(a, "motosev/images/../../evil.jpg", RandomBytes(16));
        });

        using var package = CatalogPackage.Open(zip);

        Assert.That(package.Products, Has.Count.EqualTo(2));
        Assert.That(package.Products[0].Price, Is.EqualTo(1499.90m));
        Assert.That(package.Warnings, Has.Some.Contains("not an image"));
        Assert.That(package.Warnings, Has.Some.Contains("unsafe path"));

        await using var image = await package.OpenImageAsync("ms-1.jpg");
        Assert.That(image, Is.Not.Null);
        Assert.That(image.Length, Is.EqualTo(2048));
        Assert.That(image.CanSeek, Is.True);

        Assert.That(await package.OpenImageAsync("missing.jpg"), Is.Null);
        Assert.That(await package.OpenImageAsync("evil.jpg"), Is.Null, "an entry with '..' in its path must not be served");
        Assert.That(await package.OpenImageAsync("readme.txt"), Is.Null);
    }

    [Test]
    public void Missing_products_json_is_rejected()
    {
        using var zip = CreateZip(a => Add(a, "images/a.jpg", RandomBytes(10)));

        Assert.Throws<InvalidDataException>(() => CatalogPackage.Open(zip));
    }

    [Test]
    public void Invalid_json_is_reported_as_invalid_data()
    {
        using var zip = CreateZip(a => Add(a, "products.json", "{ not json"));

        var ex = Assert.Throws<InvalidDataException>(() => CatalogPackage.Open(zip));
        Assert.That(ex.Message, Does.Contain("products.json"));
    }

    [Test]
    public void Too_many_entries_are_rejected()
    {
        using var zip = CreateZip(a =>
        {
            Add(a, "products.json", "[]");
            for (var i = 0; i < 5; i++)
            {
                Add(a, $"images/{i}.jpg", RandomBytes(8));
            }
        });

        Assert.Throws<InvalidDataException>(() => CatalogPackage.Open(zip, new CatalogPackageLimits { MaxEntries = 3 }));
    }

    [Test]
    public void Oversized_products_json_is_rejected_by_actual_size()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 2000).Select(i => $$"""{"sku":"S{{i}}","name":"N{{i}}"}""")) + "]";
        using var zip = CreateZip(a => Add(a, "products.json", json));

        Assert.Throws<InvalidDataException>(() => CatalogPackage.Open(zip, new CatalogPackageLimits { MaxProductsJsonBytes = 1024, MaxCompressionRatio = 10_000 }));
    }

    [Test]
    public void Too_many_products_are_rejected()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 20).Select(i => $$"""{"sku":"S{{i}}","name":"N{{i}}"}""")) + "]";
        using var zip = CreateZip(a => Add(a, "products.json", json));

        Assert.Throws<InvalidDataException>(() => CatalogPackage.Open(zip, new CatalogPackageLimits { MaxProducts = 10 }));
    }

    [Test]
    public void Highly_compressed_entry_is_rejected_as_zip_bomb()
    {
        // 4 MB of zeros compresses to a few KB.
        using var zip = CreateZip(a =>
        {
            Add(a, "products.json", "[]");
            Add(a, "images/bomb.jpg", new byte[4 * 1024 * 1024]);
        });

        var ex = Assert.Throws<InvalidDataException>(() => CatalogPackage.Open(zip));
        Assert.That(ex.Message, Does.Contain("bomb.jpg"));
    }

    [Test]
    public void Unpacked_size_above_total_limit_is_rejected()
    {
        using var zip = CreateZip(a =>
        {
            Add(a, "products.json", "[]");
            Add(a, "images/a.jpg", RandomBytes(4096), CompressionLevel.NoCompression);
            Add(a, "images/b.jpg", RandomBytes(4096), CompressionLevel.NoCompression);
        });

        Assert.Throws<InvalidDataException>(() => CatalogPackage.Open(zip, new CatalogPackageLimits { MaxTotalUncompressedBytes = 6000 }));
    }

    [Test]
    public void Image_above_limit_is_not_buffered()
    {
        using var zip = CreateZip(a =>
        {
            Add(a, "products.json", ProductsJson);
            Add(a, "images/ms-1.jpg", RandomBytes(64 * 1024), CompressionLevel.NoCompression);
        });

        using var package = CatalogPackage.Open(zip, new CatalogPackageLimits { MaxImageBytes = 16 * 1024 });

        Assert.ThrowsAsync<InvalidDataException>(() => package.OpenImageAsync("ms-1.jpg"));
    }

    [TestCase("images/a.jpg", true)]
    [TestCase("motosev/images/a.jpg", true)]
    [TestCase("images/../a.jpg", false)]
    [TestCase("../images/a.jpg", false)]
    [TestCase("/images/a.jpg", false)]
    [TestCase("C:/images/a.jpg", false)]
    public void Entry_path_safety(string path, bool expected)
        => Assert.That(CatalogPackage.IsSafeEntryPath(path), Is.EqualTo(expected));
}
