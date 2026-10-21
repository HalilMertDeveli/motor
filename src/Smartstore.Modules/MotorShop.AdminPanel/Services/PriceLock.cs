using MotorShop.AdminPanel.Configuration;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Identity;
using Smartstore.Core.Security;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// "Hide all prices": Smartstore hides prices and buy buttons everywhere when
/// <see cref="Permissions.Catalog.DisplayPrice"/> is denied, but its cart does not check that permission.
/// So the lock is enforced twice: the permission is denied (display) and the cart validator refuses
/// shopping cart items (add to cart, cart page, checkout, order placement).
/// </summary>
public class PriceLockPermissionService : IPermissionService
{
    private readonly IPermissionService _inner;
    private readonly Lazy<MotorPanelSettings> _settings;

    public PriceLockPermissionService(IPermissionService inner, Lazy<MotorPanelSettings> settings)
    {
        _inner = inner;
        _settings = settings;
    }

    private bool Locked(string permissionSystemName)
        => permissionSystemName == Permissions.Catalog.DisplayPrice && _settings.Value.HideAllPrices;

    public bool Authorize(string permissionSystemName, Customer customer = null, bool allowByDescendantPermission = false)
        => !Locked(permissionSystemName) && _inner.Authorize(permissionSystemName, customer, allowByDescendantPermission);

    public async Task<bool> AuthorizeAsync(string permissionSystemName, Customer customer = null, bool allowByDescendantPermission = false)
        => !Locked(permissionSystemName) && await _inner.AuthorizeAsync(permissionSystemName, customer, allowByDescendantPermission);

    public Task<PermissionTree> GetPermissionTreeAsync(CustomerRole role, bool addDisplayNames = false)
        => _inner.GetPermissionTreeAsync(role, addDisplayNames);

    public Task<PermissionTree> BuildCustomerPermissionTreeAsync(Customer customer, bool addDisplayNames = false)
        => _inner.BuildCustomerPermissionTreeAsync(customer, addDisplayNames);

    public Task<Dictionary<string, string>> GetAllSystemNamesAsync()
        => _inner.GetAllSystemNamesAsync();

    public Task<string> GetDisplayNameAsync(string permissionSystemName)
        => _inner.GetDisplayNameAsync(permissionSystemName);

    public Task<string> GetUnauthorizedMessageAsync(string permissionSystemName)
        => _inner.GetUnauthorizedMessageAsync(permissionSystemName);

    public Task InstallPermissionsAsync(IPermissionProvider[] permissionProviders, bool removeUnusedPermissions = false)
        => _inner.InstallPermissionsAsync(permissionProviders, removeUnusedPermissions);
}

public class PriceLockCartValidator : IShoppingCartValidator
{
    private readonly IShoppingCartValidator _inner;
    private readonly Lazy<MotorPanelSettings> _settings;
    private readonly Lazy<ILocalizationService> _localization;

    public PriceLockCartValidator(IShoppingCartValidator inner, Lazy<MotorPanelSettings> settings, Lazy<ILocalizationService> localization)
    {
        _inner = inner;
        _settings = settings;
        _localization = localization;
    }

    /// <summary>
    /// Wishlists stay usable; only shopping cart items are refused.
    /// </summary>
    private bool Refuse(ShoppingCartType cartType, IList<string> warnings)
    {
        if (cartType != ShoppingCartType.ShoppingCart || !_settings.Value.HideAllPrices)
        {
            return false;
        }

        var message = _localization.Value.GetResource("Plugins.MotorShop.AdminPanel.PriceLock.CartBlocked");
        if (!warnings.Contains(message))
        {
            warnings.Add(message);
        }

        return true;
    }

    public async Task<bool> ValidateAccessPermissionsAsync(Customer customer, ShoppingCartType cartType, IList<string> warnings)
        => !Refuse(cartType, warnings) & await _inner.ValidateAccessPermissionsAsync(customer, cartType, warnings);

    public async Task<bool> ValidateCartAsync(ShoppingCart cart, IList<string> warnings, bool validateCheckoutAttributes = false, bool validateRequiredProducts = false)
        => !Refuse(cart.CartType, warnings) & await _inner.ValidateCartAsync(cart, warnings, validateCheckoutAttributes, validateRequiredProducts);

    public async Task<bool> ValidateAddToCartItemAsync(AddToCartContext ctx, ShoppingCartItem cartItem, IEnumerable<OrganizedShoppingCartItem> cartItems)
        => !Refuse(ctx.CartType, ctx.Warnings) & await _inner.ValidateAddToCartItemAsync(ctx, cartItem, cartItems);

    public async Task<bool> ValidateProductAsync(ShoppingCartItem cartItem, IEnumerable<OrganizedShoppingCartItem> cartItems, IList<string> warnings, int? storeId = null, int? quantity = null)
        => !Refuse(cartItem.ShoppingCartType, warnings) & await _inner.ValidateProductAsync(cartItem, cartItems, warnings, storeId, quantity);

    public bool ValidateBundleItem(ProductBundleItem bundleItem, IList<string> warnings)
        => _inner.ValidateBundleItem(bundleItem, warnings);

    public bool ValidateItemsMaximumCartQuantity(ShoppingCartType cartType, int cartItemsCount, IList<string> warnings)
        => _inner.ValidateItemsMaximumCartQuantity(cartType, cartItemsCount, warnings);

    public bool ValidateGiftCardInfo(Product product, ProductVariantAttributeSelection selection, IList<string> warnings)
        => _inner.ValidateGiftCardInfo(product, selection, warnings);

    public Task<bool> ValidateProductAttributesAsync(
        Product product,
        ProductVariantAttributeSelection selection,
        int storeId,
        IList<string> warnings,
        int quantity = 1,
        Customer customer = null,
        ProductBundleItem bundleItem = null,
        ShoppingCartType cartType = ShoppingCartType.ShoppingCart,
        IEnumerable<OrganizedShoppingCartItem> cartItems = null)
        => _inner.ValidateProductAttributesAsync(product, selection, storeId, warnings, quantity, customer, bundleItem, cartType, cartItems);

    public Task<bool> ValidateRequiredProductsAsync(Product product, IEnumerable<OrganizedShoppingCartItem> cartItems, IList<string> warnings)
        => _inner.ValidateRequiredProductsAsync(product, cartItems, warnings);

    public Task<bool> ValidateRequiredProductsQuantityAsync(ShoppingCartItem cartItem, ShoppingCart cart, IList<string> warnings)
        => _inner.ValidateRequiredProductsQuantityAsync(cartItem, cart, warnings);
}
