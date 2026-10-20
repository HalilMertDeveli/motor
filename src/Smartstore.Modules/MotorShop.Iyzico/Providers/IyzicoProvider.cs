using Smartstore.Core;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Configuration;
using Smartstore.Core.Stores;
using Smartstore.Core.Widgets;
using Smartstore.Engine.Modularity;
using Smartstore.Http;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Services;

namespace MotorShop.Iyzico.Providers;

/// <summary>
/// iyzico Checkout Form as a redirection payment method.
/// Flow: the order is placed with payment status Pending, the customer is redirected to the hosted iyzico form,
/// and the order is marked as paid only after the server-side retrieve was verified (callback, webhook or reconciliation task).
/// </summary>
[SystemName("Payments.Iyzico")]
[FriendlyName("iyzico")]
[Order(1)]
[PaymentMethod(PaymentMethodType.Redirection)]
public class IyzicoProvider : PaymentMethodBase, IConfigurable
{
    public const string SystemName = "Payments.Iyzico";

    private readonly IStoreContext _storeContext;
    private readonly ISettingFactory _settingFactory;
    private readonly IWorkContext _workContext;
    private readonly IIyzicoCredentialProvider _credentialProvider;
    private readonly IyzicoPaymentService _paymentService;

    public IyzicoProvider(
        IStoreContext storeContext,
        ISettingFactory settingFactory,
        IWorkContext workContext,
        IIyzicoCredentialProvider credentialProvider,
        IyzicoPaymentService paymentService)
    {
        _storeContext = storeContext;
        _settingFactory = settingFactory;
        _workContext = workContext;
        _credentialProvider = credentialProvider;
        _paymentService = paymentService;
    }

    public override bool SupportRefund => true;

    public override bool SupportPartiallyRefund => true;

    /// <summary>
    /// Void applies to payments held by the fraud check (order payment status Authorized).
    /// </summary>
    public override bool SupportVoid => true;

    public RouteInfo GetConfigurationRoute()
        => new("Configure", "IyzicoAdmin", new { area = "Admin" });

    public override Widget GetPaymentInfoWidget()
        => null;

    public override async Task<(decimal FixedFeeOrPercentage, bool UsePercentage)> GetPaymentFeeInfoAsync(ShoppingCart cart)
    {
        var settings = await _settingFactory.LoadSettingsAsync<IyzicoSettings>(_storeContext.CurrentStore.Id);
        return (settings.AdditionalFee, settings.AdditionalFeePercentage);
    }

    public override async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest request)
    {
        // Nothing is charged here. The payment happens on the hosted form after the order was placed.
        var currencyCode = _workContext.WorkingCurrency?.CurrencyCode;
        var settings = await _settingFactory.LoadSettingsAsync<IyzicoSettings>(request.StoreId);
        if (!IyzicoRequestBuilder.IsCurrencyAllowed(settings, currencyCode))
        {
            throw new PaymentException(T("Plugins.MotorShop.Iyzico.UnsupportedCurrency", currencyCode));
        }

        var credentials = await _credentialProvider.GetCredentialsAsync(request.StoreId);
        if (!credentials.IsComplete)
        {
            throw new PaymentException(T("Plugins.MotorShop.Iyzico.NotConfigured"));
        }

        return new ProcessPaymentResult
        {
            NewPaymentStatus = PaymentStatus.Pending
        };
    }

    public override Task PostProcessPaymentAsync(PostProcessPaymentRequest request)
    {
        var order = request.Order;
        var store = _storeContext.GetStoreById(order.StoreId) ?? _storeContext.CurrentStore;

        // Our own page initializes the checkout form, so a failed initialization can be retried without a new order.
        request.RedirectUrl = store.GetAbsoluteUrl($"iyzico/pay/{order.OrderGuid}");

        return Task.CompletedTask;
    }

    public override Task<bool> CanRePostProcessPaymentAsync(Order order)
        => Task.FromResult(IyzicoPaymentService.IsPayable(order));

    public override async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest request)
    {
        var fullyRefunded = await _paymentService.RefundAsync(request.Order, request.AmountToRefund.Amount, request.IsPartialRefund);

        return new RefundPaymentResult
        {
            NewPaymentStatus = fullyRefunded || !request.IsPartialRefund ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded
        };
    }

    public override async Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest request)
    {
        await _paymentService.VoidAsync(request.Order);

        return new VoidPaymentResult
        {
            NewPaymentStatus = PaymentStatus.Voided
        };
    }
}
