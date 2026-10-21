using MotorShop.PaymentTerms.Components;
using Smartstore.Core.Widgets;

namespace MotorShop.PaymentTerms.Services;

/// <summary>
/// Places the installment / dealer terms box into the core payment step, no view override needed.
/// </summary>
internal class PaymentTermsWidgetSource : IWidgetSource
{
    // Views/Checkout/PaymentMethod.cshtml: inside the form, above the payment method list.
    internal const string PaymentMethodZone = "checkout_payment_method_top";

    public int Order => 0;

    public Task<IEnumerable<Widget>> GetWidgetsAsync(IWidgetZone zone, bool isPublicArea, object model = null)
    {
        if (isPublicArea && zone.Name.EqualsNoCase(PaymentMethodZone))
        {
            return Task.FromResult<IEnumerable<Widget>>([new ComponentWidget<PaymentTermsInfoViewComponent>()]);
        }

        return Task.FromResult<IEnumerable<Widget>>(null);
    }
}
