using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;

namespace MotorShop.AdminPanel.Tests;

[TestFixture]
public class PhotoRuleTests
{
    [TestCase(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 }, ".jpg")]
    [TestCase(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ".png")]
    [TestCase(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, ".webp")]
    [TestCase(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, ".gif")]
    [TestCase(new byte[] { 0x25, 0x50, 0x44, 0x46 }, null)]
    [TestCase(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)]
    [TestCase(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, null)]
    [TestCase(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 }, null)]
    [TestCase(new byte[0], null)]
    public void Format_is_detected_from_the_content(byte[] header, string expected)
        => Assert.That(ProductPhotoRules.DetectExtension(header), Is.EqualTo(expected));

    [Test]
    public void Declared_type_size_and_content_are_checked()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

        Assert.That(ProductPhotoRules.Validate("image/jpeg", 2048, jpeg), Is.Null);
        Assert.That(ProductPhotoRules.Validate("image/jpeg", 0, jpeg), Is.EqualTo("Photo.Empty"));
        Assert.That(ProductPhotoRules.Validate("image/jpeg", ProductPhotoRules.MaxBytes + 1, jpeg), Is.EqualTo("Photo.TooLarge"));
        Assert.That(ProductPhotoRules.Validate("application/pdf", 2048, jpeg), Is.EqualTo("Photo.Type"));
        Assert.That(ProductPhotoRules.Validate(null, 2048, jpeg), Is.EqualTo("Photo.Type"));
        Assert.That(ProductPhotoRules.Validate("image/jpeg", 2048, "%PDF"u8), Is.EqualTo("Photo.Type"));
    }
}

