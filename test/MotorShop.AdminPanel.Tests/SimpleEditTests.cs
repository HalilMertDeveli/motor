using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Models;
using NUnit.Framework;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;

namespace MotorShop.AdminPanel.Tests;

/// <summary>
/// The simple product editor writes only its own fields and refuses stale or invalid input.
/// </summary>
[TestFixture]
public class SimpleEditTests : PanelDbTestBase
{
    private Category _kask;
    private Category _mont;

    [SetUp]
    public async Task SeedAsync()
    {
        _kask = await AddCategoryAsync("Kask");
        _mont = await AddCategoryAsync("Mont");
    }

    private async Task<Product> AddProductAsync(Action<Product> configure = null)
    {
        var product = new Product
        {
            Name = "SHAD SH58X",
            Price = 4500m,
            Published = false,
            ManageInventoryMethod = ManageInventoryMethod.ManageStock,
            StockQuantity = 5,
            OrderMinimumQuantity = 1,
            OrderMaximumQuantity = 50,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow
        };
        product.ProductCategories.Add(new ProductCategory { CategoryId = _kask.Id, DisplayOrder = 1 });
        configure?.Invoke(product);
        Db.Products.Add(product);
        await Db.SaveChangesAsync();
        return product;
    }

    private async Task<SimpleProductModel> LoadAsync(int id)
        => await CreateService(db: NewContext()).LoadAsync(id);

    private async Task<Product> ReadAsync(int id)
    {
        using var read = NewContext();
        return await read.Products.AsNoTracking().Include(x => x.ProductCategories).SingleAsync(x => x.Id == id);
    }

    private async Task<SimpleProductResult> SaveAsync(SimpleProductModel model, bool canEditCategories = true)
    {
        // Each save is its own request with its own context, as in the app.
        using var db = NewContext();
        return await CreateService(db: db).SaveAsync(model, canEditCategories);
    }

    [Test]
    public async Task Editor_shows_turkish_amounts_and_the_main_category()
    {
        var product = await AddProductAsync(p => p.Price = 1250.5m);

        var model = await LoadAsync(product.Id);

        Assert.That(model.Price, Is.EqualTo("1.250,50"));
        Assert.That(model.Stock, Is.EqualTo("5"));
        Assert.That(model.CategoryId, Is.EqualTo(_kask.Id));
        Assert.That(model.Version, Is.EqualTo(product.UpdatedOnUtc.Ticks));
    }

    [Test]
    public async Task Name_price_stock_description_and_publication_are_saved()
    {
        var product = await AddProductAsync();
        var model = await LoadAsync(product.Id);
        model.Name = "  SHAD SH58X Arka Çanta ";
        model.Price = "4.750,90";
        model.Stock = "12";
        model.ShortDescription = "58 litre, iki kask alır.";
        model.Published = true;

        var result = await SaveAsync(model);

        Assert.That(result.Success, Is.True, string.Join(", ", result.Errors.Values));
        Assert.That(result.Message, Is.EqualTo(Key("Simple.Saved")));
        var saved = await ReadAsync(product.Id);
        Assert.That(saved.Name, Is.EqualTo("SHAD SH58X Arka Çanta"));
        Assert.That(saved.Price, Is.EqualTo(4750.90m));
        Assert.That(saved.StockQuantity, Is.EqualTo(12));
        Assert.That(saved.ShortDescription, Is.EqualTo("58 litre, iki kask alır."));
        Assert.That(saved.Published, Is.True);
    }

