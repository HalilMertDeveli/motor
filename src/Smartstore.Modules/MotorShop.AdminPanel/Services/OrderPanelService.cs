using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Models;
using Smartstore.Core;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Shipping;
using Smartstore.Core.Common;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Logging;
using Smartstore.Core.Messaging;
using Smartstore.Core.Rules;
using Smartstore.Core.Rules.Filters;
using Smartstore.Engine.Modularity;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// Orders, shipments, customers and invoice status. Every state change goes through
/// <see cref="IOrderProcessingService"/>; nothing writes order, payment or shipping status directly.
/// Payment operations of iyzico orders (refund, cancel of a paid order) stay with the iyzico module.
/// </summary>
public class OrderPanelService
{
    public const string IyzicoSystemName = "Payments.Iyzico";
    const string CarrierKey = "Carrier";

    private readonly SmartDbContext _db;
    private readonly ICommonServices _services;
    private readonly IOrderProcessingService _orderProcessing;
    private readonly IPaymentService _paymentService;
    private readonly ModuleManager _moduleManager;
    private readonly IMessageFactory _messageFactory;

    public OrderPanelService(
        SmartDbContext db,
        ICommonServices services,
        IOrderProcessingService orderProcessing,
        IPaymentService paymentService,
        ModuleManager moduleManager,
        IMessageFactory messageFactory)
    {
        _db = db;
        _services = services;
        _orderProcessing = orderProcessing;
        _paymentService = paymentService;
        _moduleManager = moduleManager;
        _messageFactory = messageFactory;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    private string R(string key, params object[] args)
        => T("Plugins.MotorShop.AdminPanel." + key, args);

    private Money M(decimal amount)
        => _services.CurrencyService.CreateMoney(amount, _services.CurrencyService.PrimaryCurrency);

    private DateTime UserTime(DateTime utc)
        => _services.DateTimeHelper.ConvertToUserTime(utc, DateTimeKind.Utc);

    private async Task<IQueryable<Order>> AuthorizedOrdersAsync()
    {
        var customer = _services.WorkContext.CurrentCustomer;
        var storeIds = await _services.StoreMappingService.GetAuthorizedStoreIdsAsync("Customer", customer.Id);

        return _db.Orders.AsNoTracking().Where(x => !x.Deleted).ApplyCustomerFilter(storeIds);
    }

    #region List

    public async Task<OrderListModel> GetOrdersAsync(OrderFilter filter, IQueryable<Order> baseQuery = null)
    {
        filter.Normalize();
        var query = baseQuery ?? await AuthorizedOrdersAsync();

        if (filter.Q.HasValue())
        {
            var number = filter.Q.TrimStart('#');
            query = int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _) || Guid.TryParse(number, out _)
                ? query.ApplyOrderNumberFilter(number)
                : query.ApplySearchFilter<Order, string>(filter.Q, LogicalRuleOperator.Or,
                    x => x.Customer.Email,
                    x => x.BillingAddress.Email,
                    x => x.BillingAddress.FirstName,
                    x => x.BillingAddress.LastName,
                    x => x.BillingAddress.Company,
                    x => x.Customer.FullName);
        }

        var dt = _services.DateTimeHelper;
        if (filter.From.HasValue)
        {
            var fromUtc = dt.ConvertToUtcTime(filter.From.Value.Date, dt.CurrentTimeZone);
            query = query.Where(x => x.CreatedOnUtc >= fromUtc);
        }
        if (filter.To.HasValue)
        {
            var toUtc = dt.ConvertToUtcTime(filter.To.Value.Date, dt.CurrentTimeZone).AddDays(1);
            query = query.Where(x => x.CreatedOnUtc < toUtc);
        }

        query = query.ApplyStatusFilter(
            filter.OrderStatus.HasValue ? [(int)filter.OrderStatus.Value] : null,
            filter.PaymentStatus.HasValue ? [(int)filter.PaymentStatus.Value] : null,
            filter.ShippingStatus.HasValue ? [(int)filter.ShippingStatus.Value] : null);

        var total = await query.CountAsync();
        filter.Page = Math.Min(filter.Page, Math.Max(1, (int)Math.Ceiling((double)total / filter.Size)));

        var rows = await ProjectRowsAsync(query
            .OrderByDescending(x => x.CreatedOnUtc)
            .ThenByDescending(x => x.Id)
            .Skip((filter.Page - 1) * filter.Size)
            .Take(filter.Size));

        return new OrderListModel { Filter = filter, TotalCount = total, Rows = rows };
    }

