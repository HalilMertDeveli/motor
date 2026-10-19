using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MotorShop.Legal.Configuration;
using Smartstore;
using Smartstore.Core;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Common;
using Smartstore.Core.Common.Configuration;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Content.Topics;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Stores;
using Smartstore.Engine.Modularity;
using Smartstore.Templating;

namespace MotorShop.Legal.Services;

public class RenderedLegalDocument
{
    public string Key { get; init; }
    public int TopicId { get; init; }
    public string TopicSystemName { get; init; }
    public string Title { get; init; }
    public string Html { get; init; }
    public string TemplateHash { get; init; }
    public string RenderHash { get; init; }
}

/// <summary>
/// Builds the order data for the legal topics and renders them with Smartstore's Liquid template engine.
/// </summary>
public class LegalDocumentService
{
    private readonly SmartDbContext _db;
    private readonly ITemplateEngine _templateEngine;
    private readonly IStoreContext _storeContext;
    private readonly IWorkContext _workContext;
    private readonly IOrderCalculationService _orderCalculationService;
    private readonly ICurrencyService _currencyService;
    private readonly IAddressService _addressService;
    private readonly IPaymentService _paymentService;
    private readonly ModuleManager _moduleManager;
    private readonly CompanyInformationSettings _companySettings;
    private readonly ContactDataSettings _contactSettings;
    private readonly LegalCheckoutSettings _settings;

    public LegalDocumentService(
        SmartDbContext db,
        ITemplateEngine templateEngine,
        IStoreContext storeContext,
        IWorkContext workContext,
        IOrderCalculationService orderCalculationService,
        ICurrencyService currencyService,
        IAddressService addressService,
        IPaymentService paymentService,
        ModuleManager moduleManager,
        CompanyInformationSettings companySettings,
        ContactDataSettings contactSettings,
        LegalCheckoutSettings settings)
    {
        _db = db;
        _templateEngine = templateEngine;
        _storeContext = storeContext;
        _workContext = workContext;
        _orderCalculationService = orderCalculationService;
        _currencyService = currencyService;
        _addressService = addressService;
        _paymentService = paymentService;
        _moduleManager = moduleManager;
        _companySettings = companySettings;
        _contactSettings = contactSettings;
        _settings = settings;
    }

    public string GetTopicSystemName(string documentKey) => documentKey switch
    {
        LegalDocumentKeys.PreInformation => _settings.PreInformationTopic,
        LegalDocumentKeys.DistanceSales => _settings.DistanceSalesTopic,
        _ => throw new ArgumentOutOfRangeException(nameof(documentKey), documentKey, null)
    };

    /// <summary>
    /// Renders all contract documents (pre-contractual information and distance sales agreement).
    /// Documents whose topic does not exist are skipped.
    /// </summary>
    public async Task<List<RenderedLegalDocument>> RenderAllAsync(LegalDocumentData data)
    {
        var result = new List<RenderedLegalDocument>();

        foreach (var key in LegalDocumentKeys.All)
        {
            var doc = await RenderAsync(key, data);
            if (doc != null)
            {
                result.Add(doc);
            }
        }

        return result;
    }

    public async Task<RenderedLegalDocument> RenderAsync(string documentKey, LegalDocumentData data)
    {
        Guard.NotNull(data);

        var systemName = GetTopicSystemName(documentKey);
        var topic = await LoadTopicAsync(systemName);
        if (topic == null)
        {
            return null;
        }

        var title = topic.GetLocalized(x => x.Title).Value.NullEmpty() ?? systemName;
        var body = topic.GetLocalized(x => x.Body).Value ?? string.Empty;
        var culture = GetCulture();
        var html = await _templateEngine.RenderAsync(body, data.ToLiquidModel(culture), culture);

        return new RenderedLegalDocument
        {
            Key = documentKey,
            TopicId = topic.Id,
            TopicSystemName = systemName,
            Title = title,
            Html = html,
            TemplateHash = LegalAcknowledgment.ComputeHash(title + "\n" + body),
            RenderHash = LegalAcknowledgment.ComputeHash(html)
        };
    }

