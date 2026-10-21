# Legal checkout (Turkish distance sales)

Module: `MotorShop.Legal` (`src/Smartstore.Modules/MotorShop.Legal`).

> All texts shipped by the module are **placeholders** marked "TASLAK – hukuk onayı gerekli".
> They are not legal advice. A lawyer must write or approve every text before go-live.

## What the module does

| Area | Mechanism | Smartstore part reused |
|---|---|---|
| Ön Bilgilendirme Formu (pre-contractual information) | Topic `MotorPreInformationForm`, rendered with the cart at the confirm step and with the placed order afterwards | Topics, Liquid template engine, widget zone `checkout_confirm_before_summary` |
| Mesafeli Satış Sözleşmesi (distance sales agreement) | Topic `MotorDistanceSalesAgreement`, same rendering | same |
| Cayma hakkı (right of withdrawal) | Topic `MotorRightOfWithdrawal`, linked at the confirm step | Topics |
| KVKK Aydınlatma Metni (privacy notice) | Topic `MotorKvkkNotice`, linked at the confirm step. A notice, never a consent checkbox | Topics |
| Acknowledgment | One combined checkbox, enforced client and server side (`CheckoutController.ConfirmOrder` and `ConfirmPayment`) | Checkout confirm form, endpoint filters |
| Evidence per order | Generic attributes on the order: `MotorShop.Legal.Acknowledgment` (JSON) and `MotorShop.Legal.Snapshot.{PreInformation,DistanceSales}` (rendered HTML), plus an admin-only order note | GenericAttribute, OrderNote, `Order.CustomerIp` |
| Customer copy | "Sipariş sözleşmeleri" links on the order details page, `/order/{id}/legal/{doc}` (owner or admin with order read permission) | Widget zone `orderdetails_page_afterproducts` |
| Cookie preferences | Smartstore's cookie manager. On install the requirement is switched from "EU countries only" to "depends on country" and Türkiye's `DisplayCookieManager` is enabled | `PrivacySettings`, `CookieConsentManager` |

No core file and no MotorDark view is changed.

### Acknowledgment record

```json
{
  "Version": 1,
  "Accepted": true,
  "AcceptedOnUtc": "2026-10-19T11:01:15.99Z",
  "LanguageCulture": "tr-TR",
  "Documents": [
    {
      "Key": "PreInformation",
      "TopicSystemName": "MotorPreInformationForm",
      "TopicId": 12,
      "TemplateHash": "sha256 of the topic title + body = text version",
      "ConfirmRenderHash": "sha256 of the document shown with the cart",
      "OrderRenderHash": "sha256 of the stored snapshot rendered with the order"
    }
  ]
}
```

- The IP address is **not** duplicated: Smartstore already stores it in `Order.CustomerIp`.
- No identity numbers (TCKN) or other sensitive identifiers are collected.
- Orders created outside the storefront confirm step (admin, recurring, API) have no record.

## Template tokens

Topic bodies of the two contract documents are Liquid templates. Every value is HTML encoded.

| Token | Content |
|---|---|
| `{{ Seller.Name }}`, `Address`, `Phone`, `Email`, `TaxNumber`, `VatId`, `CommercialRegister`, `Url` | Configuration > Settings > Company information and contact data |
| `{{ Buyer.Name }}`, `Email`, `Phone`, `BillingAddress`, `ShippingAddress` | Billing / shipping address |
| `{{ Order.Number }}` | Empty at the confirm step, order number afterwards |
| `{{ Order.Date }}`, `PaymentMethod`, `ShippingMethod`, `SubTotal`, `Shipping`, `Tax`, `Total` | Order data, formatted |
| `{% for item in Items %}{{ item.Name }} {{ item.Sku }} {{ item.Quantity }} {{ item.UnitPrice }} {{ item.Total }}{% endfor %}` | Line items |

The two contract topics are created **unpublished**: their raw page would show the tokens.
They are only rendered inside the checkout and in the order snapshot.

## Admin steps

1. Configuration > Plugins: install "Motor legal checkout (TR distance sales)".
2. Configuration > Settings > Company information: company name, address, tax number, MERSİS (commercial register). Contact data: phone, e-mail. These feed the seller block.
3. CMS > Topics: replace the four placeholder texts with the lawyer-approved texts. Keep the tokens in the two contract topics.
4. Optional, Configuration > Settings > All settings:
   - `LegalCheckoutSettings.RequireAcknowledgment` (default `True`)
   - `LegalCheckoutSettings.PrivacyNoticeTopic`: set to `PrivacyInfo` to reuse Smartstore's privacy topic.
   - `LegalCheckoutSettings.Enabled`
5. Configuration > Settings > Customers > Privacy: review cookie consent and the cookie list.

## Decisions for the owner and the lawyer

- Final texts of all four topics, and whether the one combined checkbox is the wording the lawyer wants.
- Whether Smartstore's own confirm text "I agree with the terms of service … my data … can be stored electronically" (`Checkout.TermsOfService.IAccept`, owned by the localization work) should stay. Under KVKK an information notice must not be bundled with consent.
- Whether the contract copy must also be e-mailed (durable medium). The order snapshot and the order details link exist; adding it to the "order placed" e-mail is a template change.
- Retention period for order records and snapshots.
- Cookie list (Privacy settings) and which cookies are essential.

## Known limits

- The payment confirmation path (`ConfirmPayment`, used by redirect payment methods such as 3-D Secure) is covered by the same filter and unit tests, but was not exercised end to end because no such payment method is configured locally.
- Smartstore's `CookieConsentManager` with "depends on country" shows the manager for every resolved country (upstream logic returns `true` in both branches). That matches the wanted behaviour for Türkiye; it is noted here because the setting name suggests otherwise.

## Smartstore's own confirm texts

| Smartstore text / setting | MOTOR | Why |
|---|---|---|
| `Checkout.TermsOfService.IAccept` ("I agree with the terms of service … my data … can be stored electronically"), shown next to both buy buttons | **Replaced** by `Plugins.MotorShop.Legal.PaymentObligationHint` (`LegalCheckoutSettings.ReplaceCoreTermsText`, default on). 6.4 has no setting to switch it off | Acceptance is the MOTOR checkbox; bundling KVKK "consent" into the buy text is wrong. The payment-obligation notice next to the button stays (lawyer to confirm wording) |
| `ShoppingCartSettings.ShowConfirmOrderLegalHint` | Keep **off** (default) | Would add a third hint box |
| `ShoppingCartSettings.ShowSecondBuyButtonBelowCart` | Keep on | Second button also shows the payment-obligation notice |
| `ShoppingCartSettings.ShowEsdRevocationWaiverBox` | Keep (default) | Only appears for downloadable (ESD) products; MOTOR sells physical parts, so nothing is shown |
| `Aria.Label.BuyHint` ("By clicking on Buy, I accept the terms and conditions") | Screen-reader only. Turkish wording belongs to tr-localization: suggest the payment-obligation sentence | Not visible |
| Newsletter / third-party e-mail checkboxes at confirm | If enabled, use "Deactivated" (unticked) only, never pre-ticked | Commercial e-mail consent must be a separate opt-in |
