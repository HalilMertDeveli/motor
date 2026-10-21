using Microsoft.EntityFrameworkCore;
using Smartstore.Core;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Threading;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Providers;

namespace MotorShop.Iyzico.Services;

/// <summary>
/// Hides iyzico in checkout if it is not configured, the working currency is not in <see cref="IyzicoSettings.AllowedCurrencies"/>
/// or the cart total is below <see cref="IyzicoSettings.MinOrderTotal"/> for the customer.
/// </summary>
public class IyzicoPaymentFilter : IPaymentMethodFilter
{
    private readonly IWorkContext _workContext;
    private readonly ISettingFactory _settingFactory;
    private readonly Lazy<IIyzicoCredentialProvider> _credentialProvider;
    private readonly Lazy<IOrderCalculationService> _orderCalculationService;
    private readonly Lazy<SmartDbContext> _db;

    public IyzicoPaymentFilter(
        IWorkContext workContext,
        ISettingFactory settingFactory,
        Lazy<IIyzicoCredentialProvider> credentialProvider,
        Lazy<IOrderCalculationService> orderCalculationService,
        Lazy<SmartDbContext> db)
    {
        _workContext = workContext;
        _settingFactory = settingFactory;
        _credentialProvider = credentialProvider;
        _orderCalculationService = orderCalculationService;
        _db = db;
    }

    public async Task<bool> IsExcludedAsync(PaymentFilterRequest request)
    {
        if (!request.PaymentProvider.Metadata.SystemName.EqualsNoCase(IyzicoProvider.SystemName))
        {
            return false;
        }

        var settings = await _settingFactory.LoadSettingsAsync<IyzicoSettings>(request.StoreId);
        if (!IyzicoRequestBuilder.IsCurrencyAllowed(settings, _workContext.WorkingCurrency?.CurrencyCode))
        {
            return true;
        }

        if (await IsBelowMinOrderTotalAsync(settings, request))
        {
            return true;
        }

        var credentials = await _credentialProvider.Value.GetCredentialsAsync(request.StoreId);
        return !credentials.IsComplete;
    }

    private async Task<bool> IsBelowMinOrderTotalAsync(IyzicoSettings settings, PaymentFilterRequest request)
    {
        var cart = request.Cart;
        if (settings.MinOrderTotal <= 0m || cart?.Customer == null || !cart.HasItems)
        {
            return false;
        }

        var customerId = cart.Customer.Id;
        var roleNames = await _db.Value.CustomerRoleMappings
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.CustomerRole.Active)
            .Select(x => x.CustomerRole.SystemName)
            .ToListAsync();

        if (!IyzicoRequestBuilder.OrderMinimumsApply(settings, roleNames))
        {
            return false;
        }

        // The total calculation can load payment methods again: never re-enter (same guard as Smartstore's CartTotalRule).
        var lockKey = "motorshop:iyzico:minordertotal:" + customerId;
        if (AsyncLock.IsLockHeld(lockKey))
        {
            return false;
        }

        using (await AsyncLock.KeyedAsync(lockKey))
        {
            var total = await _orderCalculationService.Value.GetShoppingCartTotalAsync(cart, includePaymentFee: false, cache: false);
            return IyzicoRequestBuilder.IsBelowMinOrderTotal(settings, total.Total?.Amount ?? decimal.Zero, roleNames);
        }
    }
}