    private async Task<List<OrderRowModel>> ProjectRowsAsync(IQueryable<Order> query)
    {
        var rows = await query
            .Select(x => new
            {
                x.Id,
                x.OrderNumber,
                x.CreatedOnUtc,
                x.OrderTotal,
                x.PaymentMethodSystemName,
                x.OrderStatusId,
                x.PaymentStatusId,
                x.ShippingStatusId,
                x.BillingAddress.FirstName,
                x.BillingAddress.LastName,
                x.BillingAddress.Company,
                BillingEmail = x.BillingAddress.Email,
                BillingCity = x.BillingAddress.City,
                ShippingCity = x.ShippingAddress.City,
                CustomerEmail = x.Customer.Email
            })
            .ToListAsync();

        var paymentNames = await GetPaymentMethodNamesAsync(rows.Select(x => x.PaymentMethodSystemName));

        return [.. rows.Select(x => new OrderRowModel
        {
            Id = x.Id,
            OrderNumber = x.OrderNumber.NullEmpty() ?? x.Id.ToStringInvariant(),
            CreatedOn = UserTime(x.CreatedOnUtc),
            Customer = $"{x.FirstName} {x.LastName}".Trim().NullEmpty() ?? x.Company.NullEmpty() ?? x.CustomerEmail ?? "-",
            Email = x.BillingEmail.NullEmpty() ?? x.CustomerEmail,
            City = x.ShippingCity.NullEmpty() ?? x.BillingCity,
            Total = M(x.OrderTotal),
            PaymentMethod = x.PaymentMethodSystemName.HasValue()
                ? paymentNames.GetValueOrDefault(x.PaymentMethodSystemName) ?? x.PaymentMethodSystemName
                : "-",
            OrderStatus = (OrderStatus)x.OrderStatusId,
            PaymentStatus = (PaymentStatus)x.PaymentStatusId,
            ShippingStatus = (ShippingStatus)x.ShippingStatusId
        })];
    }

    public async Task<Dictionary<string, string>> GetPaymentMethodNamesAsync(IEnumerable<string> systemNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var systemName in systemNames.Where(x => x.HasValue()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var provider = await _paymentService.LoadPaymentProviderBySystemNameAsync(systemName);
            result[systemName] = provider != null
                ? _moduleManager.GetLocalizedFriendlyName(provider.Metadata).NullEmpty() ?? systemName
                : systemName;
        }

        return result;
    }

    #endregion

    #region Detail

