# Turkish localization (tr-TR), TRY and KDV

MOTOR uses Smartstore's own localization system: language packs (`LocaleStringResource`),
module resource files (`Localization/resources.tr-tr.xml`), localized entities and message templates.
There is no second translation layer.

## What ships

| Area | Location |
|---|---|
| App language pack (7,101 keys, customer-facing ones translated; admin-only keys keep English values) | `src/Smartstore.Web/App_Data/Localization/App/tr/` |
| Installer UI | `src/Smartstore.Web/App_Data/Localization/Installation/installation.tr.xml` |
| Email templates (all 25) | `src/Smartstore.Web/App_Data/EmailTemplates/tr/` |
| Module resources | `resources.tr-tr.xml` in OfflinePayment, Shipping, ShippingByWeight, Tax, Google.Analytics, the five auth modules and MotorShop.Security (MotorShop.Fitment already had one) |
| MotorDark theme texts | `Themes.MotorDark.*` resources (en + tr), defined in migration `MotorTurkishLocalization` |
| Fresh install seed | `TrTRSeedData` (+ `TurkishStoreDefaults`), registered as install language `tr-TR` |
| Existing databases | migration `20261019120000_MotorTurkishLocalization` (data seeder) |

English (en-US) keeps working; the store stays multilingual.

## Fresh installation

Choose **Türkçe (tr-TR)** as the primary language in the installer (or `PrimaryLanguage=tr-TR` for a
headless `POST /install/install`). The Turkish seed sets:

- Language Türkçe as the only, default and admin language.
- Currency: Türk Lirası (TRY) is the primary store currency **and** the primary exchange-rate currency
  (rate 1). Display format `#,##0.00 ₺` with the tr-TR locale, e.g. `1.234,56 ₺`, rounding to 2 decimals,
  midpoint away from zero. Other currencies are unpublished; their rates are outdated sample values.
- Tax: `PricesIncludeTax = true` (prices are entered gross), `TaxDisplayType = IncludingTax`,
  shipping taxable and entered gross with tax category "Genel KDV". Tax categories and fixed rates:
  Genel KDV 20 %, İndirimli KDV I 10 %, İndirimli KDV II 1 %, KDV'den muaf 0 %.
- Country: Türkiye published (all others unpublished), 81 provinces (il) as state/provinces with their
  plate code, Turkish address format (district = City field, province = State field).
- Time zone `Europe/Istanbul`, metric units (cm, kg), Turkish topics/slugs placeholders,
  shipping methods, delivery times, quantity units, price labels, return reasons.
- Storefront theme MotorDark (for every install language).

Sample catalog data stays English.

## Existing databases (migration, runs automatically on startup)

The migration is additive and idempotent. It:

1. Adds the language **Türkçe (tr-TR)** if missing, published, with the lowest display order. Stores
   without an explicit default language therefore show Turkish to new visitors. Admin users keep their
   admin language (`LocalizationSettings.DefaultAdminLanguageId` is not changed).
2. Imports the Turkish app pack and the installed modules' Turkish resources. **Insert only**: existing
   or admin-edited Turkish resources are never overwritten.
3. Adds Turkish `Subject`/`Body` translations to message templates that have none for Turkish yet
   (the English base template is kept).
4. Adds the 81 provinces to Türkiye only if it has no state/provinces, sets the Turkish address format
   only if empty, adds the localized name "Türkiye".
5. Sets the TRY display format only if TRY has no custom format.
6. Sets the storefront theme to MotorDark only where `ThemeSettings.DefaultTheme` is empty or "Flex".

It does **not** touch: the primary currency, exchange rates, published currencies, tax settings,
tax categories, tax rates, product prices, customers, orders or addresses.

### Admin steps for an existing database (decide first, see "Business decisions")

Back up the database before changing currency or tax settings.

1. **Check how prices are stored.** Smartstore stores product prices as plain numbers in the primary store
   currency; there is no currency per price. `Admin > Configuration > Settings > Tax > Prices include tax`
   says whether they are gross (default `true`) or net.
   ```sql
   SELECT "Name", "Value", "StoreId" FROM "Setting"
   WHERE "Name" IN ('TaxSettings.PricesIncludeTax','TaxSettings.TaxDisplayType',
                    'CurrencySettings.PrimaryCurrencyId','CurrencySettings.PrimaryExchangeCurrencyId');
   SELECT "Id","CurrencyCode","Rate","Published","CustomFormatting" FROM "Currency";
   SELECT "Id","DefaultCurrencyId","PrimaryExchangeRateCurrencyId" FROM "Store";
   ```
2. **Primary currency TRY** (only if the stored prices already are lira amounts):
   `Admin > Configuration > Currencies`: publish *Türk Lirası*, set its rate to 1, then
   *Mark as primary store currency* and *Mark as primary exchange rate currency*.
   Smartstore does **not** convert stored prices when the primary currency changes, so a price stored as
   `1500` simply becomes `1.500,00 ₺`. If prices were entered in another currency, they must be
   re-entered/imported in lira instead; do not rely on an exchange rate.
   Afterwards unpublish the other currencies or set real rates (`Admin > Configuration > Currencies >
   exchange rate provider`), otherwise customers could switch to a currency with an outdated rate.
3. **Tax display**: `Admin > Configuration > Settings > Tax`: *Tax display type* = including tax,
   *Display tax suffix* on. Keep *Prices include tax* as it is unless all prices are re-entered.
4. **KDV rates**: `Admin > Configuration > Tax > Tax categories`: create/rename categories
   (e.g. Genel KDV, İndirimli KDV I/II, KDV'den muaf), then `Tax providers > Fixed rate` to enter the rate
   per category, and assign each product's tax category. With *Prices include tax* = true the gross price
   the customer sees stays the stored price; only the net/tax split changes.
5. **Shipping tax**: `Settings > Tax`: *Shipping is taxable*, *Shipping price includes tax*, tax category.
6. **Countries**: `Admin > Configuration > Countries`: unpublish countries you do not ship to.
7. **Default language**: if a store has an explicit default language, set it to Türkçe in
   `Admin > Configuration > Stores`.

## Business decisions — NEEDS OWNER / ACCOUNTANT CONFIRMATION

- KDV rates per product group (20 % / 10 % / 1 % / 0 %) and which category each product belongs to.
- Prices entered gross (`PricesIncludeTax = true`) vs. net; shipping fee taxable at 20 % and entered gross.
- Domestic shipping only (other countries unpublished on fresh installs).
- Return/withdrawal period of 14 days (`OrderSettings.NumberOfDaysReturnRequestAvailable`) and the legal
  topic texts (Cayma hakkı, Kullanım koşulları, Gizlilik): placeholders only, owned by the legal work.
- **TCKN** (T.C. kimlik numarası) or tax number collection is **not** added: it is sensitive personal data
  and a separate business/legal decision (e-invoice / e-archive requirements, KVKK).
- Phone numbers: any national or international notation with 7–15 digits is accepted
  (e.g. `+90 532 123 45 67`, `0532 123 45 67`); no mandatory +90 prefix.
