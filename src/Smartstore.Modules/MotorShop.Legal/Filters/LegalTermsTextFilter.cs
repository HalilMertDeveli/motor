using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MotorShop.Legal.Configuration;
using Smartstore.Web.Models.Checkout;

namespace MotorShop.Legal.Filters;

/// <summary>
/// Replaces Smartstore's confirm text "I agree with the terms of service ... my data ... can be stored electronically"
/// (resource <c>Checkout.TermsOfService.IAccept</c>, shown next to both buy buttons) with a plain notice that confirming
/// the order creates a payment obligation. Acceptance itself is the MOTOR acknowledgment checkbox; the KVKK notice is
/// information, not consent, so it must not be bundled into the buy button text.
/// Smartstore 6.4 has no setting to switch the core text off (it is only hidden in terminal checkout).
/// </summary>
public class LegalTermsTextFilter : IResultFilter
{
    private readonly LegalCheckoutSettings _settings;
    private readonly ILocalizationService _localizationService;

    public LegalTermsTextFilter(LegalCheckoutSettings settings, ILocalizationService localizationService)
    {
        _settings = settings;
        _localizationService = localizationService;
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (_settings.Enabled && _settings.ReplaceCoreTermsText)
        {
            ApplyTermsText(context.Result, _localizationService.GetResource("Plugins.MotorShop.Legal.PaymentObligationHint"));
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    /// <returns><c>true</c> if the result was the confirm view and its text was replaced.</returns>
    internal static bool ApplyTermsText(IActionResult result, string text)
    {
        // Terminal checkout has no terms text: leave it empty there.
        if (result is ViewResult { Model: CheckoutConfirmModel model } && model.TermsOfService.HasValue())
        {
            model.TermsOfService = text;
            return true;
        }

        return false;
    }
}
