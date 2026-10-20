using System.Threading;
using MotorShop.Tracking.Services;
using Smartstore.Core.Checkout.Shipping;
using Smartstore.Core.Common;
using Smartstore.Core.Data;
using Smartstore.Data.Hooks;

namespace MotorShop.Tracking.Hooks;

/// <summary>
/// Fills <see cref="Shipment.TrackingUrl"/> from the carrier template whenever a shipment with a tracking number
/// is saved. Smartstore then shows the link everywhere it already shows <c>TrackingUrl</c>: customer shipment page,
/// "shipment sent" e-mail and admin shipment page.
/// </summary>
internal class ShipmentTrackingHook : AsyncDbSaveHook<Shipment>
{
    private readonly CarrierTrackingService _trackingService;

    public ShipmentTrackingHook(CarrierTrackingService trackingService)
    {
        _trackingService = trackingService;
    }

    protected override Task<HookResult> OnInsertingAsync(Shipment entity, IHookedEntity entry, CancellationToken cancelToken)
    {
        // A new shipment has no generic attributes yet, the carrier hook below runs once the carrier is saved.
        _trackingService.ApplyTrackingUrl(entity, null);
        return Task.FromResult(HookResult.Ok);
    }

    protected override Task<HookResult> OnUpdatingAsync(Shipment entity, IHookedEntity entry, CancellationToken cancelToken)
    {
        entry.Entry.TryGetModifiedProperty(nameof(Shipment.TrackingNumber), out var previousTrackingNumber);

        var carrier = entity.GenericAttributes?.Get<string>(CarrierTrackingService.CarrierAttributeKey);
        _trackingService.ApplyTrackingUrl(entity, carrier, previousTrackingNumber as string);

        return Task.FromResult(HookResult.Ok);
    }
}

/// <summary>
/// The carrier name is a generic attribute that Smartstore may save after the shipment itself
/// (<c>OrderProcessingService.AddShipmentAsync</c>). Re-applies the tracking URL when it changes.
/// </summary>
internal class ShipmentCarrierHook : AsyncDbSaveHook<GenericAttribute>
{
    private readonly SmartDbContext _db;
    private readonly CarrierTrackingService _trackingService;

    public ShipmentCarrierHook(SmartDbContext db, CarrierTrackingService trackingService)
    {
        _db = db;
        _trackingService = trackingService;
    }

    protected override Task<HookResult> OnInsertedAsync(GenericAttribute entity, IHookedEntity entry, CancellationToken cancelToken)
        => ApplyAsync(entity, cancelToken);

    protected override Task<HookResult> OnUpdatedAsync(GenericAttribute entity, IHookedEntity entry, CancellationToken cancelToken)
        => ApplyAsync(entity, cancelToken);

    private async Task<HookResult> ApplyAsync(GenericAttribute entity, CancellationToken cancelToken)
    {
        if (entity.KeyGroup != nameof(Shipment) || entity.Key != CarrierTrackingService.CarrierAttributeKey)
        {
            return HookResult.Ok;
        }

        var shipment = await _db.Shipments.FindByIdAsync(entity.EntityId, true, cancelToken);
        if (shipment != null && _trackingService.ApplyTrackingUrl(shipment, entity.Value))
        {
            await _db.SaveChangesAsync(cancelToken);
        }

        return HookResult.Ok;
    }
}
