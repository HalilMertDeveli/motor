using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore.Core;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Common;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Logging;
using Smartstore.Core.Seo;
using Smartstore.Events;

namespace MotorShop.AdminPanel.Tests;

[TestFixture]
public class PriceVisibilityRuleTests
{
    // callForPrice, oldPrice, newPrice, showPrice => stays hidden
    [TestCase(true, 0, 500, null, ExpectedResult = false, Description = "hidden only because it had no price: entering one shows it")]
    [TestCase(true, 0, 0, null, ExpectedResult = true, Description = "still no price: stays hidden")]
    [TestCase(true, 450, 500, null, ExpectedResult = true, Description = "an admin hid a priced product on purpose: stays hidden")]
    [TestCase(false, 450, 500, null, ExpectedResult = false)]
    [TestCase(true, 450, 500, true, ExpectedResult = false, Description = "explicit choice wins")]
    [TestCase(true, 0, 500, false, ExpectedResult = true, Description = "explicit choice wins")]
    [TestCase(false, 450, 500, false, ExpectedResult = true)]
    public bool Price_visibility(bool callForPrice, decimal oldPrice, decimal newPrice, bool? showPrice)
        => SimpleProductService.PriceVisibility(callForPrice, oldPrice, newPrice, showPrice);
}

/// <summary>
/// Reported 2026-10-27: a price entered for a supplier product with "Fiyat için arayın" was saved but never shown.
/// </summary>
[TestFixture]
public class PriceVisibilityTests : PanelDbTestBase
{
    private Category _category;

    [SetUp]
    public async Task SeedAsync()
        => _category = await AddCategoryAsync("Bot");

    /// <summary>
    /// Like the supplier import creates an unpriced product: price 0, price hidden, draft.
    /// </summary>
    private async Task<Product> AddImportedAsync(decimal price = 0, bool published = false)
    {
        var product = new Product
        {
            Name = "MXPRO Cross Bot Bağlantı Plastiği",
            Price = price,
            CallForPrice = true,
            Published = published,
            ManageInventoryMethod = ManageInventoryMethod.DontManageStock,
            OrderMinimumQuantity = 1,
            OrderMaximumQuantity = 50,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow
        };
        product.ProductCategories.Add(new ProductCategory { CategoryId = _category.Id, DisplayOrder = 1 });
        Db.Products.Add(product);
        await Db.SaveChangesAsync();
        return product;
    }

    private async Task<SimpleProductResult> SaveAsync(int id, Action<SimpleProductModel> change)
    {
        using var db = NewContext();
        var service = CreateService(db: db);
        var model = await service.LoadAsync(id);
        change(model);
        return await service.SaveAsync(model, true);
    }