    public Task<Topic> LoadTopicAsync(string systemName)
    {
        if (systemName.IsEmpty())
        {
            return Task.FromResult<Topic>(null);
        }

        return _db.Topics
            .AsNoTracking()
            .Where(x => x.SystemName == systemName)
            .ApplyStoreFilter(_storeContext.CurrentStore.Id)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Data for the confirm step. Amounts are converted to the working currency like on the confirm page.
    /// </summary>
    public async Task<LegalDocumentData> CreateDataAsync(ShoppingCart cart)
    {
        Guard.NotNull(cart);

        var customer = cart.Customer;
        var data = await CreateSellerDataAsync();

        data.Date = DateTime.Now;
        data.BuyerEmail = customer.BillingAddress?.Email.NullEmpty() ?? customer.Email;
        data.BuyerPhone = customer.BillingAddress?.PhoneNumber;
        data.BuyerName = customer.BillingAddress?.GetFullName(false).NullEmpty() ?? customer.GetFullName();
        data.BillingAddress = await FormatAddressAsync(customer.BillingAddress);
        data.ShippingAddress = cart.IsShippingRequired ? await FormatAddressAsync(customer.ShippingAddress) : null;
        data.ShippingMethod = cart.IsShippingRequired ? customer.GenericAttributes.SelectedShippingOption?.Name : null;
        data.PaymentMethod = await GetPaymentMethodNameAsync(customer.GenericAttributes.SelectedPaymentMethod);

        var subtotal = await _orderCalculationService.GetShoppingCartSubtotalAsync(cart);
        var shipping = await _orderCalculationService.GetShoppingCartShippingTotalAsync(cart);
        var tax = await _orderCalculationService.GetShoppingCartTaxTotalAsync(cart);
        var total = await _orderCalculationService.GetShoppingCartTotalAsync(cart);

        data.SubTotal = _currencyService.ConvertToWorkingCurrency(subtotal.SubtotalWithDiscount).ToString();
        data.Shipping = shipping.ShippingTotal.HasValue ? _currencyService.ConvertToWorkingCurrency(shipping.ShippingTotal.Value).ToString() : null;
        data.Tax = _currencyService.ConvertToWorkingCurrency(tax.Price).ToString();
        data.Total = total.ConvertedAmount?.Total?.ToString();

        foreach (var lineItem in subtotal.LineItems ?? [])
        {
            var item = lineItem.Item.Item;
            data.Items.Add(new LegalDocumentItem
            {
                Name = item.Product.GetLocalized(x => x.Name),
                Sku = item.Product.Sku,
                Quantity = item.Quantity,
                UnitPrice = _currencyService.ConvertToWorkingCurrency(lineItem.UnitPrice.FinalPrice).ToString(),
                Total = _currencyService.ConvertToWorkingCurrency(lineItem.Subtotal.FinalPrice).ToString()
            });
        }

        return data;
    }

    /// <summary>
    /// Data of a placed order. Amounts are shown in the customer currency of the order.
    /// </summary>
    public async Task<LegalDocumentData> CreateDataAsync(Order order)
    {
        Guard.NotNull(order);

        await _db.LoadReferenceAsync(order, x => x.BillingAddress);
        await _db.LoadReferenceAsync(order, x => x.ShippingAddress);
        await _db.LoadReferenceAsync(order, x => x.Customer);

        var data = await CreateSellerDataAsync();
        var inclTax = order.CustomerTaxDisplayType == Smartstore.Core.Checkout.Tax.TaxDisplayType.IncludingTax;

        data.OrderNumber = order.GetOrderNumber();
        data.Date = order.CreatedOnUtc.ToLocalTime();
        data.BuyerEmail = order.BillingAddress?.Email.NullEmpty() ?? order.Customer?.Email;
        data.BuyerPhone = order.BillingAddress?.PhoneNumber;
        data.BuyerName = order.BillingAddress?.GetFullName(false);
        data.BillingAddress = await FormatAddressAsync(order.BillingAddress);
        data.ShippingAddress = await FormatAddressAsync(order.ShippingAddress);
        data.ShippingMethod = order.ShippingMethod;
        data.PaymentMethod = await GetPaymentMethodNameAsync(order.PaymentMethodSystemName);
        data.SubTotal = FormatOrderAmount(inclTax ? order.OrderSubtotalInclTax : order.OrderSubtotalExclTax, order);
        data.Shipping = FormatOrderAmount(inclTax ? order.OrderShippingInclTax : order.OrderShippingExclTax, order);
        data.Tax = FormatOrderAmount(order.OrderTax, order);
        data.Total = FormatOrderAmount(order.OrderTotal, order);

        var orderItems = await _db.OrderItems
            .AsNoTracking()
            .Include(x => x.Product)
            .Where(x => x.OrderId == order.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();

        foreach (var item in orderItems)
        {
            data.Items.Add(new LegalDocumentItem
            {
                Name = item.Product?.GetLocalized(x => x.Name).Value,
                Sku = item.Product?.Sku,
                Quantity = item.Quantity,
                UnitPrice = FormatOrderAmount(inclTax ? item.UnitPriceInclTax : item.UnitPriceExclTax, order),
                Total = FormatOrderAmount(inclTax ? item.PriceInclTax : item.PriceExclTax, order)
            });
        }

        return data;
    }

    private async Task<LegalDocumentData> CreateSellerDataAsync()
    {
        var store = _storeContext.CurrentStore;

        return new LegalDocumentData
        {
            SellerName = _companySettings.CompanyName.NullEmpty() ?? store.Name,
            SellerAddress = Flatten(await _addressService.FormatAddressAsync(_companySettings)),
            SellerPhone = _contactSettings.CompanyTelephoneNumber,
            SellerEmail = _contactSettings.CompanyEmailAddress.NullEmpty() ?? _contactSettings.ContactEmailAddress,
            SellerTaxNumber = _companySettings.TaxNumber,
            SellerVatId = _companySettings.VatId,
            SellerCommercialRegister = _companySettings.CommercialRegister,
            SellerUrl = store.Url
        };
    }

    private async Task<string> FormatAddressAsync(Address address)
        => address == null ? null : Flatten(await _addressService.FormatAddressAsync(address));

    private string FormatOrderAmount(decimal amount, Order order)
        => _currencyService.CreateMoney(amount * order.CurrencyRate, order.CustomerCurrencyCode).ToString();

    private async Task<string> GetPaymentMethodNameAsync(string systemName)
    {
        if (systemName.IsEmpty())
        {
            return null;
        }

        var provider = await _paymentService.LoadPaymentProviderBySystemNameAsync(systemName);
        return provider != null
            ? _moduleManager.GetLocalizedFriendlyName(provider.Metadata).NullEmpty() ?? systemName
            : systemName;
    }

    private CultureInfo GetCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(_workContext.WorkingLanguage.LanguageCulture);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.CurrentCulture;
        }
    }

    /// <summary>
    /// Formatted addresses are multi-line plain text. The documents show them on one line.
    /// </summary>
    internal static string Flatten(string text)
    {
        if (text.IsEmpty())
        {
            return null;
        }

        var lines = text
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(", ", lines);
    }
}
