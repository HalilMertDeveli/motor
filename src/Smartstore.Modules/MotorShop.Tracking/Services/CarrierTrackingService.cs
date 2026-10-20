using MotorShop.Tracking.Configuration;
using Smartstore.Core.Checkout.Shipping;

namespace MotorShop.Tracking.Services;

public sealed record CarrierDefinition(string Name, string UrlTemplate)
{
    public const string Placeholder = "{0}";

    public string BuildUrl(string trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            return null;
        }

        return UrlTemplate.Replace(Placeholder, Uri.EscapeDataString(trackingNumber.Trim()), StringComparison.Ordinal);
    }
}

/// <summary>
/// Builds shipment tracking URLs from the configured carrier templates.
/// </summary>
public class CarrierTrackingService
{
    /// <summary>
    /// Generic attribute key used by Smartstore for the carrier name of a shipment
    /// (admin shipment page, <c>OrderProcessingService.AddShipmentAsync</c>, e-mail token <c>Shipment.Carrier</c>).
    /// </summary>
    public const string CarrierAttributeKey = "Carrier";

    private readonly ShipmentTrackingSettings _settings;

    public CarrierTrackingService(ShipmentTrackingSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<CarrierDefinition> GetCarriers()
        => ParseCarriers(_settings.Carriers);

    /// <summary>
    /// Sets <see cref="Shipment.TrackingUrl"/> from the carrier template.
    /// </summary>
    /// <returns><c>true</c> if the URL changed.</returns>
    public bool ApplyTrackingUrl(Shipment shipment, string carrier, string previousTrackingNumber = null)
    {
        Guard.NotNull(shipment);

        var url = ResolveTrackingUrl(
            GetCarriers(),
            carrier.NullEmpty() ?? _settings.DefaultCarrier,
            shipment.TrackingNumber,
            previousTrackingNumber,
            shipment.TrackingUrl);

        if (url == shipment.TrackingUrl)
        {
            return false;
        }

        shipment.TrackingUrl = url;
        return true;
    }

    /// <summary>
    /// Parses the carrier configuration. Invalid lines are skipped: only absolute https templates
    /// containing the <c>{0}</c> placeholder are accepted, so no script or relative URL can end up in a link.
    /// </summary>
    public static IReadOnlyList<CarrierDefinition> ParseCarriers(string configuration)
    {
        var result = new List<CarrierDefinition>();
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return result;
        }

        foreach (var rawLine in configuration.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('|');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var template = line[(separator + 1)..].Trim();

            if (name.Length == 0
                || !template.Contains(CarrierDefinition.Placeholder, StringComparison.Ordinal)
                || !Uri.TryCreate(template.Replace(CarrierDefinition.Placeholder, "0", StringComparison.Ordinal), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            if (!result.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new CarrierDefinition(name, template));
            }
        }

        return result;
    }

    /// <summary>
    /// Decides the tracking URL of a shipment.
    /// A URL entered by the merchant is never overwritten: the current URL is only replaced when it is empty
    /// or equals a URL this module generated for the current or the previous tracking number.
    /// </summary>
    public static string ResolveTrackingUrl(
        IReadOnlyList<CarrierDefinition> carriers,
        string carrierName,
        string trackingNumber,
        string previousTrackingNumber,
        string currentUrl)
    {
        carriers ??= [];

        var isGenerated = string.IsNullOrWhiteSpace(currentUrl)
            || carriers.Any(x => currentUrl == x.BuildUrl(trackingNumber) || currentUrl == x.BuildUrl(previousTrackingNumber));

        if (!isGenerated)
        {
            return currentUrl;
        }

        var carrier = string.IsNullOrWhiteSpace(carrierName)
            ? null
            : carriers.FirstOrDefault(x => x.Name.Equals(carrierName.Trim(), StringComparison.OrdinalIgnoreCase));

        return carrier?.BuildUrl(trackingNumber);
    }
}
