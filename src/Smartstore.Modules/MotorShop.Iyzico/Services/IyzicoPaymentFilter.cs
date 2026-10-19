using Smartstore.Core;
using Smartstore.Core.Checkout.Payment;
using MotorShop.Iyzico.Providers;

namespace MotorShop.Iyzico.Services;

/// <summary>
/// Hides iyzico in checkout if it is not configured or the working currency is not supported.
/// </summary>
public class IyzicoPaymentFilter : IPaymentMethodFilter
{
    private readonly IWorkContext _workContext;
    private readonly Lazy<IIyzicoCredentialProvider> _credentialProvider;

    public IyzicoPaymentFilter(IWorkContext workContext, Lazy<IIyzicoCredentialProvider> credentialProvider)
    {
        _workContext = workContext;
        _credentialProvider = credentialProvider;
    }

    public async Task<bool> IsExcludedAsync(PaymentFilterRequest request)
    {
        if (!request.PaymentProvider.Metadata.SystemName.EqualsNoCase(IyzicoProvider.SystemName))
        {
            return false;
        }

        if (!IyzicoRequestBuilder.SupportedCurrencies.Contains(_workContext.WorkingCurrency?.CurrencyCode ?? string.Empty))
        {
            return true;
        }

        var credentials = await _credentialProvider.Value.GetCredentialsAsync(request.StoreId);
        return !credentials.IsComplete;
    }
}