    private async Task<Order> LoadOrderAsync(int id, bool tracked)
    {
        var customer = _services.WorkContext.CurrentCustomer;
        var storeIds = await _services.StoreMappingService.GetAuthorizedStoreIdsAsync("Customer", customer.Id);

        var query = _db.Orders
            .AsSplitQuery()
            .IncludeCustomer(false)
            .IncludeOrderItems()
            .IncludeShipments()
            .IncludeBillingAddress()
            .Include(x => x.OrderNotes)
            .Where(x => !x.Deleted)
            .ApplyCustomerFilter(storeIds);

        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<OrderDetailModel> GetOrderAsync(int id)
    {
        var order = await LoadOrderAsync(id, false);
        if (order == null)
        {
            return null;
        }

        var payment = (await GetPaymentMethodNamesAsync([order.PaymentMethodSystemName])).Values.FirstOrDefault();
        var isIyzico = order.PaymentMethodSystemName.EqualsNoCase(IyzicoSystemName);
        var shipped = new Dictionary<int, int>();
        foreach (var item in order.OrderItems)
        {
            shipped[item.Id] = order.Shipments.SelectMany(s => s.ShipmentItems).Where(si => si.OrderItemId == item.Id).Sum(si => si.Quantity);
        }

        var model = new OrderDetailModel
        {
            Id = order.Id,
            OrderNumber = order.GetOrderNumber(),
            CreatedOn = UserTime(order.CreatedOnUtc),
            PaidOn = order.PaidDateUtc.HasValue ? UserTime(order.PaidDateUtc.Value) : null,
            OrderStatus = order.OrderStatus,
            PaymentStatus = order.PaymentStatus,
            ShippingStatus = order.ShippingStatus,
            PaymentMethod = payment ?? order.PaymentMethodSystemName,
            PaymentMethodSystemName = order.PaymentMethodSystemName,
            IsIyzico = isIyzico,
            ShippingMethod = order.ShippingMethod,
            CustomerComment = order.CustomerOrderComment,
            CustomerId = order.CustomerId,
            CustomerName = order.BillingAddress?.GetFullName(false).NullEmpty() ?? order.Customer?.GetFullName() ?? "-",
            CustomerEmail = order.BillingAddress?.Email.NullEmpty() ?? order.Customer?.FindEmail(),
            CustomerIsGuest = order.Customer?.IsGuest() ?? false,
            BillingAddress = MapAddress(order.BillingAddress),
            ShippingAddress = MapAddress(order.ShippingAddress),
            Items = [.. order.OrderItems.Select(x => new OrderItemModel
            {
                Id = x.Id,
                ProductId = x.ProductId,
                Name = x.Product?.GetLocalized(p => p.Name).Value ?? "#" + x.ProductId,
                Sku = x.Sku,
                AttributeDescription = x.AttributeDescription,
                Quantity = x.Quantity,
                Shipped = shipped.GetValueOrDefault(x.Id),
                UnitPrice = M(x.UnitPriceInclTax),
                Total = M(x.PriceInclTax),
                TaxRate = x.TaxRate
            })],
            Subtotal = M(order.OrderSubtotalInclTax),
            SubtotalDiscount = M(order.OrderSubTotalDiscountInclTax),
            Shipping = M(order.OrderShippingInclTax),
            PaymentFee = M(order.PaymentMethodAdditionalFeeInclTax),
            Tax = M(order.OrderTax),
            TaxRates = [.. order.TaxRatesDictionary.Where(x => x.Value != 0).Select(x => ($"%{x.Key.ToString("0.##", CultureInfo.InvariantCulture)}", M(x.Value)))],
            Discount = M(order.OrderDiscount),
            CreditBalance = M(order.CreditBalance),
            Rounding = M(order.OrderTotalRounding),
            Total = M(order.OrderTotal),
            Refunded = M(order.RefundedAmount),
            HasRefund = order.RefundedAmount > 0,
            AppliedDiscounts = await _db.DiscountUsageHistory
                .AsNoTracking()
                .Where(x => x.OrderId == order.Id && x.Discount != null)
                .Select(x => x.Discount.Name)
                .ToListAsync(),
            Notes = [.. order.OrderNotes
                .OrderByDescending(x => x.CreatedOnUtc)
                .Select(x => new OrderNoteModel { CreatedOn = UserTime(x.CreatedOnUtc), Note = x.Note, DisplayToCustomer = x.DisplayToCustomer })]
        };

        var carriers = await GetCarriersAsync(order.Shipments.Select(x => x.Id));
        model.Shipments = [.. order.Shipments.OrderBy(x => x.CreatedOnUtc).Select(s => new ShipmentModel
        {
            Id = s.Id,
            Carrier = carriers.GetValueOrDefault(s.Id),
            TrackingNumber = s.TrackingNumber,
            TrackingUrl = SafeUrl(s.TrackingUrl),
            CreatedOn = UserTime(s.CreatedOnUtc),
            ShippedOn = s.ShippedDateUtc.HasValue ? UserTime(s.ShippedDateUtc.Value) : null,
            DeliveredOn = s.DeliveryDateUtc.HasValue ? UserTime(s.DeliveryDateUtc.Value) : null,
            Items = [.. s.ShipmentItems.Select(si => (
                model.Items.FirstOrDefault(i => i.Id == si.OrderItemId)?.Name ?? "#" + si.OrderItemId,
                si.Quantity))]
        })];

        var (canCancel, cancelReason) = CancelState(order);
        model.CanMarkPaid = order.CanMarkOrderAsPaid() && !isIyzico;
        model.CanCancel = canCancel;
        model.CancelBlockedReason = cancelReason;
        model.CanComplete = CompleteAllowed(order);
        model.CanAddShipment = order.OrderStatus != OrderStatus.Cancelled && await _orderProcessing.CanAddItemsToShipmentAsync(order);

        return model;
    }

    /// <summary>
    /// The panel only cancels orders without collected money: cancelling does not refund, and refunds of
    /// card payments belong to the payment module.
    /// </summary>
    internal static (bool Allowed, string ReasonKey) CancelState(Order order)
    {
        if (!order.CanCancelOrder())
        {
            return (false, null);
        }

        return order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded or PaymentStatus.Authorized
            ? (false, "Order.CancelPaidBlocked")
            : (true, null);
    }

    /// <summary>
    /// Completing an unpaid order would let Smartstore capture or mark it paid; the panel only completes
    /// paid orders whose goods have left the warehouse (or need no shipping).
    /// </summary>
    internal static bool CompleteAllowed(Order order)
        => order.CanCompleteOrder()
            && order.PaymentStatus == PaymentStatus.Paid
            && order.ShippingStatus is ShippingStatus.Shipped or ShippingStatus.Delivered or ShippingStatus.ShippingNotRequired;

    private static AddressModel MapAddress(Address address)
    {
        if (address == null)
        {
            return null;
        }

        return new AddressModel
        {
            Name = $"{address.FirstName} {address.LastName}".Trim(),
            Company = address.Company,
            Lines = string.Join(", ", new[] { address.Address1, address.Address2 }.Where(x => x.HasValue())),
            City = string.Join(" ", new[] { address.ZipPostalCode, address.City, address.StateProvince?.Name }.Where(x => x.HasValue())),
            Country = address.Country?.Name,
            Phone = address.PhoneNumber,
            Email = address.Email
        };
    }

    /// <summary>
    /// Only http(s) tracking links are rendered as links.
    /// </summary>
    internal static string SafeUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? url : null;

    private async Task<Dictionary<int, string>> GetCarriersAsync(IEnumerable<int> shipmentIds)
    {
        var ids = shipmentIds.ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await _db.GenericAttributes
            .AsNoTracking()
            .Where(x => x.KeyGroup == nameof(Shipment) && x.Key == CarrierKey && ids.Contains(x.EntityId))
            .ToDictionaryAsync(x => x.EntityId, x => x.Value);
    }

    #endregion

    #region Actions

    public async Task<string> AddNoteAsync(int orderId, string note, bool displayToCustomer)
    {
        note = note?.Trim();
        if (note.IsEmpty() || note.Length > 4000)
        {
            return R("Order.NoteInvalid");
        }

        var order = await LoadOrderAsync(orderId, true);
        if (order == null)
        {
            return R("NotFound");
        }

        var orderNote = _db.OrderNotes.Add(order, note, displayToCustomer);
        await _db.SaveChangesAsync();

        if (displayToCustomer && orderNote != null)
        {
            await _messageFactory.SendNewOrderNoteAddedCustomerNotificationAsync(orderNote, _services.WorkContext.WorkingLanguage.Id);
        }

        LogOrder(order);
        return null;
    }

    public async Task<string> MarkPaidAsync(int orderId)
    {
        var order = await LoadOrderAsync(orderId, true);
        if (order == null)
        {
            return R("NotFound");
        }

        if (order.PaymentMethodSystemName.EqualsNoCase(IyzicoSystemName))
        {
            return R("Order.IyzicoManaged");
        }

        if (!order.CanMarkOrderAsPaid())
        {
            return R("Order.NotAllowed");
        }

        await _orderProcessing.MarkOrderAsPaidAsync(order);
        LogOrder(order);
        return null;
    }

    public async Task<string> CancelAsync(int orderId, bool notifyCustomer)
    {
        var order = await LoadOrderAsync(orderId, true);
        if (order == null)
        {
            return R("NotFound");
        }

        var (allowed, reason) = CancelState(order);
        if (!allowed)
        {
            return R(reason ?? "Order.NotAllowed");
        }

        await _orderProcessing.CancelOrderAsync(order, notifyCustomer);
        LogOrder(order);
        return null;
    }

    public async Task<string> CompleteAsync(int orderId)
    {
        var order = await LoadOrderAsync(orderId, true);
        if (order == null)
        {
            return R("NotFound");
        }

        if (!CompleteAllowed(order))
        {
            return R("Order.CompleteNotAllowed");
        }

        await _orderProcessing.CompleteOrderAsync(order);
        LogOrder(order);
        return null;
    }

    public async Task<ShipmentFormModel> GetShipmentFormAsync(int orderId)
    {
        var order = await LoadOrderAsync(orderId, false);
        if (order == null || order.OrderStatus == OrderStatus.Cancelled)
        {
            return null;
        }

        var model = new ShipmentFormModel
        {
            OrderId = order.Id,
            OrderNumber = order.GetOrderNumber(),
            Carriers = await GetConfiguredCarriersAsync()
        };

        foreach (var item in order.OrderItems.Where(x => x.Product?.IsShippingEnabled == true))
        {
            var remaining = await _orderProcessing.GetShippableItemsCountAsync(item);
            model.Items.Add(new ShipmentFormItem
            {
                OrderItemId = item.Id,
                Name = item.Product.GetLocalized(x => x.Name),
                Sku = item.Sku,
                Ordered = item.Quantity,
                AlreadyShipped = await _orderProcessing.GetShipmentItemsCountAsync(item),
                Remaining = remaining
            });
        }

        return model;
    }

    /// <param name="quantities">Order item id to quantity. Quantities above what is left are clamped by Smartstore.</param>
    public async Task<string> AddShipmentAsync(int orderId, string carrier, string trackingNumber, Dictionary<int, int> quantities)
    {
        var order = await LoadOrderAsync(orderId, true);
        if (order == null)
        {
            return R("NotFound");
        }

        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            return R("Order.NotAllowed");
        }

        var selected = quantities?
            .Where(x => x.Value > 0 && order.OrderItems.Any(i => i.Id == x.Key))
            .ToDictionary(x => x.Key, x => x.Value) ?? [];

        if (selected.Count == 0)
        {
            return R("Shipment.NothingSelected");
        }

        // The tracking URL is filled from the carrier template by MotorShop.Tracking, if installed.
        var shipment = await _orderProcessing.AddShipmentAsync(order, carrier?.Trim().Truncate(100), trackingNumber?.Trim().Truncate(100), null, selected);
        if (shipment == null)
        {
            return R("Shipment.NothingSelected");
        }

        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.EditOrder, T("ActivityLog.EditOrder", order.GetOrderNumber()));
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task<string> ShipAsync(int shipmentId, bool notifyCustomer)
    {
        var shipment = await LoadShipmentAsync(shipmentId);
        if (shipment == null)
        {
            return R("NotFound");
        }

        if (shipment.ShippedDateUtc.HasValue)
        {
            return R("Shipment.AlreadyShipped");
        }

        await _orderProcessing.ShipAsync(shipment, notifyCustomer);
        LogOrder(shipment.Order);
        return null;
    }

    public async Task<string> DeliverAsync(int shipmentId, bool notifyCustomer)
    {
        var shipment = await LoadShipmentAsync(shipmentId);
        if (shipment == null)
        {
            return R("NotFound");
        }

        // Smartstore itself does not guard this.
        if (!shipment.ShippedDateUtc.HasValue || shipment.DeliveryDateUtc.HasValue)
        {
            return R("Shipment.DeliverNotAllowed");
        }

        await _orderProcessing.DeliverAsync(shipment, notifyCustomer);
        LogOrder(shipment.Order);
        return null;
    }

    private async Task<Shipment> LoadShipmentAsync(int shipmentId)
    {
        var customer = _services.WorkContext.CurrentCustomer;
        var storeIds = await _services.StoreMappingService.GetAuthorizedStoreIdsAsync("Customer", customer.Id);

        var shipment = await _db.Shipments
            .Include(x => x.Order)
            .Include(x => x.ShipmentItems)
            .FirstOrDefaultAsync(x => x.Id == shipmentId);

        // Same store scope as the order list.
        return shipment != null && !shipment.Order.Deleted && (storeIds.Length == 0 || storeIds.Contains(shipment.Order.StoreId))
            ? shipment
            : null;
    }

    private void LogOrder(Order order)
    {
        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.EditOrder, T("ActivityLog.EditOrder", order.GetOrderNumber()));
    }

    /// <summary>
    /// Carrier names from MotorShop.Tracking's settings, read as a raw setting so the panel does not depend on that module.
    /// </summary>
    private async Task<List<string>> GetConfiguredCarriersAsync()
    {
        var raw = await _services.Settings.GetSettingByKeyAsync<string>("ShipmentTrackingSettings.Carriers");
        return [.. (raw ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !x.StartsWith('#') && x.Contains('|'))
            .Select(x => x.Split('|')[0].Trim())
            .Where(x => x.HasValue())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    #endregion

    #region Shipping queue

    public async Task<ShipmentQueueModel> GetShipmentQueueAsync()
    {
        var orders = await AuthorizedOrdersAsync();
        var awaiting = orders.Where(x =>
            x.OrderStatusId != (int)OrderStatus.Cancelled &&
            (x.ShippingStatusId == (int)ShippingStatus.NotYetShipped || x.ShippingStatusId == (int)ShippingStatus.PartiallyShipped));

        var model = new ShipmentQueueModel
        {
            AwaitingCount = await awaiting.CountAsync(),
            AwaitingShipment = await ProjectRowsAsync(awaiting.OrderBy(x => x.CreatedOnUtc).Take(50))
        };

        var customer = _services.WorkContext.CurrentCustomer;
        var storeIds = await _services.StoreMappingService.GetAuthorizedStoreIdsAsync("Customer", customer.Id);

        var open = await _db.Shipments
            .AsNoTracking()
            .Where(x => x.ShippedDateUtc != null && x.DeliveryDateUtc == null && !x.Order.Deleted
                && (storeIds.Length == 0 || storeIds.Contains(x.Order.StoreId)))
            .OrderBy(x => x.ShippedDateUtc)
            .Take(100)
            .Select(x => new
            {
                x.Id,
                x.OrderId,
                x.Order.OrderNumber,
                x.TrackingNumber,
                x.TrackingUrl,
                ShippedDateUtc = x.ShippedDateUtc.Value,
                x.Order.BillingAddress.FirstName,
                x.Order.BillingAddress.LastName
            })
            .ToListAsync();

        var carriers = await GetCarriersAsync(open.Select(x => x.Id));
        model.InTransit = [.. open.Select(x => new OpenShipmentRow
        {
            ShipmentId = x.Id,
            OrderId = x.OrderId,
            OrderNumber = x.OrderNumber.NullEmpty() ?? x.OrderId.ToStringInvariant(),
            Customer = $"{x.FirstName} {x.LastName}".Trim(),
            Carrier = carriers.GetValueOrDefault(x.Id),
            TrackingNumber = x.TrackingNumber,
            TrackingUrl = SafeUrl(x.TrackingUrl),
            ShippedOn = UserTime(x.ShippedDateUtc)
        })];

        return model;
    }

    #endregion

    #region Customers

    public async Task<CustomerListModel> GetCustomersAsync(CustomerFilter filter)
    {
        filter.Normalize();

        var query = _db.Customers.AsNoTracking().Where(x => !x.Deleted && !x.IsSystemAccount);

        if (filter.Role != "all")
        {
            var role = await _db.CustomerRoles.AsNoTracking()
                .FirstOrDefaultAsync(x => x.SystemName == (filter.Role == "guests" ? SystemCustomerRoleNames.Guests : SystemCustomerRoleNames.Registered));
            if (role != null)
            {
                query = query.ApplyRolesFilter(role.Id);
            }

            if (filter.Role == "guests")
            {
                // Guests without any order are just anonymous visitors.
                query = query.Where(c => _db.Orders.Any(o => o.CustomerId == c.Id && !o.Deleted));
            }
        }

        if (filter.Q.HasValue())
        {
            // Wider than Smartstore's term filter (full name, company): guests often only have a billing name.
            query = query.ApplySearchFilter<Customer, string>(filter.Q, LogicalRuleOperator.Or,
                x => x.FullName,
                x => x.Company,
                x => x.Email,
                x => x.BillingAddress.FirstName,
                x => x.BillingAddress.LastName,
                x => x.BillingAddress.Email);
        }

        var total = await query.CountAsync();
        filter.Page = Math.Min(filter.Page, Math.Max(1, (int)Math.Ceiling((double)total / filter.Size)));

        var customers = await query
            .OrderByDescending(x => x.CreatedOnUtc)
            .ThenByDescending(x => x.Id)
            .Skip((filter.Page - 1) * filter.Size)
            .Take(filter.Size)
            .Select(x => new
            {
                x.Id,
                x.FirstName,
                x.LastName,
                x.FullName,
                x.Email,
                x.Active,
                BillingEmail = x.BillingAddress.Email,
                BillingFirst = x.BillingAddress.FirstName,
                BillingLast = x.BillingAddress.LastName,
                City = x.BillingAddress.City ?? x.ShippingAddress.City
            })
            .ToListAsync();

        var ids = customers.Select(x => x.Id).ToArray();
        var stats = await OrderStatsAsync(ids);
        var guestIds = await GuestIdsAsync(ids);

        return new CustomerListModel
        {
            Filter = filter,
            TotalCount = total,
            Rows = [.. customers.Select(x =>
            {
                stats.TryGetValue(x.Id, out var s);
                return new CustomerRowModel
                {
                    Id = x.Id,
                    Name = x.FullName.NullEmpty() ?? $"{x.FirstName} {x.LastName}".Trim().NullEmpty() ?? $"{x.BillingFirst} {x.BillingLast}".Trim().NullEmpty() ?? "-",
                    Email = x.Email.NullEmpty() ?? x.BillingEmail,
                    City = x.City,
                    Active = x.Active,
                    IsGuest = guestIds.Contains(x.Id),
                    OrderCount = s.Count,
                    TotalSpend = M(s.Total),
                    LastOrderOn = s.Last.HasValue ? UserTime(s.Last.Value) : null
                };
            })]
        };
    }

    /// <summary>
    /// Order count, spend and last order per customer. Cancelled orders are not counted as spend.
    /// </summary>
    private async Task<Dictionary<int, (int Count, decimal Total, DateTime? Last)>> OrderStatsAsync(int[] customerIds)
    {
        if (customerIds.Length == 0)
        {
            return [];
        }

        var stats = await _db.Orders
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.CustomerId) && !x.Deleted && x.OrderStatusId != (int)OrderStatus.Cancelled)
            .GroupBy(x => x.CustomerId)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(x => x.OrderTotal - x.RefundedAmount), Last = g.Max(x => x.CreatedOnUtc) })
            .ToListAsync();

        return stats.ToDictionary(x => x.Key, x => (x.Count, x.Total, (DateTime?)x.Last));
    }

