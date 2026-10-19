# Shipping, shipments and tracking

MOTOR uses Smartstore's own shipping methods, shipping rate providers, shipments and shipment e-mails.
The `MotorShop.Tracking` module (`src/Smartstore.Modules/MotorShop.Tracking`) only adds what is missing for a
carrier-neutral setup: a tracking URL template per carrier, a tracking card on the customer order page and
Turkish shipment e-mail texts. **No courier API is called**: the carrier has not been chosen yet.

> No price or threshold is set by code. The amounts below are decisions for the owner.

## 1. Shipping method and fee (admin steps, no code)

1. **Configuration > Shipping > Shipping methods**: create the methods customers can choose, e.g. "Standart kargo".
   Remove or limit the sample methods ("In-Store Pickup", "By Ground", ...) on a production store.
2. **Configuration > Shipping > Providers**: activate **Fixed rate shipping** (`Shipping.FixedRate`, module `Smartstore.Shipping`).
   - Configure: enter one rate per shipping method. The value is stored as the setting
     `ShippingRateComputationMethod.FixedRate.Rate.ShippingMethodId{id}`.
   - For rates by order total, weight or country use "Shipping by total" (`Smartstore.Shipping`) or "Shipping by weight" instead.
3. **Configuration > Settings > Shipping**: free shipping threshold.
   - `Free shipping over X` (`ShippingSettings.FreeShippingOverXEnabled`)
   - `Value of X` (`ShippingSettings.FreeShippingOverXValue`). The comparison is **strictly greater than**
     (`subtotal > X`, `OrderCalculationService`), so a cart exactly at X still pays shipping.
   - `Including tax` (`ShippingSettings.FreeShippingOverXIncludingTax`). With prices including KDV, set this to on.
4. Optionally restrict methods per country or customer role with rules (shipping method edit page).

## 2. Carrier tracking URLs

**Configuration > Plugins > Shipment tracking (carrier neutral) > Configure**

```
# one carrier per line: name | https URL template, {0} = tracking number
Kargo firması | https://kargo-firmasi.example/takip?no={0}
```

- Only absolute `https` templates containing `{0}` are accepted. The tracking number is URL encoded.
  Invalid lines are listed on the page and ignored.
- `Default carrier`: used when a shipment has no carrier name.
- When a shipment is saved with a tracking number, the module writes the generated link into the shipment's
  **Tracking URL**. A URL typed in by hand is never replaced. A generated URL is updated when the tracking number
  or the carrier changes.
- Smartstore then shows the link everywhere it already uses `Shipment.TrackingUrl`: the customer shipment
  page, the "shipment sent" e-mail (`{{ Shipment.TrackingUrl }}`) and the admin shipment page.

## 3. Shipment workflow (admin)

1. **Sales > Orders > (order) > Shipments > Add shipment**: choose the items and quantities.
2. **Edit shipment**: `Carrier` (must match a configured carrier name, case-insensitive) and `Tracking number`.
   Save → the tracking URL is filled.
3. **Set as shipped** → `ShippedDateUtc` and the order's shipping status are updated, and
   `ShipmentSent.CustomerNotification` is queued.
4. **Set as delivered** → `DeliveryDateUtc`, and `ShipmentDelivered.CustomerNotification` is queued. When everything
   is delivered and the order is paid, the order becomes Complete (`OrderCompleted.CustomerNotification`).

Shipment status = Smartstore's own data: not yet shipped / shipped on date / delivered on date per shipment,
and `Order.ShippingStatus` (NotYetShipped, PartiallyShipped, Shipped, Delivered) for the order.

## 4. Customer view

The order details page (`/order/details/{id}`) gets a "Kargo takibi" card (zone `orderdetails_page_afterproducts`)
for every shipped shipment: carrier, tracking number, "Kargonu takip et" button and the shipped or delivered date.
It can be switched off with `ShipmentTrackingSettings.ShowOnOrderDetails`.

## 5. Turkish shipment e-mails

`App_Data/EmailTemplates/tr/ShipmentSent.CustomerNotification.xml` and `ShipmentDelivered.CustomerNotification.xml`
in the module hold Turkish subject and body texts. They are written as **localized** values of the existing templates
for every language whose culture starts with `tr`:

- on module install, and
- on the button "Türkçe metinleri uygula" on the configure page (for a Turkish language added later).

A template that already has a Turkish subject or body is not touched, so texts from the localization work win.
The shared partials (`address`, `shipment` in the theme's `EmailTemplates`) are not changed by this module.

## Decisions for the owner

- Carrier(s) and their tracking URL templates.
- Shipping methods, fixed rate(s), free shipping threshold and whether it includes KDV.
- Final wording of the Turkish e-mails (review with the localization owner).
