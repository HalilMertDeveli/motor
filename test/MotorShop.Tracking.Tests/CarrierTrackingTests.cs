using System.IO;
using System.Linq;
using System.Xml.Linq;
using MotorShop.Tracking.Components;
using MotorShop.Tracking.Configuration;
using MotorShop.Tracking.Services;
using NUnit.Framework;
using Smartstore.Core.Checkout.Shipping;

namespace MotorShop.Tracking.Tests;

[TestFixture]
public class CarrierTrackingTests
{
    // Example hosts only, no real carrier is chosen.
    const string Config = """
        # comment line
        Kargo A | https://kargo-a.example/takip?no={0}
        Kargo B|https://kargo-b.example/t/{0}/detay
        kargo a | https://duplicate.example/{0}
        No placeholder | https://kargo-c.example/takip
        Insecure | http://kargo-d.example/{0}
        Script | javascript:alert({0})
        Missing separator https://kargo-e.example/{0}
        """;

    [Test]
    public void ParseCarriers_accepts_only_https_templates_with_placeholder()
    {
        var carriers = CarrierTrackingService.ParseCarriers(Config);

        Assert.That(carriers.Select(x => x.Name), Is.EqualTo(new[] { "Kargo A", "Kargo B" }));
        Assert.That(carriers[1].UrlTemplate, Is.EqualTo("https://kargo-b.example/t/{0}/detay"));
        Assert.That(CarrierTrackingService.ParseCarriers(null), Is.Empty);
        Assert.That(CarrierTrackingService.ParseCarriers("   "), Is.Empty);
    }

    [Test]
    public void BuildUrl_encodes_tracking_number()
    {
        var carrier = new CarrierDefinition("Kargo A", "https://kargo-a.example/takip?no={0}");

        Assert.That(carrier.BuildUrl(" AB 12&x=<1> "), Is.EqualTo("https://kargo-a.example/takip?no=AB%2012%26x%3D%3C1%3E"));
        Assert.That(carrier.BuildUrl(""), Is.Null);
        Assert.That(carrier.BuildUrl(null), Is.Null);
    }

    [Test]
    public void Resolve_fills_empty_url_for_known_carrier_case_insensitive()
    {
        var carriers = CarrierTrackingService.ParseCarriers(Config);

        Assert.That(CarrierTrackingService.ResolveTrackingUrl(carriers, "kargo b", "123", null, null),
            Is.EqualTo("https://kargo-b.example/t/123/detay"));
    }

    [Test]
    public void Resolve_keeps_manual_url()
    {
        var carriers = CarrierTrackingService.ParseCarriers(Config);
        const string manual = "https://elle-girilen.example/takip/123";

        Assert.That(CarrierTrackingService.ResolveTrackingUrl(carriers, "Kargo A", "123", null, manual), Is.EqualTo(manual));
    }

    [Test]
    public void Resolve_replaces_generated_url_when_tracking_number_or_carrier_changes()
    {
        var carriers = CarrierTrackingService.ParseCarriers(Config);
        var old = carriers[0].BuildUrl("111");

        // New tracking number.
        Assert.That(CarrierTrackingService.ResolveTrackingUrl(carriers, "Kargo A", "222", "111", old),
            Is.EqualTo("https://kargo-a.example/takip?no=222"));

        // Carrier changed, same number.
        Assert.That(CarrierTrackingService.ResolveTrackingUrl(carriers, "Kargo B", "111", null, old),
            Is.EqualTo("https://kargo-b.example/t/111/detay"));

        // Tracking number removed: the generated link goes away.
        Assert.That(CarrierTrackingService.ResolveTrackingUrl(carriers, "Kargo A", null, "111", old), Is.Null);
    }

    [Test]
    public void Resolve_returns_null_for_unknown_or_missing_carrier()
    {
        var carriers = CarrierTrackingService.ParseCarriers(Config);

        Assert.That(CarrierTrackingService.ResolveTrackingUrl(carriers, "Bilinmeyen", "123", null, null), Is.Null);
        Assert.That(CarrierTrackingService.ResolveTrackingUrl(carriers, null, "123", null, null), Is.Null);
        Assert.That(CarrierTrackingService.ResolveTrackingUrl([], "Kargo A", "123", null, null), Is.Null);
    }

    [Test]
    public void ApplyTrackingUrl_uses_default_carrier_when_shipment_has_none()
    {
        var service = new CarrierTrackingService(new ShipmentTrackingSettings { Carriers = Config, DefaultCarrier = "Kargo B" });
        var shipment = new Shipment { TrackingNumber = "XY9" };

        Assert.That(service.ApplyTrackingUrl(shipment, null), Is.True);
        Assert.That(shipment.TrackingUrl, Is.EqualTo("https://kargo-b.example/t/XY9/detay"));

        // Explicit carrier wins over the default.
        Assert.That(service.ApplyTrackingUrl(shipment, "Kargo A"), Is.True);
        Assert.That(shipment.TrackingUrl, Is.EqualTo("https://kargo-a.example/takip?no=XY9"));

        // Nothing to change.
        Assert.That(service.ApplyTrackingUrl(shipment, "Kargo A"), Is.False);
    }

    [Test]
    public void ApplyTrackingUrl_without_configuration_changes_nothing()
    {
        var service = new CarrierTrackingService(new ShipmentTrackingSettings());
        var shipment = new Shipment { TrackingNumber = "XY9", TrackingUrl = "https://elle.example/1" };

        Assert.That(service.ApplyTrackingUrl(shipment, "Kargo A"), Is.False);
        Assert.That(shipment.TrackingUrl, Is.EqualTo("https://elle.example/1"));
    }

    [TestCase("https://kargo.example/1", true)]
    [TestCase("http://kargo.example/1", true)]
    [TestCase("javascript:alert(1)", false)]
    [TestCase("/relative", false)]
    [TestCase(null, false)]
    public void Order_details_only_renders_absolute_http_links(string url, bool expected)
        => Assert.That(OrderShipmentTrackingViewComponent.IsSafeUrl(url), Is.EqualTo(expected));

    [TestCase("ShipmentSent.CustomerNotification", "kargoya verildi", "Shipment.TrackingUrl")]
    [TestCase("ShipmentDelivered.CustomerNotification", "teslim edildi", "Shipment.DeliveredOn")]
    public void Turkish_templates_are_valid_and_keep_the_model_types(string name, string subjectText, string token)
    {
        Assert.That(ShipmentTemplateLocalizer.TemplateNames, Does.Contain(name));

        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Smartstore.sln")))
        {
            root = root.Parent;
        }

        Assert.That(root, Is.Not.Null, "Repository root not found.");

        var path = Path.Combine(root.FullName, "src", "Smartstore.Modules", "MotorShop.Tracking", "App_Data", "EmailTemplates", "tr", name + ".xml");
        var xml = XDocument.Load(path).Root;

        Assert.That(xml.Element("Subject").Value, Does.Contain(subjectText));
        Assert.That(xml.Element("ModelTypes").Value, Is.EqualTo("Shipment, Shipment.Order, Shipment.Customer"));
        Assert.That(xml.Element("Body").Value, Does.Contain("{% extends 'master' %}").And.Contain(token).And.Contain("Merhaba"));
    }
}
