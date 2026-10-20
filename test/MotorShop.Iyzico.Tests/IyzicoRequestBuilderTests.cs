using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MotorShop.Iyzico.Configuration;
using MotorShop.Iyzico.Services;
using NUnit.Framework;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Common;
using Smartstore.Core.Identity;

namespace MotorShop.Iyzico.Tests;

[TestFixture]
public class IyzicoRequestBuilderTests
{
    private static Order CreateOrder(decimal total, params decimal[] lines)
    {
        var billing = new Address
        {
            FirstName = "Ayşe",
            LastName = "Yılmaz",
            Email = "ayse@example.com",
            PhoneNumber = "+905350000000",
            Address1 = "Bağdat Cad. 1",
            City = "İstanbul",
            ZipPostalCode = "34710",
            Country = new Country { Name = "Turkey", TwoLetterIsoCode = "TR" }
        };

        var order = new Order
        {
            Id = 42,
            OrderNumber = "42",
            OrderGuid = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
            CustomerId = 7,
            Customer = new Customer { Email = "ayse@example.com", CreatedOnUtc = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc) },
            OrderTotal = total,
            CurrencyRate = 1m,
            CustomerCurrencyCode = "try",
            CustomerIp = "203.0.113.10",
            BillingAddress = billing,
        };

        for (var i = 0; i < lines.Length; i++)
        {
            order.OrderItems.Add(new OrderItem
            {
                Id = i + 1,
                ProductId = 100 + i,
                PriceInclTax = lines[i],
                Product = new Product { Name = "Fren balatası " + i, IsShippingEnabled = true }
            });
        }

        return order;
    }

    [Test]
    public void Itemizes_lines_and_shipping_when_they_add_up()
    {
        var order = CreateOrder(165m, 100m, 50m);
        order.OrderShippingInclTax = 15m;

        var request = IyzicoRequestBuilder.Build(order, new IyzicoSettings(), "conv1", "https://shop.test/iyzico/callback", "tr");

        Assert.That(request.Price, Is.EqualTo("165.0"));
        Assert.That(request.PaidPrice, Is.EqualTo("165.0"));
        Assert.That(request.Currency, Is.EqualTo("TRY"));
        Assert.That(request.BasketId, Is.EqualTo(order.OrderGuid.ToString()));
        Assert.That(request.BasketItems.Select(x => x.Id), Is.EqualTo(new[] { "OI1", "OI2", "SHIPPING" }));
        Assert.That(request.BasketItems.Sum(x => decimal.Parse(x.Price, CultureInfo.InvariantCulture)), Is.EqualTo(165m));
        Assert.That(request.BasketItems[0].ItemType, Is.EqualTo("PHYSICAL"));
        Assert.That(request.Buyer.Name, Is.EqualTo("Ayşe"));
        Assert.That(request.Buyer.Country, Is.EqualTo("Turkey"));
        Assert.That(request.Buyer.RegistrationDate, Is.EqualTo("2025-01-02 03:04:05"));
        Assert.That(request.ShippingAddress.ContactName, Is.EqualTo("Ayşe Yılmaz"), "Billing address is used when there is no shipping address.");
    }

    [Test]
    public void Discount_falls_back_to_single_basket_item()
    {
        var order = CreateOrder(130m, 100m, 50m); // 20 discount

        var request = IyzicoRequestBuilder.Build(order, new IyzicoSettings(), "conv1", "https://shop.test/cb", "tr");

        Assert.That(request.BasketItems, Has.Count.EqualTo(1));
        Assert.That(request.BasketItems[0].Price, Is.EqualTo("130.0"));
        Assert.That(request.BasketItems[0].Name, Does.Contain("42"));
    }

    [Test]
    public void Itemization_can_be_disabled()
    {
        var order = CreateOrder(150m, 100m, 50m);

        var request = IyzicoRequestBuilder.Build(order, new IyzicoSettings { ItemizeBasket = false }, "conv1", "https://shop.test/cb", "en");

        Assert.That(request.BasketItems, Has.Count.EqualTo(1));
        Assert.That(request.Locale, Is.EqualTo("en"));
    }

    [Test]
    public void Free_lines_are_skipped_because_iyzico_rejects_zero_prices()
    {
        var order = CreateOrder(100m, 100m, 0m);

        var request = IyzicoRequestBuilder.Build(order, new IyzicoSettings(), "conv1", "https://shop.test/cb", "tr");

        Assert.That(request.BasketItems.Select(x => x.Id), Is.EqualTo(new[] { "OI1" }));
    }

    [Test]
    public void Customer_currency_amount_uses_currency_rate()
    {
        var order = CreateOrder(100m, 100m);
        order.CurrencyRate = 0.0283m;
        order.CustomerCurrencyCode = "EUR";

        Assert.That(IyzicoRequestBuilder.GetOrderAmount(order), Is.EqualTo(2.83m));
    }

    [Test]
    public void Identity_number_is_the_configured_placeholder_not_a_collected_tckn()
    {
        var order = CreateOrder(100m, 100m);

        Assert.That(IyzicoRequestBuilder.Build(order, new IyzicoSettings(), "c", "https://x/cb", "tr").Buyer.IdentityNumber, Is.EqualTo("11111111111"));
        Assert.That(IyzicoRequestBuilder.Build(order, new IyzicoSettings { PlaceholderIdentityNumber = "22222222222" }, "c", "https://x/cb", "tr").Buyer.IdentityNumber, Is.EqualTo("22222222222"));
    }

    [Test]
    public void Installments_are_parsed_validated_and_always_include_single_payment()
    {
        Assert.That(IyzicoRequestBuilder.GetInstallments(new IyzicoSettings { EnableInstallments = false, EnabledInstallments = "2,3" }), Is.EqualTo(new[] { 1 }));
        Assert.That(IyzicoRequestBuilder.GetInstallments(new IyzicoSettings { EnableInstallments = true, EnabledInstallments = "9, 3,x,2,3,13,0" }), Is.EqualTo(new[] { 1, 2, 3, 9 }));

        var order = CreateOrder(100m, 100m);
        var request = IyzicoRequestBuilder.Build(order, new IyzicoSettings { EnableInstallments = true, EnabledInstallments = "1,2,3,6,9" }, "c", "https://x/cb", "tr");
        Assert.That(request.EnabledInstallments, Is.EqualTo(new List<int> { 1, 2, 3, 6, 9 }));
    }

    [TestCase("tr-TR", "tr")]
    [TestCase("en-US", "en")]
    [TestCase(null, "en")]
    public void Locale_from_language(string culture, string expected)
    {
        Assert.That(IyzicoRequestBuilder.GetLocale(culture), Is.EqualTo(expected));
    }
}
