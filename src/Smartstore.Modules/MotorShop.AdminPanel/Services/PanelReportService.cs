using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Models;
using Smartstore.Core;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Common;
using Smartstore.Core.Data;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// Operational report over the order tables. Revenue counts only orders whose money was collected
/// (paid, partially refunded or refunded); refunds are shown and deducted separately, unpaid and
/// failed orders never count as revenue. Periods use the order date in the admin's time zone.
/// </summary>
public class PanelReportService
{
    internal static readonly int[] RealizedPaymentStates =
    [
        (int)PaymentStatus.Paid,
        (int)PaymentStatus.PartiallyRefunded,
        (int)PaymentStatus.Refunded
    ];

    private readonly SmartDbContext _db;
    private readonly ICommonServices _services;

    public PanelReportService(SmartDbContext db, ICommonServices services)
    {
        _db = db;
        _services = services;
    }

    private Money M(decimal amount)
        => _services.CurrencyService.CreateMoney(amount, _services.CurrencyService.PrimaryCurrency);

    public async Task<ReportModel> GetReportAsync(ReportFilter filter)
    {
        var dt = _services.DateTimeHelper;
        var today = dt.ConvertToUserTime(DateTime.UtcNow, DateTimeKind.Utc).Date;
        filter.Normalize(today);

        var fromUtc = dt.ConvertToUtcTime(filter.From.Value.Date, dt.CurrentTimeZone);
        var toUtc = dt.ConvertToUtcTime(filter.To.Value.Date, dt.CurrentTimeZone).AddDays(1);

        var customer = _services.WorkContext.CurrentCustomer;
        var storeIds = await _services.StoreMappingService.GetAuthorizedStoreIdsAsync("Customer", customer.Id);

        var orders = _db.Orders
            .AsNoTracking()
            .Where(x => !x.Deleted && x.CreatedOnUtc >= fromUtc && x.CreatedOnUtc < toUtc)
            .ApplyCustomerFilter(storeIds);

        var realized = orders.Where(x => RealizedPaymentStates.Contains(x.PaymentStatusId));

        var model = new ReportModel
        {
            Filter = filter,
            CurrencyCode = _services.CurrencyService.PrimaryCurrency.CurrencyCode,
            OrdersPlaced = await orders.CountAsync(),
            OrdersCancelled = await orders.CountAsync(x => x.OrderStatusId == (int)OrderStatus.Cancelled)
        };

        var totals = await realized
            .GroupBy(x => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Gross = g.Sum(x => x.OrderTotal),
                Refunds = g.Sum(x => x.RefundedAmount),
                Shipping = g.Sum(x => x.OrderShippingInclTax),
                Tax = g.Sum(x => x.OrderTax)
            })
            .FirstOrDefaultAsync();

        model.PaidOrders = totals?.Count ?? 0;
        model.GrossRevenue = M(totals?.Gross ?? 0);
        model.Refunds = M(totals?.Refunds ?? 0);
        model.NetRevenue = M((totals?.Gross ?? 0) - (totals?.Refunds ?? 0));
        model.ShippingFees = M(totals?.Shipping ?? 0);
        model.Taxes = M(totals?.Tax ?? 0);
        model.AverageOrder = M(model.PaidOrders > 0 ? Math.Round((totals?.Gross ?? 0) / model.PaidOrders, 2) : 0);

        var awaiting = orders.Where(x => x.PaymentStatusId == (int)PaymentStatus.Pending && x.OrderStatusId != (int)OrderStatus.Cancelled);
        model.AwaitingPaymentOrders = await awaiting.CountAsync();
        model.AwaitingPayment = M(model.AwaitingPaymentOrders > 0 ? await awaiting.SumAsync(x => x.OrderTotal) : 0);

        var realizedIds = realized.Select(x => x.Id);
        var items = _db.OrderItems.AsNoTracking().Where(x => realizedIds.Contains(x.OrderId));
        model.UnitsSold = await items.SumAsync(x => (int?)x.Quantity) ?? 0;

        var top = await items
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.PriceInclTax) })
            .OrderByDescending(x => x.Quantity)
            .ThenByDescending(x => x.Revenue)
            .Take(10)
            .ToListAsync();

        var topIds = top.Select(x => x.ProductId).ToArray();
        var names = await _db.Products.AsNoTracking().Where(x => topIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
        model.TopProducts = [.. top.Select(x => new TopProductRow
        {
            ProductId = x.ProductId,
            Name = names.GetValueOrDefault(x.ProductId) ?? "#" + x.ProductId,
            Quantity = x.Quantity,
            Revenue = M(x.Revenue)
        })];

        var points = await realized.Select(x => new { x.CreatedOnUtc, x.OrderTotal }).ToListAsync();
        model.MonthlyBuckets = (filter.To.Value - filter.From.Value).TotalDays > 92;
        model.Revenue = BuildBuckets(
            points.Select(x => (dt.ConvertToUserTime(x.CreatedOnUtc, DateTimeKind.Utc), x.OrderTotal)),
            filter.From.Value,
            filter.To.Value,
            model.MonthlyBuckets);
        foreach (var bucket in model.Revenue)
        {
            bucket.Money = M(bucket.Amount);
        }

        model.OrdersByStatus = [.. (await orders
            .GroupBy(x => x.OrderStatusId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync())
            .OrderBy(x => x.Key)
            .Select(x => ((OrderStatus)x.Key, x.Count))];

        var lowStock = _db.Products
            .AsNoTracking()
            .Where(x => !x.Deleted && !x.IsSystemProduct && x.Published
                && x.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStock
                && x.StockQuantity <= x.MinStockQuantity);

        model.StockAlertCount = await lowStock.CountAsync();
        model.StockAlerts = await lowStock
            .OrderBy(x => x.StockQuantity)
            .ThenBy(x => x.Name)
            .Take(10)
            .Select(x => new StockAlertRow { ProductId = x.Id, Name = x.Name, Sku = x.Sku, StockQuantity = x.StockQuantity, MinStockQuantity = x.MinStockQuantity })
            .ToListAsync();

        return model;
    }

    /// <summary>
    /// One bucket per day (or month) from <paramref name="from"/> to <paramref name="to"/>, empty ones included.
    /// </summary>
    internal static List<ReportBucket> BuildBuckets(IEnumerable<(DateTime UserTime, decimal Amount)> points, DateTime from, DateTime to, bool monthly)
    {
        DateTime Key(DateTime d) => monthly ? new DateTime(d.Year, d.Month, 1) : d.Date;

        var buckets = new List<ReportBucket>();
        for (var d = Key(from); d <= Key(to); d = monthly ? d.AddMonths(1) : d.AddDays(1))
        {
            buckets.Add(new ReportBucket { Start = d });
        }

        var index = buckets.ToDictionary(x => x.Start);
        foreach (var (time, amount) in points)
        {
            if (index.TryGetValue(Key(time), out var bucket))
            {
                bucket.Amount += amount;
                bucket.Orders++;
            }
        }

        return buckets;
    }
}
