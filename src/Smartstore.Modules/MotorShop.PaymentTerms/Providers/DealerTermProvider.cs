using Microsoft.AspNetCore.Http;
using MotorShop.PaymentTerms.Components;
using MotorShop.PaymentTerms.Configuration;
using MotorShop.PaymentTerms.Services;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Common;
using Smartstore.Core.Configuration;
using Smartstore.Core.Data;
using Smartstore.Core.Stores;
using Smartstore.Core.Widgets;
using Smartstore.Engine.Modularity;

namespace MotorShop.PaymentTerms.Providers;

/// <summary>
/// Dealer term payment ("Vadeli ödeme"): the dealer chooses cash within 10 days or 30/60/90 days.
/// The order is placed with payment status Pending; the store collects the payment later (invoice, transfer, cheque).
/// Only offered to customers in the dealer role (see <see cref="DealerTermPaymentFilter"/>).
/// </summary>
[SystemName(PaymentTermsDefaults.DealerTermSystemName)]
[FriendlyName("Dealer term payment")]
[Order(5)]
[PaymentMethod(PaymentMethodType.Standard)]
public class DealerTermProvider : PaymentMethodBase
{
    private readonly ISettingFactory _settingFactory;
    private readonly IStoreContext _storeContext;
    private readonly ICheckoutStateAccessor _checkoutStateAccessor;
    private readonly SmartDbContext _db;

    public DealerTermProvider(
        ISettingFactory settingFactory,
        IStoreContext storeContext,
        ICheckoutStateAccessor checkoutStateAccessor,
        SmartDbContext db)
    {
        _settingFactory = settingFactory;
        _storeContext = storeContext;
        _checkoutStateAccessor = checkoutStateAccessor;
        _db = db;
    }

    public override bool RequiresInteraction => true;

    public override bool RequiresPaymentSelection => false;

    public override Widget GetPaymentInfoWidget()
        => new ComponentWidget<DealerTermPaymentInfoViewComponent>();

    public override async Task<PaymentValidationResult> ValidatePaymentDataAsync(IFormCollection form)
    {
        var settings = await LoadSettingsAsync();
        var term = DealerTerms.Find(settings, form[PaymentTermsDefaults.TermFormKey]);

        return term == null
            ? new PaymentValidationResult(new PaymentValidationFailure(PaymentTermsDefaults.TermFormKey, T("Plugins.MotorShop.PaymentTerms.TermRequired")))
            : new PaymentValidationResult();
    }

    public override Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        => Task.FromResult(new ProcessPaymentRequest());

    public override async Task<string> GetPaymentSummaryAsync()
    {
        var term = await GetSelectedTermAsync();
        return term == null ? string.Empty : DealerTerms.GetLabel(term.Value, T);
    }

    public override Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        => Task.FromResult(new ProcessPaymentResult { NewPaymentStatus = PaymentStatus.Pending });

    public override async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
    {
        var order = postProcessPaymentRequest.Order;
        var term = await GetSelectedTermAsync();
        if (order == null || term == null)
        {
            return;
        }

        var settings = await LoadSettingsAsync(order.StoreId);

        // Visible to the dealer in the order details: the agreed plan and the catalog's late fee rule.
        _db.OrderNotes.Add(order, T("Plugins.MotorShop.PaymentTerms.OrderNote", DealerTerms.GetLabel(term.Value, T), settings.LateFeeMonthlyPercent.ToString("0.##")), true);
        order.GenericAttributes.Set(PaymentTermsDefaults.OrderTermDaysAttribute, term.Value.Days);

        await _db.SaveChangesAsync();
    }

    private async Task<DealerTerm?> GetSelectedTermAsync()
    {
        // Saved by Smartstore from the payment form (PaymentMethodHandler) and kept until the order is placed.
        var value = _checkoutStateAccessor.CheckoutState?.PaymentData?.Get(PaymentTermsDefaults.TermFormKey) as string;
        return DealerTerms.Find(await LoadSettingsAsync(), value);
    }

    private Task<PaymentTermsSettings> LoadSettingsAsync(int? storeId = null)
        => _settingFactory.LoadSettingsAsync<PaymentTermsSettings>(storeId ?? _storeContext.CurrentStore.Id);
}
