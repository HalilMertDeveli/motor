using Microsoft.AspNetCore.Mvc;
using MotorShop.Legal.Configuration;
using MotorShop.Legal.Filters;
using MotorShop.Legal.Services;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Content.Menus;
using Smartstore.Web.Components;

namespace MotorShop.Legal.Components;

public class LegalCheckoutModel
{
    public List<RenderedLegalDocument> Documents { get; set; } = [];
    public bool RequireAcknowledgment { get; set; }
    public string FormFieldName { get; set; }
    public string WithdrawalUrl { get; set; }
    public string PrivacyNoticeUrl { get; set; }
}

/// <summary>
/// Confirm step: pre-contractual information and distance sales agreement rendered with the cart,
/// links to the right of withdrawal and the KVKK privacy notice, and the acknowledgment checkbox.
/// </summary>
public class LegalCheckoutViewComponent : SmartViewComponent
{
    private readonly LegalCheckoutSettings _settings;
    private readonly LegalDocumentService _documentService;
    private readonly IShoppingCartService _shoppingCartService;

    public LegalCheckoutViewComponent(
        LegalCheckoutSettings settings,
        LegalDocumentService documentService,
        IShoppingCartService shoppingCartService)
    {
        _settings = settings;
        _documentService = documentService;
        _shoppingCartService = shoppingCartService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!_settings.Enabled)
        {
            return Empty();
        }

        var cart = await _shoppingCartService.GetCartAsync(Services.WorkContext.CurrentCustomer, ShoppingCartType.ShoppingCart, Services.StoreContext.CurrentStore.Id);
        if (!cart.HasItems)
        {
            return Empty();
        }

        var data = await _documentService.CreateDataAsync(cart);
        var model = new LegalCheckoutModel
        {
            Documents = await _documentService.RenderAllAsync(data),
            RequireAcknowledgment = _settings.RequireAcknowledgment,
            FormFieldName = LegalConfirmFilter.FormFieldName,
            WithdrawalUrl = await GetTopicUrlAsync(_settings.WithdrawalTopic),
            PrivacyNoticeUrl = await GetTopicUrlAsync(_settings.PrivacyNoticeTopic)
        };

        return View(model);
    }

    private async Task<string> GetTopicUrlAsync(string systemName)
    {
        // Only link published topics, an unpublished topic would answer 404.
        var topic = await _documentService.LoadTopicAsync(systemName);
        return topic != null && topic.IsPublished ? await Url.TopicAsync(systemName) : null;
    }
}
