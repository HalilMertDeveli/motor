using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MotorShop.Legal.Configuration;
using MotorShop.Legal.Filters;
using MotorShop.Legal.Services;
using NUnit.Framework;
using Smartstore.Engine;
using Smartstore.Events;
using Smartstore.Templating.Liquid;

namespace MotorShop.Legal.Tests;

[TestFixture]
public class LegalCheckoutTests
{
    private static LegalDocumentData CreateData() => new()
    {
        SellerName = "Motor Parça Ltd.",
        SellerAddress = "Örnek Cad. 1, İstanbul",
        BuyerName = "Ayşe <b>Test</b>",
        BuyerEmail = "ayse@example.com",
        BillingAddress = "Test Sok. 2, Ankara",
        ShippingAddress = "Test Sok. 2, Ankara",
        OrderNumber = "1001",
        Date = new DateTime(2026, 10, 19),
        PaymentMethod = "Kredi kartı",
        ShippingMethod = "Standart",
        SubTotal = "100,00 ₺",
        Shipping = "0,00 ₺",
        Tax = "16,67 ₺",
        Total = "100,00 ₺",
        Items =
        [
            new() { Name = "Fren balatası <script>", Sku = "FB-1", Quantity = 2, UnitPrice = "50,00 ₺", Total = "100,00 ₺" }
        ]
    };

    private static RenderedLegalDocument Doc(string key, string html, string template = "t") => new()
    {
        Key = key,
        TopicId = 7,
        TopicSystemName = "Topic" + key,
        Title = key,
        Html = html,
        TemplateHash = LegalAcknowledgment.ComputeHash(template),
        RenderHash = LegalAcknowledgment.ComputeHash(html)
    };

    [Test]
    public void ComputeHash_is_lowercase_sha256()
    {
        Assert.That(LegalAcknowledgment.ComputeHash("abc"),
            Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        Assert.That(LegalAcknowledgment.ComputeHash(null), Is.EqualTo(LegalAcknowledgment.ComputeHash(string.Empty)));
    }

    [Test]
    public void Acknowledgment_keeps_confirm_hashes_and_adds_order_hashes()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var ack = LegalAcknowledgment.Create(
            [Doc(LegalDocumentKeys.PreInformation, "cart-pre"), Doc(LegalDocumentKeys.DistanceSales, "cart-mss")],
            accepted: true, "tr-TR", now);

        // Topic edited between confirm and placement: the template hash of the confirm step must be kept.
        ack.ApplyOrderDocuments([Doc(LegalDocumentKeys.PreInformation, "order-pre", template: "edited")]);

        var pre = ack.Documents.Single(x => x.Key == LegalDocumentKeys.PreInformation);
        Assert.Multiple(() =>
        {
            Assert.That(ack.Accepted, Is.True);
            Assert.That(ack.AcceptedOnUtc, Is.EqualTo(now));
            Assert.That(pre.TemplateHash, Is.EqualTo(LegalAcknowledgment.ComputeHash("t")));
            Assert.That(pre.ConfirmRenderHash, Is.EqualTo(LegalAcknowledgment.ComputeHash("cart-pre")));
            Assert.That(pre.OrderRenderHash, Is.EqualTo(LegalAcknowledgment.ComputeHash("order-pre")));
            Assert.That(ack.Documents.Single(x => x.Key == LegalDocumentKeys.DistanceSales).OrderRenderHash, Is.Null);
        });
    }