/// <summary>
/// Photos through the real service: stored as Smartstore product media in order, the first one is the main picture.
/// </summary>
[TestFixture]
public class PhotoTests : PanelDbTestBase
{
    [Test]
    public async Task Valid_upload_creates_media_in_order_and_the_first_is_main()
    {
        var category = await AddCategoryAsync("Kask");

        var result = await CreateService().CreateAsync(Quick("Kask", category.Id),
            [Photo("on.jpg", Jpeg), Photo("arka.png", Png, "image/png"), Photo("yan.jpeg", Jpeg)], 1);

        Assert.That(result.Success, Is.True, string.Join(", ", result.Errors.Values));
        using var read = NewContext();
        var product = await read.Products.Include(x => x.ProductMediaFiles).ThenInclude(x => x.MediaFile).SingleAsync();
        var ordered = product.ProductMediaFiles.OrderBy(x => x.DisplayOrder).ToList();

        Assert.That(ordered.Select(x => x.DisplayOrder), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(product.MainPictureId, Is.EqualTo(ordered[0].MediaFileId));
        // Stored with the extension of the real format and named after the product slug.
        Assert.That(ordered.Select(x => x.MediaFile.Name), Is.EqualTo(new[] { $"urun-{product.Id}-1.jpg", $"urun-{product.Id}-2.png", $"urun-{product.Id}-3.jpg" }));
        Assert.That(result.Photos.Select(x => x.FileId), Is.EqualTo(ordered.Select(x => x.MediaFileId)));
    }

    [Test]
    public async Task Wrong_file_type_is_refused_before_anything_is_saved()
    {
        var category = await AddCategoryAsync("Kask");

        // A PDF renamed to .jpg and declared as image/jpeg.
        var result = await CreateService().CreateAsync(Quick("Kask", category.Id), [Photo("fatura.jpg", Pdf)], 1);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors["Photos"], Is.EqualTo(Key("Photo.Type")));
        Assert.That(await Db.Products.CountAsync(), Is.Zero);
        Assert.That(await Db.MediaFiles.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Size_and_count_limits_are_enforced()
    {
        var category = await AddCategoryAsync("Kask");
        var service = CreateService();

        var tooLarge = await service.CreateAsync(Quick("A", category.Id), [Photo("buyuk.jpg", Jpeg, claimedLength: ProductPhotoRules.MaxBytes + 1)], 1);
        var tooMany = await service.CreateAsync(Quick("B", category.Id),
            [.. Enumerable.Range(1, ProductPhotoRules.MaxPhotos + 1).Select(i => Photo($"{i}.jpg", Jpeg))], 1);

        Assert.That(tooLarge.Errors["Photos"], Is.EqualTo(Key("Photo.TooLarge")));
        Assert.That(tooMany.Errors["Photos"], Is.EqualTo(Key("Photo.TooMany")));
        Assert.That(await Db.Products.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Failed_photo_storage_rolls_the_whole_product_back()
    {
        var category = await AddCategoryAsync("Kask");
        var service = CreateService();
        service.FailPhotoSave = true;

        var result = await service.CreateAsync(Quick("Kask", category.Id, publish: true), [Photo("on.jpg", Jpeg)], 1);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors["Photos"], Is.EqualTo(Key("Photo.SaveFailed")));
        using var read = NewContext();
        Assert.That(await read.Products.CountAsync(), Is.Zero, "no half-created product");
        Assert.That(await read.ProductCategories.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Choosing_the_main_photo_reorders_and_removing_it_promotes_the_next()
    {
        var category = await AddCategoryAsync("Kask");
        var created = await CreateService().CreateAsync(Quick("Kask", category.Id),
            [Photo("1.jpg", Jpeg), Photo("2.jpg", Jpeg), Photo("3.jpg", Jpeg)], 1);
        var ids = created.Photos.Select(x => x.FileId).ToArray();

        using (var db = NewContext())
        {
            var ordered = await CreateService(db: db).ReorderPhotosAsync(created.ProductId, created.Version, [ids[2], ids[0], ids[1]]);
            Assert.That(ordered.Success, Is.True);
            Assert.That(ordered.Photos.Select(x => x.FileId), Is.EqualTo(new[] { ids[2], ids[0], ids[1] }));
        }

        using (var read = NewContext())
        {
            Assert.That((await read.Products.SingleAsync()).MainPictureId, Is.EqualTo(ids[2]));
        }

        using (var db = NewContext())
        {
            var removed = await CreateService(db: db).RemovePhotoAsync(created.ProductId, created.Version, ids[2]);
            Assert.That(removed.Photos.Select(x => x.FileId), Is.EqualTo(new[] { ids[0], ids[1] }));
        }

        using (var read = NewContext())
        {
            Assert.That((await read.Products.SingleAsync()).MainPictureId, Is.EqualTo(ids[0]));
            // Only the assignment is removed; the file stays in the media manager like in the classic editor.
            Assert.That(await read.MediaFiles.CountAsync(x => x.Id == ids[2]), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Order_must_name_exactly_the_products_photos()
    {
        var category = await AddCategoryAsync("Kask");
        var created = await CreateService().CreateAsync(Quick("Kask", category.Id), [Photo("1.jpg", Jpeg), Photo("2.jpg", Jpeg)], 1);
        var ids = created.Photos.Select(x => x.FileId).ToArray();

        using var db = NewContext();
        var service = CreateService(db: db);
        Assert.That((await service.ReorderPhotosAsync(created.ProductId, created.Version, [ids[0]])).Errors[""], Is.EqualTo(Key("Photo.OrderChanged")));
        Assert.That((await service.ReorderPhotosAsync(created.ProductId, created.Version, [ids[0], 999])).Errors[""], Is.EqualTo(Key("Photo.OrderChanged")));
    }

    [Test]
    public async Task Adding_photos_to_an_existing_product_appends_them()
    {
        var category = await AddCategoryAsync("Kask");
        var created = await CreateService().CreateAsync(Quick("Kask", category.Id), [], 1);

        using (var db = NewContext())
        {
            var first = await CreateService(db: db).AddPhotosAsync(created.ProductId, created.Version, [Photo("a.jpg", Jpeg)]);
            Assert.That(first.Success, Is.True);
        }
        using (var db = NewContext())
        {
            var second = await CreateService(db: db).AddPhotosAsync(created.ProductId, created.Version, [Photo("b.png", Png, "image/png")]);
            Assert.That(second.Photos, Has.Count.EqualTo(2));
        }
        using (var db = NewContext())
        {
            var refused = await CreateService(db: db).AddPhotosAsync(created.ProductId, created.Version, [Photo("x.jpg", Pdf)]);
            Assert.That(refused.Errors["Photos"], Is.EqualTo(Key("Photo.Type")));
        }

        using var read = NewContext();
        var product = await read.Products.Include(x => x.ProductMediaFiles).SingleAsync();
        Assert.That(product.ProductMediaFiles.OrderBy(x => x.DisplayOrder).Select(x => x.DisplayOrder), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(product.MainPictureId, Is.EqualTo(product.ProductMediaFiles.OrderBy(x => x.DisplayOrder).First().MediaFileId));
    }

    [Test]
    public async Task Photo_change_keeps_a_stale_editor_version_so_its_save_still_detects_the_conflict()
    {
        var category = await AddCategoryAsync("Kask");
        var created = await CreateService().CreateAsync(Quick("Kask", category.Id), [Photo("1.jpg", Jpeg)], 1);

        using var db = NewContext();
        var result = await CreateService(db: db).AddPhotosAsync(created.ProductId, version: 123, [Photo("2.jpg", Jpeg)]);

        Assert.That(result.Version, Is.EqualTo(123));
    }
}
