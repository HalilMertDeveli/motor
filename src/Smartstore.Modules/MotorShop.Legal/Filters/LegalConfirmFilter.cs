using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MotorShop.Legal.Configuration;
using MotorShop.Legal.Services;
using Smartstore.Core;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Logging;
using Smartstore.Core.Stores;

namespace MotorShop.Legal.Filters;

/// <summary>
/// Runs on the confirm POST (<c>CheckoutController.ConfirmOrder</c>) and on the AJAX payment confirmation
/// (<c>CheckoutController.ConfirmPayment</c>, used by redirect payment methods before the order exists).
/// Enforces the acknowledgment server-side and keeps the record in the checkout state until the order is placed.
/// </summary>
public class LegalConfirmFilter : IAsyncActionFilter
{
    public const string FormFieldName = "MotorLegalAccepted";

    private readonly LegalCheckoutSettings _settings;
    private readonly IWorkContext _workContext;
    private readonly IStoreContext _storeContext;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;
    private readonly LegalDocumentService _documentService;
    private readonly INotifier _notifier;
    private readonly ILocalizationService _localizationService;

    public LegalConfirmFilter(
        LegalCheckoutSettings settings,
        IWorkContext workContext,
        IStoreContext storeContext,
        IShoppingCartService shoppingCartService,
        ICheckoutStateAccessor checkoutStateAccessor,
        LegalDocumentService documentService,
        INotifier notifier,
        ILocalizationService localizationService)
    {
        _settings = settings;
        _workContext = workContext;
        _storeContext = storeContext;
        _shoppingCartService = shoppingCartService;
        _checkoutStateAccessor = checkoutStateAccessor;
        _documentService = documentService;
        _notifier = notifier;
        _localizationService = localizationService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!_settings.Enabled)
        {
            await next();
            return;
        }

        var request = context.HttpContext.Request;
        var accepted = request.HasFormContentType && IsAccepted(request.Form[FormFieldName]);

        if (!IsAcknowledgmentSufficient(_settings.RequireAcknowledgment, accepted))
        {
            // Filters are created by ActivatorUtilities: no Localizer property injection.
            var message = _localizationService.GetResource("Plugins.MotorShop.Legal.PleaseAccept");
            var isAjax = request.Headers.XRequestedWith == "XMLHttpRequest";

            if (isAjax)
            {
                // Same shape as CheckoutController.ConfirmPayment, the confirm script shows the messages.
                context.Result = new JsonResult(new { success = false, redirectUrl = (string)null, messages = new[] { message } });
            }
            else
            {
                _notifier.Error(message);
                context.Result = new RedirectToActionResult("Confirm", "Checkout", null);
            }

            return;
        }

        var cart = await _shoppingCartService.GetCartAsync(_workContext.CurrentCustomer, ShoppingCartType.ShoppingCart, _storeContext.CurrentStore.Id);
        if (cart.HasItems)
        {
            var data = await _documentService.CreateDataAsync(cart);
            var documents = await _documentService.RenderAllAsync(data);
            var ack = LegalAcknowledgment.Create(documents, accepted, _workContext.WorkingLanguage.LanguageCulture, DateTime.UtcNow);

            var state = _checkoutStateAccessor.CheckoutState;
            if (state != null)
            {
                state.CustomProperties[LegalAcknowledgment.CheckoutStateKey] = ack.Serialize();
            }
        }

        await next();
    }

    internal static bool IsAccepted(string[] values)
        => values != null && values.Any(x => string.Equals(x, "true", StringComparison.OrdinalIgnoreCase) || x == "on");

    internal static bool IsAcknowledgmentSufficient(bool required, bool accepted)
        => !required || accepted;
}