    [Test]
    public void Acknowledgment_roundtrips_as_json()
    {
        var ack = LegalAcknowledgment.Create([Doc(LegalDocumentKeys.DistanceSales, "x")], false, "tr-TR", DateTime.UtcNow);
        var copy = LegalAcknowledgment.Deserialize(ack.Serialize());

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy.Version, Is.EqualTo(LegalAcknowledgment.CurrentVersion));
        Assert.That(copy.Accepted, Is.False);
        Assert.That(copy.Documents.Single().ConfirmRenderHash, Is.EqualTo(ack.Documents.Single().ConfirmRenderHash));
        Assert.That(copy.Serialize(), Does.Not.Contain("CustomerIp"), "The IP is stored by Smartstore in Order.CustomerIp, not duplicated.");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("{not json")]
    public void Deserialize_returns_null_for_invalid_input(string json)
        => Assert.That(LegalAcknowledgment.Deserialize(json), Is.Null);

    [Test]
    public void Liquid_model_html_encodes_customer_values()
    {
        var model = CreateData().ToLiquidModel(CultureInfo.GetCultureInfo("tr-TR"));
        var buyer = (IDictionary<string, object>)model["Buyer"];
        var item = (IDictionary<string, object>)((List<object>)model["Items"]).Single();
        var order = (IDictionary<string, object>)model["Order"];

        Assert.That(buyer["Name"], Is.EqualTo("Ayşe &lt;b&gt;Test&lt;/b&gt;"));
        Assert.That(item["Name"], Is.EqualTo("Fren balatası &lt;script&gt;"));
        Assert.That(item["Quantity"], Is.EqualTo(2));
        Assert.That(order["Date"], Is.EqualTo("19.10.2026"));
    }

    [TestCase(new[] { "true" }, true)]
    [TestCase(new[] { "on" }, true)]
    [TestCase(new[] { "false" }, false)]
    [TestCase(new string[0], false)]
    [TestCase(null, false)]
    public void Checkbox_value_is_parsed(string[] values, bool expected)
        => Assert.That(LegalConfirmFilter.IsAccepted(values), Is.EqualTo(expected));

    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, true)]
    public void Acknowledgment_is_only_mandatory_when_configured(bool required, bool accepted, bool expected)
        => Assert.That(LegalConfirmFilter.IsAcknowledgmentSufficient(required, accepted), Is.EqualTo(expected));

    [Test]
    public void Flatten_joins_address_lines()
    {
        Assert.That(LegalDocumentService.Flatten("Ali Veli\r\nCadde 1<br />\n34000 İstanbul\n"), Is.EqualTo("Ali Veli, Cadde 1, 34000 İstanbul"));
        Assert.That(LegalDocumentService.Flatten(null), Is.Null);
    }

    [Test]
    public void Placeholders_are_marked_as_draft_and_contract_documents_are_not_published()
    {
        var placeholders = LegalTopicSeeder.GetPlaceholders().ToList();

        Assert.That(placeholders.Select(x => x.SystemName), Is.EquivalentTo(new[]
        {
            LegalTopicNames.PreInformation, LegalTopicNames.DistanceSales, LegalTopicNames.Withdrawal, LegalTopicNames.PrivacyNotice
        }));
        Assert.That(placeholders, Has.All.Matches<(string SystemName, string Slug, string Title, string Body, bool Published)>(x => x.Body.Contains("TASLAK – hukuk onayı gerekli")));
        Assert.That(placeholders.Select(x => x.Slug), Has.All.Matches<string>(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[a-z-]+$")),
            "Explicit Turkish slugs, Smartstore's default slugifier would turn Ö into 'oe'.");
        Assert.That(placeholders.Where(x => !x.Published).Select(x => x.SystemName),
            Is.EquivalentTo(new[] { LegalTopicNames.PreInformation, LegalTopicNames.DistanceSales }));
    }

    [Test]
    public async Task Placeholder_contracts_render_order_data_with_liquid()
    {
        var engine = new LiquidTemplateEngine(
            Mock.Of<IApplicationContext>(),
            Mock.Of<IEventPublisher>(),
            new MemoryCache(new MemoryCacheOptions()),
            null,
            null);

        var culture = CultureInfo.GetCultureInfo("tr-TR");
        var data = CreateData();
        var pre = LegalTopicSeeder.GetPlaceholders().Single(x => x.SystemName == LegalTopicNames.PreInformation);

        var html = await engine.RenderAsync(pre.Body, data.ToLiquidModel(culture), culture);

        Assert.That(html, Does.Contain("Motor Parça Ltd."));
        Assert.That(html, Does.Contain("Sipariş no: 1001"));
        Assert.That(html, Does.Contain("<td>Fren balatası &lt;script&gt;</td><td>2</td><td>50,00 ₺</td>"));
        Assert.That(html, Does.Contain("Genel toplam: 100,00 ₺"));
        Assert.That(html, Does.Not.Contain("{{"));

        // At the confirm step the order number is not known yet.
        data.OrderNumber = null;
        html = await engine.RenderAsync(pre.Body, data.ToLiquidModel(culture), culture);
        Assert.That(html, Does.Contain("[sipariş onayında atanır]"));
    }
}

[TestFixture]
public class LegalTermsTextFilterTests
{
    [Test]
    public void Replaces_core_terms_text_on_confirm_view_only()
    {
        var model = new Smartstore.Web.Models.Checkout.CheckoutConfirmModel { TermsOfService = "I agree ... stored electronically" };
        var view = new Microsoft.AspNetCore.Mvc.ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()) { Model = model } };

        Assert.That(LegalTermsTextFilter.ApplyTermsText(view, "payment hint"), Is.True);
        Assert.That(model.TermsOfService, Is.EqualTo("payment hint"));

        // Terminal checkout: no core text, nothing added.
        var terminal = new Smartstore.Web.Models.Checkout.CheckoutConfirmModel();
        view.ViewData.Model = terminal;
        Assert.That(LegalTermsTextFilter.ApplyTermsText(view, "payment hint"), Is.False);
        Assert.That(terminal.TermsOfService, Is.Null);

        Assert.That(LegalTermsTextFilter.ApplyTermsText(new Microsoft.AspNetCore.Mvc.RedirectResult("/x"), "payment hint"), Is.False);
    }
}
