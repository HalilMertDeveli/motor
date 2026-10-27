using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Installation;

namespace MotorShop.AdminPanel.Tests;

/// <summary>
/// "Yeni Ürün Ekle": validation, defaults, duplicate submissions and what the admin list and the storefront see.
/// </summary>
[TestFixture]
public class QuickCreateTests : PanelDbTestBase
{
    [Test]
    public async Task Valid_draft_is_saved_unpublished_with_category_stock_sku_and_slug()
    {
        var category = await AddCategoryAsync("Kask");
        var service = CreateService();

        var result = await service.CreateAsync(Quick("SHAD Kask", category.Id, price: "1.250,50", stock: "7"), [], 1);

        Assert.That(result.Success, Is.True, string.Join(", ", result.Errors.Values));
        Assert.That(result.Published, Is.False);
        Assert.That(result.Slug, Is.EqualTo("urun-" + result.ProductId));

        using var read = NewContext();
        var product = await read.Products.Include(x => x.ProductCategories).SingleAsync(x => x.Id == result.ProductId);
        Assert.That(product.Name, Is.EqualTo("SHAD Kask"));
        Assert.That(product.Published, Is.False);
        Assert.That(product.Price, Is.EqualTo(1250.50m));
        Assert.That(product.ManageInventoryMethod, Is.EqualTo(ManageInventoryMethod.ManageStock));
        Assert.That(product.StockQuantity, Is.EqualTo(7));
        Assert.That(product.ProductType, Is.EqualTo(ProductType.SimpleProduct));
        Assert.That(product.Visibility, Is.EqualTo(ProductVisibility.Full));
        Assert.That(product.Sku, Does.Match(@"^MP-\d{6}-[A-Z2-9]{5}$"));
        Assert.That(product.ProductCategories.Select(x => x.CategoryId), Is.EqualTo(new[] { category.Id }));
    }

