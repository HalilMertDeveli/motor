using Smartstore.Core;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Configuration;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Providers;

namespace MotorShop.Iyzico.Services;

/// <summary>
/// Hides iyzico in checkout if it is not configured or the working currency is not in <see cref="IyzicoSettings.AllowedCurrencies"/>.
/// </summary>
public class IyzicoPaymentFilter : IPaymentMethodFilter
{
    private readonly IWorkContext _workContext;
    private readonly ISettingFactory _settingFactory;
    private readonly Lazy<IIyzicoCredentialProvider> _credentialProvider;

    public IyzicoPaymentFilter(IWorkContext workContext, ISettingFactory settingFactory, Lazy<IIyzicoCredentialProvider> credentialProvider)
    {
        _workContext = workContext;
        _settingFactory = settingFactory;
        _credentialProvider = credentialProvider;
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

        var credentials = await _credentialProvider.Value.GetCredentialsAsync(request.StoreId);
        return !credentials.IsComplete;
    }
}
