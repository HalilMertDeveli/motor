using MotorShop.AdminPanel.Services;
using NUnit.Framework;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Shipping;

namespace MotorShop.AdminPanel.Tests;

[TestFixture]
public class OrderRulesTests
{
    private static Order Order(OrderStatus status, PaymentStatus payment, ShippingStatus shipping) => new()
    {
        OrderStatus = status,
        PaymentStatus = payment,
        ShippingStatus = shipping
    };

    [TestCase(PaymentStatus.Pending, true)]
    [TestCase(PaymentStatus.Voided, true)]
    [TestCase(PaymentStatus.Refunded, true)]
    [TestCase(PaymentStatus.Paid, false)]
    [TestCase(PaymentStatus.PartiallyRefunded, false)]
    [TestCase(PaymentStatus.Authorized, false)]
    public void Panel_cancels_only_orders_without_collected_money(PaymentStatus payment, bool allowed)
    {
        var (canCancel, reason) = OrderPanelService.CancelState(Order(OrderStatus.Pending, payment, ShippingStatus.NotYetShipped));

        Assert.That(canCancel, Is.EqualTo(allowed));
        Assert.That(reason, allowed ? Is.Null : Is.EqualTo("Order.CancelPaidBlocked"));
    }

    [Test]
    public void Cancelled_order_cannot_be_cancelled_again()
    {
        var (canCancel, reason) = OrderPanelService.CancelState(Order(OrderStatus.Cancelled, PaymentStatus.Pending, ShippingStatus.NotYetShipped));

        Assert.That(canCancel, Is.False);
        Assert.That(reason, Is.Null);
    }

    [TestCase(PaymentStatus.Paid, ShippingStatus.Shipped, true)]
    [TestCase(PaymentStatus.Paid, ShippingStatus.Delivered, true)]
    [TestCase(PaymentStatus.Paid, ShippingStatus.ShippingNotRequired, true)]
    [TestCase(PaymentStatus.Paid, ShippingStatus.NotYetShipped, false)]
    [TestCase(PaymentStatus.Paid, ShippingStatus.PartiallyShipped, false)]
    [TestCase(PaymentStatus.Pending, ShippingStatus.Delivered, false)]
    [TestCase(PaymentStatus.Authorized, ShippingStatus.Delivered, false)]
    public void Complete_needs_payment_and_shipment(PaymentStatus payment, ShippingStatus shipping, bool allowed)
        => Assert.That(OrderPanelService.CompleteAllowed(Order(OrderStatus.Processing, payment, shipping)), Is.EqualTo(allowed));

    [Test]
    public void Completed_order_cannot_be_completed_again()
        => Assert.That(OrderPanelService.CompleteAllowed(Order(OrderStatus.Complete, PaymentStatus.Paid, ShippingStatus.Delivered)), Is.False);

    [TestCase("https://kargo.example/takip?no=1", true)]
    [TestCase("http://kargo.example/takip", true)]
    [TestCase("javascript:alert(1)", false)]
    [TestCase("data:text/html,x", false)]
    [TestCase("/relative", false)]
    [TestCase(null, false)]
    public void Only_http_tracking_links_are_rendered(string url, bool rendered)
        => Assert.That(OrderPanelService.SafeUrl(url) != null, Is.EqualTo(rendered));
}
