using Microsoft.AspNetCore.Routing;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Shipping;
using Smartstore.Core.Common;

namespace MotorShop.AdminPanel.Models;

public class OrderFilter
{
    public const int DefaultPageSize = 25;

    public string Q { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public OrderStatus? OrderStatus { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public ShippingStatus? ShippingStatus { get; set; }
    public int Page { get; set; } = 1;
    public int Size { get; set; } = DefaultPageSize;

    public void Normalize()
    {
        Q = Q?.Trim();
        if (Q?.Length > 200)
        {
            Q = Q[..200];
        }

        Page = Math.Max(Page, 1);
        Size = ProductFilter.PageSizes.Contains(Size) ? Size : DefaultPageSize;
    }

    public RouteValueDictionary ToRouteValues(int? page = null)
    {
        var values = new RouteValueDictionary { ["area"] = "Admin" };
        var p = page ?? Page;

        if (Q.HasValue()) values["q"] = Q;
        if (From.HasValue) values["from"] = From.Value.ToString("yyyy-MM-dd");
        if (To.HasValue) values["to"] = To.Value.ToString("yyyy-MM-dd");
        if (OrderStatus.HasValue) values["orderStatus"] = OrderStatus.Value.ToString();
        if (PaymentStatus.HasValue) values["paymentStatus"] = PaymentStatus.Value.ToString();
        if (ShippingStatus.HasValue) values["shippingStatus"] = ShippingStatus.Value.ToString();
        if (Size != DefaultPageSize) values["size"] = Size;
        if (p > 1) values["page"] = p;

        return values;
    }
}

public class OrderListModel
{
    public OrderFilter Filter { get; set; }
    public List<OrderRowModel> Rows { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Filter.Size);
}

public class OrderRowModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; }
    public DateTime CreatedOn { get; set; }
    public string Customer { get; set; }
    public string Email { get; set; }
    public string City { get; set; }
    public Money Total { get; set; }
    public string PaymentMethod { get; set; }
    public OrderStatus OrderStatus { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public ShippingStatus ShippingStatus { get; set; }
}

public class OrderDetailModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? PaidOn { get; set; }
    public OrderStatus OrderStatus { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public ShippingStatus ShippingStatus { get; set; }
    public string PaymentMethod { get; set; }
    public string PaymentMethodSystemName { get; set; }
    public bool IsIyzico { get; set; }
    public string ShippingMethod { get; set; }
    public string CustomerComment { get; set; }

    public int CustomerId { get; set; }
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public bool CustomerIsGuest { get; set; }
    public AddressModel BillingAddress { get; set; }
    public AddressModel ShippingAddress { get; set; }

    public List<OrderItemModel> Items { get; set; } = [];

    public Money Subtotal { get; set; }
    public Money SubtotalDiscount { get; set; }
    public Money Shipping { get; set; }
    public Money PaymentFee { get; set; }
    public Money Tax { get; set; }
    public List<(string Rate, Money Amount)> TaxRates { get; set; } = [];
    public Money Discount { get; set; }
    public Money CreditBalance { get; set; }
    public Money Rounding { get; set; }
    public Money Total { get; set; }
    public Money Refunded { get; set; }
    public bool HasRefund { get; set; }
    public List<string> AppliedDiscounts { get; set; } = [];

    public List<OrderNoteModel> Notes { get; set; } = [];
    public List<ShipmentModel> Shipments { get; set; } = [];

    public bool CanMarkPaid { get; set; }
    public bool CanCancel { get; set; }
    public string CancelBlockedReason { get; set; }
    public bool CanComplete { get; set; }
    public bool CanAddShipment { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanEditShipments { get; set; }
    public string PdfUrl { get; set; }
}

public class AddressModel
{
    public string Name { get; set; }
    public string Company { get; set; }
    public string Lines { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
}

public class OrderItemModel
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public string AttributeDescription { get; set; }
    public int Quantity { get; set; }
    public int Shipped { get; set; }
    public Money UnitPrice { get; set; }
    public Money Total { get; set; }
    public decimal TaxRate { get; set; }
}

public class OrderNoteModel
{
    public DateTime CreatedOn { get; set; }
    public string Note { get; set; }
    public bool DisplayToCustomer { get; set; }
}

public class ShipmentModel
{
    public int Id { get; set; }
    public string Carrier { get; set; }
    public string TrackingNumber { get; set; }
    public string TrackingUrl { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? ShippedOn { get; set; }
    public DateTime? DeliveredOn { get; set; }
    public List<(string Name, int Quantity)> Items { get; set; } = [];
    public bool CanShip => !ShippedOn.HasValue;
    public bool CanDeliver => ShippedOn.HasValue && !DeliveredOn.HasValue;
}

public class ShipmentFormModel
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public List<string> Carriers { get; set; } = [];
    public List<ShipmentFormItem> Items { get; set; } = [];
}

public class ShipmentFormItem
{
    public int OrderItemId { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public int Ordered { get; set; }
    public int AlreadyShipped { get; set; }
    public int Remaining { get; set; }
}

public class ShipmentQueueModel
{
    public List<OrderRowModel> AwaitingShipment { get; set; } = [];
    public int AwaitingCount { get; set; }
    public List<OpenShipmentRow> InTransit { get; set; } = [];
}

public class OpenShipmentRow
{
    public int ShipmentId { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public string Customer { get; set; }
    public string Carrier { get; set; }
    public string TrackingNumber { get; set; }
    public string TrackingUrl { get; set; }
    public DateTime ShippedOn { get; set; }
}

public class CustomerFilter
{
    public string Q { get; set; }

    /// <summary>
    /// "registered" (default), "guests" or "all".
    /// </summary>
    public string Role { get; set; }
    public int Page { get; set; } = 1;
    public int Size { get; set; } = OrderFilter.DefaultPageSize;

    public void Normalize()
    {
        Q = Q?.Trim();
        if (Q?.Length > 200)
        {
            Q = Q[..200];
        }

        Role = Role is "guests" or "all" ? Role : "registered";
        Page = Math.Max(Page, 1);
        Size = ProductFilter.PageSizes.Contains(Size) ? Size : OrderFilter.DefaultPageSize;
    }

    public RouteValueDictionary ToRouteValues(int? page = null)
    {
        var values = new RouteValueDictionary { ["area"] = "Admin" };
        var p = page ?? Page;
        if (Q.HasValue()) values["q"] = Q;
        if (Role != "registered") values["role"] = Role;
        if (Size != OrderFilter.DefaultPageSize) values["size"] = Size;
        if (p > 1) values["page"] = p;
        return values;
    }
}

public class CustomerListModel
{
    public CustomerFilter Filter { get; set; }
    public List<CustomerRowModel> Rows { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Filter.Size);
}

public class CustomerRowModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string City { get; set; }
    public bool Active { get; set; }
    public bool IsGuest { get; set; }
    public int OrderCount { get; set; }
    public Money TotalSpend { get; set; }
    public DateTime? LastOrderOn { get; set; }
}

public class CustomerDetailModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public bool Active { get; set; }
    public bool IsGuest { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? LastLoginOn { get; set; }
    public List<string> Roles { get; set; } = [];
    public AddressModel BillingAddress { get; set; }
    public AddressModel ShippingAddress { get; set; }
    public int OrderCount { get; set; }
    public Money TotalSpend { get; set; }
    public Money AverageOrder { get; set; }
    public List<OrderRowModel> Orders { get; set; } = [];
}

public class InvoiceListModel
{
    public OrderListModel Orders { get; set; }
    public bool PdfEnabled { get; set; }
}