    [Test]
    public async Task Advanced_fields_are_never_overwritten()
    {
        var start = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var product = await AddProductAsync(p =>
        {
            p.Sku = "SH-58X";
            p.ManufacturerPartNumber = "D0B58100";
            p.Gtin = "8430358645107";
            p.TaxCategoryId = 7;
            p.SpecialPrice = 3999m;
            p.SpecialPriceStartDateTimeUtc = start;
            p.SpecialPriceEndDateTimeUtc = start.AddDays(10);
            p.ComparePrice = 5200m;
            p.CallForPrice = false;
            p.Visibility = ProductVisibility.SearchResults;
            p.IsShippingEnabled = true;
            p.IsFreeShipping = true;
            p.Weight = 4.2m;
            p.MetaTitle = "SEO başlık";
            p.MetaDescription = "SEO açıklama";
            p.FullDescription = "<p>Uzun açıklama</p>";
            p.MinStockQuantity = 2;
            p.LowStockActivity = LowStockActivity.Unpublish;
            p.OrderMaximumQuantity = 3;
            p.DisableBuyButton = false;
        });

        var model = await LoadAsync(product.Id);
        model.Name = "Yeni ad";
        model.Price = "4.600";
        var result = await SaveAsync(model);
        Assert.That(result.Success, Is.True, string.Join(", ", result.Errors.Values));

        var saved = await ReadAsync(product.Id);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Sku, Is.EqualTo("SH-58X"));
            Assert.That(saved.ManufacturerPartNumber, Is.EqualTo("D0B58100"));
            Assert.That(saved.Gtin, Is.EqualTo("8430358645107"));
            Assert.That(saved.TaxCategoryId, Is.EqualTo(7));
            Assert.That(saved.SpecialPrice, Is.EqualTo(3999m));
            Assert.That(saved.SpecialPriceStartDateTimeUtc, Is.EqualTo(start));
            Assert.That(saved.SpecialPriceEndDateTimeUtc, Is.EqualTo(start.AddDays(10)));
            Assert.That(saved.ComparePrice, Is.EqualTo(5200m));
            Assert.That(saved.Visibility, Is.EqualTo(ProductVisibility.SearchResults));
            Assert.That(saved.IsFreeShipping, Is.True);
            Assert.That(saved.Weight, Is.EqualTo(4.2m));
            Assert.That(saved.MetaTitle, Is.EqualTo("SEO başlık"));
            Assert.That(saved.MetaDescription, Is.EqualTo("SEO açıklama"));
            Assert.That(saved.FullDescription, Is.EqualTo("<p>Uzun açıklama</p>"));
            Assert.That(saved.MinStockQuantity, Is.EqualTo(2));
            Assert.That(saved.LowStockActivity, Is.EqualTo(LowStockActivity.Unpublish));
            Assert.That(saved.OrderMaximumQuantity, Is.EqualTo(3));
            Assert.That(saved.ManageInventoryMethod, Is.EqualTo(ManageInventoryMethod.ManageStock));
            Assert.That(saved.Name, Is.EqualTo("Yeni ad"));
            Assert.That(saved.Price, Is.EqualTo(4600m));
        });
    }

    [Test]
    public async Task Untracked_stock_stays_untracked_and_the_quantity_is_ignored()
    {
        var product = await AddProductAsync(p => { p.ManageInventoryMethod = ManageInventoryMethod.DontManageStock; p.StockQuantity = 0; });
        var model = await LoadAsync(product.Id);
        model.Stock = "99";

        var result = await SaveAsync(model);

        Assert.That(result.Success, Is.True);
        var saved = await ReadAsync(product.Id);
        Assert.That(saved.ManageInventoryMethod, Is.EqualTo(ManageInventoryMethod.DontManageStock));
        Assert.That(saved.StockQuantity, Is.Zero);
    }

    [Test]
    public async Task Variant_stock_products_are_sent_to_the_advanced_editor()
    {
        var product = await AddProductAsync(p => p.ManageInventoryMethod = ManageInventoryMethod.ManageStockByAttributes);
        var model = await LoadAsync(product.Id);
        model.Name = "Değişmemeli";

        var result = await SaveAsync(model);

        Assert.That(result.Errors[""], Is.EqualTo(Key("Simple.VariantProduct")));
        Assert.That((await ReadAsync(product.Id)).Name, Is.EqualTo("SHAD SH58X"));
    }

    [Test]
    public async Task Changing_the_category_replaces_the_main_one_and_keeps_the_others()
    {
        var aksesuar = await AddCategoryAsync("Aksesuar");
        var product = await AddProductAsync(p => p.ProductCategories.Add(new ProductCategory { CategoryId = aksesuar.Id, DisplayOrder = 5 }));
        var model = await LoadAsync(product.Id);
        Assert.That(model.OtherCategories, Is.EqualTo(new[] { "Aksesuar" }));
        model.CategoryId = _mont.Id;

        var result = await SaveAsync(model);

        Assert.That(result.Success, Is.True);
        var mappings = (await ReadAsync(product.Id)).ProductCategories.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(x => x.CategoryId).ToList();
        Assert.That(mappings, Is.EqualTo(new[] { _mont.Id, aksesuar.Id }));
        Assert.That((await LoadAsync(product.Id)).CategoryId, Is.EqualTo(_mont.Id));
    }

    [Test]
    public async Task Choosing_a_category_the_product_already_has_makes_it_main_without_a_twin()
    {
        var product = await AddProductAsync(p => p.ProductCategories.Add(new ProductCategory { CategoryId = _mont.Id, DisplayOrder = 1 }));
        var model = await LoadAsync(product.Id);
        model.CategoryId = _mont.Id == model.CategoryId ? _kask.Id : _mont.Id;
        var target = model.CategoryId;

        await SaveAsync(model);

        var saved = await ReadAsync(product.Id);
        Assert.That(saved.ProductCategories.Count(x => x.CategoryId == target), Is.EqualTo(1));
        Assert.That((await LoadAsync(product.Id)).CategoryId, Is.EqualTo(target));
    }

    [Test]
    public async Task Without_category_permission_the_category_is_not_changed()
    {
        var product = await AddProductAsync();
        var model = await LoadAsync(product.Id);
        model.CategoryId = _mont.Id;

        await SaveAsync(model, canEditCategories: false);

        Assert.That((await ReadAsync(product.Id)).ProductCategories.Single().CategoryId, Is.EqualTo(_kask.Id));
    }

    [TestCase("", null, null, "Name", "Validation.NameRequired")]
    [TestCase("Ad", "abc", null, "Price", "Validation.PriceInvalid")]
    [TestCase("Ad", "", null, "Price", "Validation.PriceInvalid")]
    [TestCase("Ad", null, "-1", "Stock", "Validation.StockInvalid")]
    [TestCase("Ad", null, "", "Stock", "Validation.StockInvalid")]
    public async Task Invalid_changes_are_refused_and_nothing_is_stored(string name, string price, string stock, string field, string key)
    {
        var product = await AddProductAsync();
        var model = await LoadAsync(product.Id);
        model.Name = name;
        model.Price = price ?? model.Price;
        model.Stock = stock ?? model.Stock;

        var result = await SaveAsync(model);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors[field], Is.EqualTo(Key(key)));
        var saved = await ReadAsync(product.Id);
        Assert.That(saved.Name, Is.EqualTo("SHAD SH58X"));
        Assert.That(saved.Price, Is.EqualTo(4500m));
        Assert.That(saved.StockQuantity, Is.EqualTo(5));
    }

    [Test]
    public async Task Unknown_category_is_refused()
    {
        var product = await AddProductAsync();
        var model = await LoadAsync(product.Id);
        model.CategoryId = 9999;

        var result = await SaveAsync(model);

        Assert.That(result.Errors["CategoryId"], Is.EqualTo(Key("Validation.CategoryRequired")));
    }

    [Test]
    public async Task Publishing_needs_a_price_and_a_category()
    {
        var noCategory = await AddProductAsync(p => p.ProductCategories.Clear());
        var model = await LoadAsync(noCategory.Id);
        model.Published = true;
        Assert.That((await SaveAsync(model)).Errors["CategoryId"], Is.EqualTo(Key("Publish.CategoryRequired")));

        var zero = await AddProductAsync(p => p.Price = 0);
        model = await LoadAsync(zero.Id);
        model.Published = true;
        Assert.That((await SaveAsync(model)).Errors["Price"], Is.EqualTo(Key("Publish.PriceRequired")));

        Assert.That((await ReadAsync(noCategory.Id)).Published, Is.False);
        Assert.That((await ReadAsync(zero.Id)).Published, Is.False);
    }

    [Test]
    public async Task Concurrent_edit_is_reported_and_does_not_overwrite_the_other_change()
    {
        var product = await AddProductAsync();
        var mine = await LoadAsync(product.Id);

        // Someone else saves first (the audit hook would move UpdatedOnUtc; done by hand here).
        using (var other = NewContext())
        {
            var p = await other.Products.SingleAsync(x => x.Id == product.Id);
            p.Price = 4999m;
            p.UpdatedOnUtc = p.UpdatedOnUtc.AddSeconds(5);
            await other.SaveChangesAsync();
        }

        mine.Price = "1.000";
        var result = await SaveAsync(mine);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Conflict, Is.True);
        Assert.That(result.Errors[""], Is.EqualTo(Key("Simple.Conflict")));
        Assert.That((await ReadAsync(product.Id)).Price, Is.EqualTo(4999m));
    }

    [Test]
    public async Task Deleted_product_cannot_be_edited()
    {
        var product = await AddProductAsync(p => p.Deleted = true);

        Assert.That(await LoadAsync(product.Id), Is.Null);
        var result = await SaveAsync(new SimpleProductModel { Id = product.Id, Name = "x", Price = "1", Stock = "1" });
        Assert.That(result.Errors[""], Is.EqualTo(Key("NotFound")));
    }

    [Test]
    public async Task Existing_slug_is_kept_and_a_missing_one_is_created()
    {
        var product = await AddProductAsync();
        using var db = NewContext();
        var service = CreateService(db: db);
        var model = await service.LoadAsync(product.Id);

        model.Name = "Yeni ad";
        await service.SaveAsync(model, true);
        Assert.That(service.SlugCalls, Is.EqualTo(1), "a product without slug gets one");

        model = await service.LoadAsync(product.Id);
        model.Name = "Bir daha";
        await service.SaveAsync(model, true);
        Assert.That(service.SlugCalls, Is.EqualTo(1), "an existing slug is not regenerated from the new name");
    }
}