    [TestCase("4500", 4500)]
    [TestCase("4.500,00", 4500)]
    [TestCase("1.250,50", 1250.50)]
    [TestCase("₺ 99,90", 99.90)]
    public async Task Publish_accepts_turkish_price_notation(string input, decimal expected)
    {
        var category = await AddCategoryAsync("Eldiven");

        var result = await CreateService().CreateAsync(Quick("Eldiven", category.Id, price: input, publish: true), [], 1);

        Assert.That(result.Success, Is.True, string.Join(", ", result.Errors.Values));
        Assert.That(result.Published, Is.True);
        using var read = NewContext();
        Assert.That((await read.Products.SingleAsync()).Price, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Missing_name_is_refused_and_nothing_is_saved(string name)
    {
        var category = await AddCategoryAsync("Kask");

        var result = await CreateService().CreateAsync(Quick(name, category.Id, publish: true), [], 1);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors["Name"], Is.EqualTo(Key("Validation.NameRequired")));
        Assert.That(await Db.Products.CountAsync(), Is.Zero);
    }

    [TestCase("abc", "Validation.PriceInvalid")]
    [TestCase("12,345", "Validation.PriceDecimals")]
    [TestCase("-5", "Validation.PriceInvalid")]
    [TestCase("1.2.3", "Validation.PriceInvalid")]
    public async Task Invalid_price_is_refused(string price, string expectedKey)
    {
        var category = await AddCategoryAsync("Kask");

        var result = await CreateService().CreateAsync(Quick("Kask", category.Id, price: price), [], 1);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors["Price"], Is.EqualTo(Key(expectedKey)));
        Assert.That(await Db.Products.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Publishing_needs_a_price_above_zero_but_a_draft_does_not()
    {
        var category = await AddCategoryAsync("Kask");
        var service = CreateService();

        var empty = await service.CreateAsync(Quick("Kask", category.Id, price: "", publish: true), [], 1);
        var zero = await service.CreateAsync(Quick("Kask", category.Id, price: "0", publish: true), [], 1);
        var draft = await service.CreateAsync(Quick("Kask taslak", category.Id, price: "", publish: false), [], 1);

        Assert.That(empty.Errors["Price"], Is.EqualTo(Key("Validation.PriceInvalid")));
        Assert.That(zero.Errors["Price"], Is.EqualTo(Key("Publish.PriceRequired")));
        Assert.That(draft.Success, Is.True);
        Assert.That(await Db.Products.Select(x => x.Name).ToListAsync(), Is.EqualTo(new[] { "Kask taslak" }));
    }

    [Test]
    public async Task Invalid_category_is_refused()
    {
        var deleted = await AddCategoryAsync("Eski", deleted: true);
        var service = CreateService();

        foreach (var categoryId in new[] { 0, -1, 9999, deleted.Id })
        {
            var result = await service.CreateAsync(Quick("Kask", categoryId), [], 1);
            Assert.That(result.Errors["CategoryId"], Is.EqualTo(Key("Validation.CategoryRequired")), categoryId.ToString());
        }

        Assert.That(await Db.Products.CountAsync(), Is.Zero);
    }

    [TestCase("-3")]
    [TestCase("1,5")]
    [TestCase("abc")]
    [TestCase("1000001")]
    public async Task Invalid_stock_is_refused(string stock)
    {
        var category = await AddCategoryAsync("Kask");

        var result = await CreateService().CreateAsync(Quick("Kask", category.Id, stock: stock), [], 1);

        Assert.That(result.Errors["Stock"], Is.EqualTo(Key("Validation.StockInvalid")));
    }

    [Test]
    public async Task Empty_stock_means_zero_and_thousands_are_understood()
    {
        var category = await AddCategoryAsync("Kask");
        var service = CreateService();

        var empty = await service.CreateAsync(Quick("A", category.Id, stock: ""), [], 1);
        var thousand = await service.CreateAsync(Quick("B", category.Id, stock: "1.000"), [], 1);

        using var read = NewContext();
        Assert.That((await read.Products.SingleAsync(x => x.Id == empty.ProductId)).StockQuantity, Is.Zero);
        Assert.That((await read.Products.SingleAsync(x => x.Id == thousand.ProductId)).StockQuantity, Is.EqualTo(1000));
    }

    [Test]
    public async Task Duplicate_submission_returns_the_first_product_instead_of_a_twin()
    {
        var category = await AddCategoryAsync("Kask");
        var registry = new SubmissionRegistry();
        var model = Quick("Çift tık", category.Id, publish: true);

        var first = await CreateService(registry).CreateAsync(model, [], 1);
        // A new request scope (new service and context), same form.
        using var other = NewContext();
        var second = await CreateService(registry, other).CreateAsync(model, [], 1);

        Assert.That(first.Success && second.Success, Is.True);
        Assert.That(second.ProductId, Is.EqualTo(first.ProductId));
        Assert.That(second.Message, Is.EqualTo(Key("Create.AlreadySaved")));
        Assert.That(await Db.Products.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Same_form_of_another_user_and_a_corrected_form_are_not_blocked()
    {
        var category = await AddCategoryAsync("Kask");
        var registry = new SubmissionRegistry();
        var service = CreateService(registry);
        var model = Quick("", category.Id);

        var invalid = await service.CreateAsync(model, [], 1);
        model.Name = "Düzeltildi";
        var corrected = await service.CreateAsync(model, [], 1);
        var otherUser = await service.CreateAsync(model, [], 2);

        Assert.That(invalid.Success, Is.False);
        Assert.That(corrected.Success, Is.True);
        Assert.That(otherUser.ProductId, Is.Not.EqualTo(corrected.ProductId));
    }

    [Test]
    public void A_submission_still_in_progress_is_refused()
    {
        var registry = new SubmissionRegistry();
        Assert.That(registry.Begin("1:abc", out _), Is.EqualTo(SubmissionRegistry.State.Started));
        Assert.That(registry.Begin("1:abc", out _), Is.EqualTo(SubmissionRegistry.State.InProgress));

        registry.Complete("1:abc", 42);
        Assert.That(registry.Begin("1:abc", out var id), Is.EqualTo(SubmissionRegistry.State.Completed));
        Assert.That(id, Is.EqualTo(42));
    }

    [Test]
    public async Task Given_sku_is_kept_and_a_taken_sku_is_refused()
    {
        var category = await AddCategoryAsync("Kask");
        var service = CreateService();

        var model = Quick("A", category.Id);
        model.Sku = " SH-58X ";
        var first = await service.CreateAsync(model, [], 1);

        var twin = Quick("B", category.Id);
        twin.Sku = "SH-58X";
        var second = await service.CreateAsync(twin, [], 1);

        using var read = NewContext();
        Assert.That((await read.Products.SingleAsync(x => x.Id == first.ProductId)).Sku, Is.EqualTo("SH-58X"));
        Assert.That(second.Errors["Sku"], Is.EqualTo(Key("Validation.SkuTaken")));
    }

    [Test]
    public async Task Generated_skus_are_unique()
    {
        var service = CreateService();
        var skus = new System.Collections.Generic.HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            Assert.That(skus.Add(await service.GenerateSkuAsync()), Is.True);
        }
    }

    [Test]
    public async Task New_product_gets_the_general_vat_category_like_imported_products()
    {
        var category = await AddCategoryAsync("Kask");
        await AddTaxCategoryAsync("İndirimli");
        var general = await AddTaxCategoryAsync(TurkishStoreDefaults.TaxCategories[0].Name);

        var result = await CreateService().CreateAsync(Quick("Kask", category.Id), [], 1);

        using var read = NewContext();
        Assert.That((await read.Products.SingleAsync(x => x.Id == result.ProductId)).TaxCategoryId, Is.EqualTo(general.Id));
    }

    [Test]
    public async Task Created_product_appears_in_the_admin_list_first()
    {
        var category = await AddCategoryAsync("Kask");
        var service = CreateService();
        await service.CreateAsync(Quick("Eski ürün", category.Id), [], 1);
        var created = await service.CreateAsync(Quick("Yeni ürün", category.Id), [], 1);

        using var read = NewContext();
        var newest = await PanelQueryService.ApplySorting(PanelQueryService.CountableProducts(read.Products), ProductSort.Newest).FirstAsync();

        Assert.That(newest.Id, Is.EqualTo(created.ProductId));
    }

    [Test]
    public async Task Published_product_is_visible_on_the_storefront_and_a_draft_is_not()
    {
        var category = await AddCategoryAsync("Kask");
        var service = CreateService();
        var published = await service.CreateAsync(Quick("Yayında", category.Id, publish: true), [], 1);
        var draft = await service.CreateAsync(Quick("Taslak", category.Id, publish: false), [], 1);

        using var read = NewContext();
        // Smartstore's storefront filter (catalog lists, search) and the category page query.
        var storefront = await read.Products.ApplyStandardFilter(false).Select(x => x.Id).ToListAsync();
        var inCategory = await read.ProductCategories
            .Where(x => x.CategoryId == category.Id && x.Product.Published && !x.Product.Deleted)
            .Select(x => x.ProductId)
            .ToListAsync();

        Assert.That(storefront, Does.Contain(published.ProductId).And.Not.Contain(draft.ProductId));
        Assert.That(inCategory, Is.EqualTo(new[] { published.ProductId }));
    }
}
