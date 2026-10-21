using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using MotorShop.AdminPanel.Configuration;
using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Security;

namespace MotorShop.AdminPanel.Tests;

[TestFixture]
public class PriceLockTests
{
    const string Message = "Satış kapalı";

    private static Lazy<MotorPanelSettings> Settings(bool locked)
        => new(() => new MotorPanelSettings { HideAllPrices = locked });

    private static PriceLockPermissionService Permissions(bool locked)
    {
        var inner = new Mock<IPermissionService>();
        inner.Setup(x => x.AuthorizeAsync(It.IsAny<string>(), It.IsAny<Customer>(), It.IsAny<bool>())).ReturnsAsync(true);
        inner.Setup(x => x.Authorize(It.IsAny<string>(), It.IsAny<Customer>(), It.IsAny<bool>())).Returns(true);

        return new PriceLockPermissionService(inner.Object, Settings(locked));
    }

    private static (PriceLockCartValidator Validator, Mock<IShoppingCartValidator> Inner) Cart(bool locked)
    {
        var inner = new Mock<IShoppingCartValidator>();
        inner.Setup(x => x.ValidateAccessPermissionsAsync(It.IsAny<Customer>(), It.IsAny<ShoppingCartType>(), It.IsAny<IList<string>>())).ReturnsAsync(true);
        inner.Setup(x => x.ValidateCartAsync(It.IsAny<ShoppingCart>(), It.IsAny<IList<string>>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(true);

        var localization = new Mock<ILocalizationService>();
        localization
            .Setup(x => x.GetResource(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(Message);

        return (new PriceLockCartValidator(inner.Object, Settings(locked), new Lazy<ILocalizationService>(() => localization.Object)), inner);
    }

    [Test]
    public async Task Unlocked_store_changes_nothing()
    {
        var permissions = Permissions(false);
        Assert.That(await permissions.AuthorizeAsync(Smartstore.Core.Security.Permissions.Catalog.DisplayPrice), Is.True);

        var (cart, _) = Cart(false);
        var warnings = new List<string>();
        Assert.That(await cart.ValidateAccessPermissionsAsync(new Customer(), ShoppingCartType.ShoppingCart, warnings), Is.True);
        Assert.That(warnings, Is.Empty);
    }

    [Test]
    public async Task Locked_store_denies_only_the_display_price_permission()
    {
        var permissions = Permissions(true);

        Assert.That(await permissions.AuthorizeAsync(Smartstore.Core.Security.Permissions.Catalog.DisplayPrice), Is.False);
        Assert.That(permissions.Authorize(Smartstore.Core.Security.Permissions.Catalog.DisplayPrice), Is.False);
        Assert.That(await permissions.AuthorizeAsync(Smartstore.Core.Security.Permissions.Cart.AccessShoppingCart), Is.True);
        Assert.That(await permissions.AuthorizeAsync(Smartstore.Core.Security.Permissions.System.AccessBackend), Is.True);
    }

    [Test]
    public async Task Locked_store_refuses_the_shopping_cart_on_the_server()
    {
        var (cart, inner) = Cart(true);
        var warnings = new List<string>();

        Assert.That(await cart.ValidateAccessPermissionsAsync(new Customer(), ShoppingCartType.ShoppingCart, warnings), Is.False);
        Assert.That(warnings, Is.EqualTo(new[] { Message }));

        // Checkout and order placement validate the whole cart.
        var checkout = new List<string>();
        Assert.That(await cart.ValidateCartAsync(new ShoppingCart(new Customer(), 1, []), checkout, true, true), Is.False);
        Assert.That(checkout, Does.Contain(Message));

        // The inner validator still runs so its own warnings are shown too.
        inner.Verify(x => x.ValidateCartAsync(It.IsAny<ShoppingCart>(), It.IsAny<IList<string>>(), true, true), Times.Once);
    }

    [Test]
    public async Task Wishlist_stays_usable_while_locked()
    {
        var (cart, _) = Cart(true);
        var warnings = new List<string>();

        Assert.That(await cart.ValidateAccessPermissionsAsync(new Customer(), ShoppingCartType.Wishlist, warnings), Is.True);
        Assert.That(warnings, Is.Empty);
    }

    [Test]
    public async Task Message_is_added_once()
    {
        var (cart, _) = Cart(true);
        var warnings = new List<string>();

        await cart.ValidateAccessPermissionsAsync(new Customer(), ShoppingCartType.ShoppingCart, warnings);
        await cart.ValidateAccessPermissionsAsync(new Customer(), ShoppingCartType.ShoppingCart, warnings);

        Assert.That(warnings, Has.Count.EqualTo(1));
    }
}
