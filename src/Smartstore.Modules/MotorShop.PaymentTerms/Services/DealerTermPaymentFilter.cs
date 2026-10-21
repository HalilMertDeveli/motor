using Microsoft.EntityFrameworkCore;
using MotorShop.PaymentTerms.Configuration;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;

namespace MotorShop.PaymentTerms.Services;

/// <summary>
/// Offers dealer term payment only to customers in the dealer role.
/// </summary>
public class DealerTermPaymentFilter : IPaymentMethodFilter
{
    private readonly ISettingFactory _settingFactory;
    private readonly Lazy<SmartDbContext> _db;

    public DealerTermPaymentFilter(ISettingFactory settingFactory, Lazy<SmartDbContext> db)
    {
        _settingFactory = settingFactory;
        _db = db;
    }

    public async Task<bool> IsExcludedAsync(PaymentFilterRequest request)
    {
        if (!request.PaymentProvider.Metadata.SystemName.EqualsNoCase(PaymentTermsDefaults.DealerTermSystemName))
        {
            return false;
        }

        var customer = request.Cart?.Customer;
        if (customer == null)
        {
            return true;
        }

        var settings = await _settingFactory.LoadSettingsAsync<PaymentTermsSettings>(request.StoreId);
        return !await IsDealerAsync(_db.Value, customer.Id, settings.DealerRoleSystemName);
    }

    public static Task<bool> IsDealerAsync(SmartDbContext db, int customerId, string dealerRoleSystemName)
    {
        if (dealerRoleSystemName.IsEmpty())
        {
            return Task.FromResult(false);
        }

        return db.CustomerRoleMappings
            .AsNoTracking()
            .AnyAsync(x => x.CustomerId == customerId && x.CustomerRole.Active && x.CustomerRole.SystemName == dealerRoleSystemName);
    }
}
