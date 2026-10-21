using System.ComponentModel.DataAnnotations;
using Smartstore.Core.Catalog.Discounts;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Common;

namespace MotorShop.AdminPanel.Models;

#region Reports

public class ReportFilter
{
    /// <summary>
    /// 7, 30, 90 or 365 days, or 0 for <see cref="From"/>/<see cref="To"/>.
    /// </summary>
    public int Days { get; set; } = 30;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public void Normalize(DateTime today)
    {
        if (Days is not (0 or 7 or 30 or 90 or 365))
        {
            Days = 30;
        }

        if (Days > 0 || From == null || To == null)
        {
            To = today;
            From = today.AddDays(-(Days > 0 ? Days : 30) + 1);
            Days = Days > 0 ? Days : 30;
        }

        if (From > To)
        {
            (From, To) = (To, From);
        }

        // At most two years per report.
        if ((To.Value - From.Value).TotalDays > 731)
        {
            From = To.Value.AddDays(-731);
        }
    }
}

public class ReportModel
{
    public ReportFilter Filter { get; set; }
    public string CurrencyCode { get; set; }

    public int OrdersPlaced { get; set; }
    public int OrdersCancelled { get; set; }
    public int PaidOrders { get; set; }
    public Money GrossRevenue { get; set; }
    public Money Refunds { get; set; }
    public Money NetRevenue { get; set; }
    public Money ShippingFees { get; set; }
    public Money Taxes { get; set; }
    public Money AverageOrder { get; set; }
    public Money AwaitingPayment { get; set; }
    public int AwaitingPaymentOrders { get; set; }
    public int UnitsSold { get; set; }

    public List<ReportBucket> Revenue { get; set; } = [];
    public bool MonthlyBuckets { get; set; }
    public List<(OrderStatus Status, int Count)> OrdersByStatus { get; set; } = [];
    public List<TopProductRow> TopProducts { get; set; } = [];
    public List<StockAlertRow> StockAlerts { get; set; } = [];
    public int StockAlertCount { get; set; }
}

public class ReportBucket
{
    public DateTime Start { get; set; }
    public decimal Amount { get; set; }
    public Money Money { get; set; }
    public int Orders { get; set; }
}

public class TopProductRow
{
    public int ProductId { get; set; }
    public string Name { get; set; }
    public int Quantity { get; set; }
    public Money Revenue { get; set; }
}

public class StockAlertRow
{
    public int ProductId { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public int StockQuantity { get; set; }
    public int MinStockQuantity { get; set; }
}

#endregion

#region Content

public class TopicRowModel
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string SystemName { get; set; }
    public bool IsPublished { get; set; }
    public bool IsSystemTopic { get; set; }
    public bool RenderAsWidget { get; set; }
    public string Slug { get; set; }
}

public class ContentListModel
{
    public List<TopicRowModel> Topics { get; set; } = [];
    public bool BlogInstalled { get; set; }
    public string BlogConfigureUrl { get; set; }
}

public class TopicEditorModel
{
    public int Id { get; set; }
    public string SystemName { get; set; }

    [Required, StringLength(400)]
    public string Title { get; set; }

    [StringLength(50)]
    public string ShortTitle { get; set; }

    public string Intro { get; set; }
    public string Body { get; set; }
    public bool IsPublished { get; set; }

    [StringLength(400)]
    public string MetaTitle { get; set; }

    [StringLength(4000)]
    public string MetaDescription { get; set; }

    [StringLength(400)]
    public string MetaKeywords { get; set; }

    [StringLength(400)]
    public string SeName { get; set; }

    // Display only.
    public bool IsSystemTopic { get; set; }
    public bool RenderAsWidget { get; set; }
    public string Slug { get; set; }
}

#endregion

#region Coupons

public enum CouponState
{
    Active,
    Scheduled,
    Expired,
    UsedUp
}

public class CouponRowModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DiscountType Type { get; set; }
    public string Value { get; set; }
    public string CouponCode { get; set; }
    public bool RequiresCouponCode { get; set; }
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public CouponState State { get; set; }
    public int Used { get; set; }
    public string Limit { get; set; }
}

public class CouponEditorModel
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; }

    public DiscountType DiscountType { get; set; } = DiscountType.AssignedToOrderTotal;
    public bool UsePercentage { get; set; } = true;

    // Text so Turkish notation can be parsed on the server.
    public string Percentage { get; set; }
    public string Amount { get; set; }

    public bool RequiresCouponCode { get; set; } = true;

    [StringLength(100)]
    public string CouponCode { get; set; }

    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }

    public DiscountLimitationType Limitation { get; set; } = DiscountLimitationType.Unlimited;
    public int LimitationTimes { get; set; } = 1;

    public int[] CategoryIds { get; set; } = [];

    /// <summary>
    /// SKUs, one per line or comma separated, for <see cref="DiscountType.AssignedToSkus"/>.
    /// </summary>
    public string Skus { get; set; }

    // Display only.
    public int Used { get; set; }
    public List<CategoryOption> AllCategories { get; set; } = [];
}

#endregion

#region Settings

public class SettingsPageModel
{
    public string StoreName { get; set; }
    public PanelSettingsForm Panel { get; set; }
    public CompanyForm Company { get; set; }
    public ShippingForm Shipping { get; set; }

    // Read only, managed elsewhere.
    public string PrimaryCurrency { get; set; }
    public List<string> OtherCurrencies { get; set; } = [];
    public bool PricesIncludeTax { get; set; }
    public string TaxDisplay { get; set; }
    public List<(string Name, bool Active)> PaymentMethods { get; set; } = [];
    public string DefaultEmailAccount { get; set; }
    public bool HideAllPrices { get; set; }
    public bool HasStoreOverrides { get; set; }

    public bool CanUpdate { get; set; }
    public bool CanUpdateStore { get; set; }
}

public class PanelSettingsForm
{
    [StringLength(100)]
    public string PanelTitle { get; set; }

    [StringLength(7)]
    public string AccentColor { get; set; }
}

public class CompanyForm
{
    [Required, StringLength(400)]
    public string StoreName { get; set; }

    [StringLength(400)] public string CompanyName { get; set; }
    [StringLength(400)] public string Street { get; set; }
    [StringLength(20)] public string ZipCode { get; set; }
    [StringLength(100)] public string City { get; set; }
    [StringLength(50)] public string TaxNumber { get; set; }
    [StringLength(100)] public string CommercialRegister { get; set; }
    [StringLength(50)] public string Phone { get; set; }
    [StringLength(255), EmailAddress] public string CompanyEmail { get; set; }
    [StringLength(255), EmailAddress] public string ContactEmail { get; set; }
    [StringLength(255), EmailAddress] public string SupportEmail { get; set; }
}

public class ShippingForm
{
    public bool FreeShippingOverXEnabled { get; set; }
    public string FreeShippingOverXValue { get; set; }
    public bool FreeShippingOverXIncludingTax { get; set; }
}

#endregion
