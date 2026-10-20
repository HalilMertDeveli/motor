using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MotorShop.PaymentTerms.Configuration;
using MotorShop.PaymentTerms.Models;
using MotorShop.PaymentTerms.Services;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Configuration;
using Smartstore.Core.Content.Menus;
using Smartstore.Core.Data;
using Smartstore.Web.Components;

namespace MotorShop.PaymentTerms.Components;

/// <summary>
/// Payment step info box (zone "checkout_payment_method_top"): the catalog's "3 installments on all credit cards"
/// while card payment is offered, and the dealer terms for dealers.
/// </summary>
public class PaymentTermsInfoViewComponent : SmartViewComponent
{
    private readonly PaymentTermsSettings _settings;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IPaymentService _paymentService;
    private readonly ISettingService _settingService;
    private readonly ICurrencyService _currencyService;
    private readonly IOrderCalculationService _orderCalculationService;
    private readonly SmartDbContext _db;

    public PaymentTermsInfoViewComponent(
        PaymentTermsSettings settings,
        IShoppingCartService shoppingCartService,
        IPaymentService paymentService,
        ISettingService settingService,
        ICurrencyService currencyService,
        IOrderCalculationService orderCalculationService,
        SmartDbContext db)
    {
        _settings = settings;
        _shoppingCartService = shoppingCartService;
        _paymentService = paymentService;
        _settingService = settingService;
        _currencyService = currencyService;
        _orderCalculationService = orderCalculationService;
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var store = Services.StoreContext.CurrentStore;
        var customer = Services.WorkContext.CurrentCustomer;
        var cart = await _shoppingCartService.GetCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        var activeMethods = await _paymentService.LoadActivePaymentProvidersAsync(cart, store.Id, provideFallbackMethod: false);
        var cardOffered = activeMethods.Any(x => x.Metadata.SystemName.EqualsNoCase(PaymentTermsDefaults.IyzicoSystemName));
        var isDealer = await DealerTermPaymentFilter.IsDealerAsync(_db, customer.Id, _settings.DealerRoleSystemName);

        var model = new PaymentTermsInfoModel
        {
            ShowInstallments = _settings.ShowInstallmentInfo && cardOffered,
            IsDealer = isDealer,
            CashTermDays = _settings.CashTermDays,
            DeferredTerms = string.Join("-", DealerTerms.GetTerms(_settings).Where(x => !x.IsCash).Select(x => x.Days.ToString(CultureInfo.InvariantCulture))),
            LateFeeMonthlyPercent = _settings.LateFeeMonthlyPercent,
            PaymentInfoUrl = await Url.TopicAsync("PaymentInfo")
        };

        if (isDealer)
        {
            // Card minimums are iyzico settings (MotorShop.Iyzico); read by key to keep the modules independent.
            var minCard = await _settingService.GetSettingByKeyAsync("IyzicoSettings.MinOrderTotal", 5000m, store.Id, true);
            var minInstallment = await _settingService.GetSettingByKeyAsync("IyzicoSettings.MinInstallmentOrderTotal", 10000m, store.Id, true);
            var currency = _currencyService.PrimaryCurrency;

            model.MinCardTotal = minCard > 0m ? _currencyService.CreateMoney(minCard, currency).ToString() : null;
            model.MinInstallmentTotal = minInstallment > 0m ? _currencyService.CreateMoney(minInstallment, currency).ToString() : null;

            // Below the installment minimum, iyzico offers dealers single payment only: no "3 installments" promise then.
            if (model.ShowInstallments && minInstallment > 0m)
            {
                var total = await _orderCalculationService.GetShoppingCartTotalAsync(cart, includePaymentFee: false, cache: false);
                model.ShowInstallments = (total.Total?.Amount ?? decimal.Zero) >= minInstallment;
            }
        }

        if (!model.ShowInstallments && !model.IsDealer)
        {
            return Empty();
        }

        return View(model);
    }
}