    private async Task<HashSet<int>> GuestIdsAsync(int[] customerIds)
    {
        var registered = await _db.CustomerRoles.AsNoTracking().Where(x => x.SystemName == SystemCustomerRoleNames.Registered).Select(x => x.Id).FirstOrDefaultAsync();
        var withRegistered = await _db.CustomerRoleMappings
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.CustomerId) && x.CustomerRoleId == registered)
            .Select(x => x.CustomerId)
            .ToListAsync();

        return [.. customerIds.Except(withRegistered)];
    }

    public async Task<CustomerDetailModel> GetCustomerAsync(int id)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.BillingAddress).ThenInclude(x => x.Country)
            .Include(x => x.ShippingAddress).ThenInclude(x => x.Country)
            .IncludeCustomerRoles()
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted && !x.IsSystemAccount);

        if (customer == null)
        {
            return null;
        }

        var stats = (await OrderStatsAsync([id])).GetValueOrDefault(id);
        var orders = await GetOrdersAsync(new OrderFilter { Size = 25 }, (await AuthorizedOrdersAsync()).Where(x => x.CustomerId == id));

        return new CustomerDetailModel
        {
            Id = customer.Id,
            Name = customer.GetFullName().NullEmpty() ?? "-",
            Email = customer.FindEmail(),
            Phone = customer.BillingAddress?.PhoneNumber ?? customer.ShippingAddress?.PhoneNumber,
            Active = customer.Active,
            IsGuest = customer.IsGuest(),
            CreatedOn = UserTime(customer.CreatedOnUtc),
            LastLoginOn = customer.LastLoginDateUtc.HasValue ? UserTime(customer.LastLoginDateUtc.Value) : null,
            Roles = [.. customer.GetRoleNames()],
            BillingAddress = MapAddress(customer.BillingAddress),
            ShippingAddress = MapAddress(customer.ShippingAddress),
            OrderCount = stats.Count,
            TotalSpend = M(stats.Total),
            AverageOrder = M(stats.Count > 0 ? Math.Round(stats.Total / stats.Count, 2) : 0),
            Orders = orders.Rows
        };
    }

    #endregion
}