    private async Task<Product> ReadAsync(int id)
    {
        using var read = NewContext();
        return await read.Products.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    [Test]
    public async Task Editor_shows_the_hidden_price_state()
    {
        var product = await AddImportedAsync();

        var model = await CreateService(db: NewContext()).LoadAsync(product.Id);

        Assert.That(model.CallForPrice, Is.True);
        Assert.That(model.Price, Is.EqualTo("0,00"));
    }

    [Test]
    public async Task Entering_a_price_with_show_price_ticked_makes_it_visible_and_purchasable()
    {
        var product = await AddImportedAsync();

        var result = await SaveAsync(product.Id, m => { m.Price = "50.000"; m.ShowPrice = true; m.Published = true; });

        Assert.That(result.Success, Is.True, string.Join(", ", result.Errors.Values));
        Assert.That(result.CallForPrice, Is.False);
        Assert.That(result.Warning, Is.Null);
        var saved = await ReadAsync(product.Id);
        Assert.That(saved.Price, Is.EqualTo(50000m));
        Assert.That(saved.CallForPrice, Is.False);
        Assert.That(saved.Published, Is.True);

        using var read = NewContext();
        var storefront = await read.Products.ApplyStandardFilter(false).Where(x => x.Id == product.Id && !x.CallForPrice).CountAsync();
        Assert.That(storefront, Is.EqualTo(1), "published with a visible price");
    }

    [Test]
    public async Task Without_an_explicit_choice_a_price_for_an_unpriced_product_is_shown()
    {
        var product = await AddImportedAsync();

        await SaveAsync(product.Id, m => { m.Price = "1.250,50"; m.ShowPrice = null; });

        Assert.That((await ReadAsync(product.Id)).CallForPrice, Is.False);
    }

    [Test]
    public async Task Unticked_show_price_keeps_the_price_hidden()
    {
        var product = await AddImportedAsync();

        await SaveAsync(product.Id, m => { m.Price = "1.250,50"; m.ShowPrice = false; });

        var saved = await ReadAsync(product.Id);
        Assert.That(saved.Price, Is.EqualTo(1250.50m));
        Assert.That(saved.CallForPrice, Is.True);
    }

    [Test]
    public async Task A_price_hidden_on_purpose_stays_hidden_when_only_the_price_changes()
    {
        var product = await AddImportedAsync(price: 450);

        await SaveAsync(product.Id, m => { m.Price = "500"; m.ShowPrice = null; });

        Assert.That((await ReadAsync(product.Id)).CallForPrice, Is.True);
    }

    [Test]
    public async Task Showing_a_price_of_zero_is_refused()
    {
        var product = await AddImportedAsync();

        var draft = await SaveAsync(product.Id, m => { m.Price = "0"; m.ShowPrice = true; });
        var published = await SaveAsync(product.Id, m => { m.Price = "0"; m.ShowPrice = true; m.Published = true; });

        Assert.That(draft.Errors["Price"], Is.EqualTo(Key("Simple.ShowPriceNeedsPrice")));
        Assert.That(published.Errors["Price"], Is.EqualTo(Key("Publish.PriceRequired")));
        Assert.That((await ReadAsync(product.Id)).CallForPrice, Is.True);
    }

    [Test]
    public async Task Hidden_price_product_may_be_published_without_a_price()
    {
        var product = await AddImportedAsync();

        var result = await SaveAsync(product.Id, m => { m.Price = "0"; m.ShowPrice = false; m.Published = true; });

        Assert.That(result.Success, Is.True, string.Join(", ", result.Errors.Values));
        Assert.That((await ReadAsync(product.Id)).Published, Is.True);
    }

    [Test]
    public async Task Hiding_the_price_again_is_possible()
    {
        var product = await AddImportedAsync(price: 300);
        await SaveAsync(product.Id, m => m.ShowPrice = true);
        Assert.That((await ReadAsync(product.Id)).CallForPrice, Is.False);

        await SaveAsync(product.Id, m => m.ShowPrice = false);

        Assert.That((await ReadAsync(product.Id)).CallForPrice, Is.True);
    }

    // ---------- Inline price in the list ----------

    private ProductEditService CreateEditService(Smartstore.Core.Data.SmartDbContext db)
    {
        var currency = new Currency { CurrencyCode = "TRY", DisplayLocale = "tr-TR", Name = "Türk Lirası" };
        var currencies = new Mock<ICurrencyService>();
        currencies.SetupGet(x => x.PrimaryCurrency).Returns(currency);
        currencies.Setup(x => x.CreateMoney(It.IsAny<decimal>(), It.IsAny<object>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns<decimal, object, bool, bool>((amount, _, _, _) => new Money(amount, currency));

        var services = new Mock<ICommonServices>();
        services.SetupGet(x => x.ActivityLogger).Returns(Mock.Of<IActivityLogger>());
        services.SetupGet(x => x.CurrencyService).Returns(currencies.Object);

        return new ProductEditService(db, services.Object, null, Mock.Of<IProductCloner>(), Mock.Of<IProductTagService>(),
            Mock.Of<IUrlService>(), Mock.Of<IMediaService>(), Mock.Of<IEventPublisher>());
    }

    [Test]
    public async Task Inline_price_for_an_unpriced_hidden_product_shows_the_price()
    {
        var product = await AddImportedAsync();

        using var db = NewContext();
        var result = await CreateEditService(db).UpdateFieldAsync(product.Id, ProductRules.Fields.Price, "2.500", "0");

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.CallForPrice, Is.False);
        var saved = await ReadAsync(product.Id);
        Assert.That(saved.Price, Is.EqualTo(2500m));
        Assert.That(saved.CallForPrice, Is.False);
    }

    [Test]
    public async Task Inline_price_keeps_a_price_hidden_on_purpose()
    {
        var product = await AddImportedAsync(price: 450);

        using var db = NewContext();
        var result = await CreateEditService(db).UpdateFieldAsync(product.Id, ProductRules.Fields.Price, "500", "450");

        Assert.That(result.CallForPrice, Is.True);
        Assert.That((await ReadAsync(product.Id)).CallForPrice, Is.True);
    }

    [Test]
    public async Task Inline_toggle_shows_and_hides_the_price()
    {
        var product = await AddImportedAsync(price: 450, published: true);

        using (var db = NewContext())
        {
            var shown = await CreateEditService(db).UpdateFieldAsync(product.Id, ProductRules.Fields.CallForPrice, "false", "true");
            Assert.That(shown.Success, Is.True, shown.Error);
        }
        Assert.That((await ReadAsync(product.Id)).CallForPrice, Is.False);

        using (var db = NewContext())
        {
            await CreateEditService(db).UpdateFieldAsync(product.Id, ProductRules.Fields.CallForPrice, "true", "false");
        }
        Assert.That((await ReadAsync(product.Id)).CallForPrice, Is.True);
    }

    [Test]
    public async Task Inline_toggle_cannot_show_a_zero_price_on_a_published_product()
    {
        var product = await AddImportedAsync(price: 0, published: true);

        using var db = NewContext();
        var result = await CreateEditService(db).UpdateFieldAsync(product.Id, ProductRules.Fields.CallForPrice, "false", "true");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo(Key("Rule.ZeroPriceSellable")));
        Assert.That((await ReadAsync(product.Id)).CallForPrice, Is.True);
    }
}
