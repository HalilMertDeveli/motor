using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Shipping;

namespace MotorShop.AdminPanel.Models;

/// <summary>
/// Badge colors for order, payment and shipping states, shared by all panel lists.
/// </summary>
public static class PanelBadges
{
    public static string Order(OrderStatus status) => status switch
    {
        OrderStatus.Complete => "mp-badge--green",
        OrderStatus.Processing => "mp-badge--blue",
        OrderStatus.Cancelled => "mp-badge--red",
        _ => "mp-badge--amber"
    };

    public static string Payment(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "mp-badge--green",
        PaymentStatus.Authorized => "mp-badge--blue",
        PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded => "mp-badge--gray",
        PaymentStatus.Voided => "mp-badge--red",
        _ => "mp-badge--amber"
    };

    public static string Shipping(ShippingStatus status) => status switch
    {
        ShippingStatus.Delivered => "mp-badge--green",
        ShippingStatus.Shipped => "mp-badge--blue",
        ShippingStatus.PartiallyShipped => "mp-badge--amber",
        ShippingStatus.NotYetShipped => "mp-badge--amber",
        _ => "mp-badge--gray"
    };
}
