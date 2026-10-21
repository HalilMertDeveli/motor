using Microsoft.AspNetCore.Mvc;
using MotorShop.PaymentTerms.Configuration;
using MotorShop.PaymentTerms.Models;
using MotorShop.PaymentTerms.Services;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Web.Components;

namespace MotorShop.PaymentTerms.Components;

/// <summary>
/// Term selection shown under "Dealer term payment" in the payment step.
/// </summary>
public class DealerTermPaymentInfoViewComponent : SmartViewComponent
{
    private readonly PaymentTermsSettings _settings;
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;

    public DealerTermPaymentInfoViewComponent(PaymentTermsSettings settings, ICheckoutStateAccessor checkoutStateAccessor)
    {
        _settings = settings;
        _checkoutStateAccessor = checkoutStateAccessor;
    }

    public IViewComponentResult Invoke()
    {
        var selected = _checkoutStateAccessor.CheckoutState?.PaymentData?.Get(PaymentTermsDefaults.TermFormKey) as string;
        var terms = DealerTerms.GetTerms(_settings);

        // Preselect cash: the shortest, interest-free term.
        if (DealerTerms.Find(_settings, selected) == null)
        {
            selected = terms.FirstOrDefault().Value;
        }

        var model = new DealerTermPaymentInfoModel
        {
            FormKey = PaymentTermsDefaults.TermFormKey,
            LateFeeMonthlyPercent = _settings.LateFeeMonthlyPercent,
            Terms = terms
                .Select(x => new DealerTermOptionModel
                {
                    Value = x.Value,
                    Label = DealerTerms.GetLabel(x, T),
                    Selected = x.Value.EqualsNoCase(selected)
                })
                .ToList()
        };

        return View(model);
    }
}
