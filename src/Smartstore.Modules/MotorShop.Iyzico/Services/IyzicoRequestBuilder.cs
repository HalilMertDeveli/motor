using System.Globalization;
using MotorShop.Iyzico.Client;
using MotorShop.Iyzico.Configuration;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Common;

namespace MotorShop.Iyzico.Services;

/// <summary>
/// Builds the checkout form initialize request from a placed order.
/// </summary>
public static class IyzicoRequestBuilder
{
    /// <summary>
    /// Currencies accepted by the checkout form (https://docs.iyzico.com, CF initialize). Technical upper bound only:
    /// the method is offered for <see cref="IyzicoSettings.AllowedCurrencies"/> (default TRY).
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedCurrencies
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TRY", "USD", "EUR", "GBP", "NOK", "CHF" };

    const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Currencies configured in <see cref="IyzicoSettings.AllowedCurrencies"/> that iyzico accepts. Empty if none.
    /// </summary>
    public static IReadOnlySet<string> GetAllowedCurrencies(IyzicoSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (settings.AllowedCurrencies ?? string.Empty).Split(',', ';', ' '))
        {
            var code = part.Trim();
            if (SupportedCurrencies.Contains(code))
            {
                result.Add(code.ToUpperInvariant());
            }
        }

        return result;
    }

    public static bool IsCurrencyAllowed(IyzicoSettings settings, string currencyCode)
        => currencyCode.HasValue() && GetAllowedCurrencies(settings).Contains(currencyCode);

    /// <summary>
    /// The amount iyzico must charge for <paramref name="order"/>, in the customer currency, rounded to 2 decimals.
    /// </summary>
    public static decimal GetOrderAmount(Order order)
        => Round((order ?? throw new ArgumentNullException(nameof(order))).OrderTotal * order.CurrencyRate);

    public static decimal Round(decimal amount)
        => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static string GetLocale(string languageCulture)
        => languageCulture.HasValue() && languageCulture.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";

    public static List<int> GetInstallments(IyzicoSettings settings)
    {
        var result = new List<int>();

        if (settings.EnableInstallments && settings.EnabledInstallments.HasValue())
        {
            foreach (var part in settings.EnabledInstallments.Split(',', ';', ' '))
            {
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= 12 && !result.Contains(n))
                {
                    result.Add(n);
                }
            }
        }

        if (!result.Contains(1))
        {
            result.Insert(0, 1);
        }

        result.Sort();
        return result;
    }

    public static CheckoutFormInitializeRequest Build(
        Order order,
        IyzicoSettings settings,
        string conversationId,
        string callbackUrl,
        string locale)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(conversationId);
        ArgumentException.ThrowIfNullOrEmpty(callbackUrl);

        var billing = order.BillingAddress ?? throw new InvalidOperationException($"Order {order.Id} has no billing address.");
        var shipping = order.ShippingAddress ?? billing;
        var customer = order.Customer;
        var amount = GetOrderAmount(order);
        var price = IyzicoSignature.FormatPriceForRequest(amount);

        var request = new CheckoutFormInitializeRequest
        {
            Locale = locale,
            ConversationId = conversationId,
            Price = price,
            PaidPrice = price,
            Currency = order.CustomerCurrencyCode.ToUpperInvariant(),
            BasketId = order.OrderGuid.ToString(),
            PaymentGroup = "PRODUCT",
            CallbackUrl = callbackUrl,
            EnabledInstallments = GetInstallments(settings),
            Buyer = new IyzicoBuyer
            {
                Id = order.CustomerId.ToString(CultureInfo.InvariantCulture),
                Name = Truncate(billing.FirstName.NullEmpty() ?? "-", 100),
                Surname = Truncate(billing.LastName.NullEmpty() ?? "-", 100),
                Email = Truncate(billing.Email.NullEmpty() ?? customer?.Email, 100),
                GsmNumber = Truncate(billing.PhoneNumber.NullEmpty(), 50),
                // iyzico marks identityNumber as required. The store collects no TCKN (owner decision), so a configurable value is sent.
                IdentityNumber = settings.PlaceholderIdentityNumber.NullEmpty() ?? "11111111111",
                RegistrationAddress = Truncate(FormatStreet(billing), 500),
                City = Truncate(GetCity(billing), 100),
                Country = Truncate(billing.Country?.Name.NullEmpty() ?? "Turkey", 100),
                ZipCode = Truncate(billing.ZipPostalCode.NullEmpty(), 20),
                Ip = Truncate(order.CustomerIp.NullEmpty(), 50),
                RegistrationDate = customer?.CreatedOnUtc.ToString(DateFormat, CultureInfo.InvariantCulture),
                LastLoginDate = customer?.LastLoginDateUtc?.ToString(DateFormat, CultureInfo.InvariantCulture)
            },
            BillingAddress = CreateAddress(billing),
            ShippingAddress = CreateAddress(shipping),
            BasketItems = CreateBasketItems(order, settings, amount)
        };

        return request;
    }

    /// <summary>
    /// iyzico requires basket items whose prices add up to "price", each greater than zero.
    /// Lines are itemized when they add up exactly; discounts, gift cards or rounding fall back to one item for the order.
    /// </summary>
    public static List<IyzicoBasketItem> CreateBasketItems(Order order, IyzicoSettings settings, decimal amount)
    {
        var category = settings.BasketItemCategory.NullEmpty() ?? "General";

        if (settings.ItemizeBasket)
        {
            var items = new List<IyzicoBasketItem>();
            var sum = 0m;
            var valid = true;

            foreach (var item in order.OrderItems.OrderBy(x => x.Id))
            {
                var lineAmount = Round(item.PriceInclTax * order.CurrencyRate);
                if (lineAmount < 0m)
                {
                    valid = false;
                    break;
                }
                if (lineAmount == 0m)
                {
                    // iyzico rejects zero prices. Free lines do not change the total.
                    continue;
                }

                var isPhysical = item.Product?.IsShippingEnabled ?? true;
                items.Add(new IyzicoBasketItem
                {
                    Id = "OI" + item.Id.ToString(CultureInfo.InvariantCulture),
                    Name = Truncate(item.Product?.Name.NullEmpty() ?? ("Item " + item.ProductId.ToString(CultureInfo.InvariantCulture)), 200),
                    Category1 = category,
                    ItemType = isPhysical ? "PHYSICAL" : "VIRTUAL",
                    Price = IyzicoSignature.FormatPriceForRequest(lineAmount)
                });
                sum += lineAmount;
            }

            if (valid)
            {
                AddExtra(items, "SHIPPING", "Shipping", Round(order.OrderShippingInclTax * order.CurrencyRate), category, ref sum, ref valid);
                AddExtra(items, "PAYMENTFEE", "Payment fee", Round(order.PaymentMethodAdditionalFeeInclTax * order.CurrencyRate), category, ref sum, ref valid);
            }

            if (valid && items.Count > 0 && sum == amount)
            {
                return items;
            }
        }

        return
        [
            new IyzicoBasketItem
            {
                Id = "ORDER" + order.Id.ToString(CultureInfo.InvariantCulture),
                Name = "Order " + order.GetOrderNumber(),
                Category1 = category,
                ItemType = order.OrderItems.Any(x => x.Product?.IsShippingEnabled ?? true) ? "PHYSICAL" : "VIRTUAL",
                Price = IyzicoSignature.FormatPriceForRequest(amount)
            }
        ];
    }

    private static void AddExtra(List<IyzicoBasketItem> items, string id, string name, decimal value, string category, ref decimal sum, ref bool valid)
    {
        if (value < 0m)
        {
            valid = false;
        }
        else if (value > 0m)
        {
            items.Add(new IyzicoBasketItem
            {
                Id = id,
                Name = name,
                Category1 = category,
                ItemType = "VIRTUAL",
                Price = IyzicoSignature.FormatPriceForRequest(value)
            });
            sum += value;
        }
    }

    private static IyzicoAddress CreateAddress(Address address)
    {
        return new IyzicoAddress
        {
            ContactName = Truncate($"{address.FirstName} {address.LastName}".Trim().NullEmpty() ?? "-", 200),
            City = Truncate(GetCity(address), 100),
            Country = Truncate(address.Country?.Name.NullEmpty() ?? "Turkey", 100),
            Address = Truncate(FormatStreet(address), 500),
            ZipCode = Truncate(address.ZipPostalCode.NullEmpty(), 20)
        };
    }

    private static string GetCity(Address address)
        => address.City.NullEmpty() ?? address.StateProvince?.Name.NullEmpty() ?? "-";

    private static string FormatStreet(Address address)
        => string.Join(' ', new[] { address.Address1, address.Address2 }.Where(x => x.HasValue())).NullEmpty() ?? "-";

    private static string Truncate(string value, int maxLength)
        => value == null || value.Length <= maxLength ? value : value[..maxLength];
}
