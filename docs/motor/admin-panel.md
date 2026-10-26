# MOTOR premium admin panel

Module `MotorShop.AdminPanel` (`src/Smartstore.Modules/MotorShop.AdminPanel`).
Route: **`/motor-panel`**. The classic Smartstore admin at `/admin` is unchanged and
linked from the panel header ("Klasik yönetim").

The panel is a second, task-focused UI over the same application. It has no
tables, no second product, order or customer store and no authentication of
its own. Everything goes through Smartstore services, permissions and EF Core.

```
Storefront (MotorDark) ─┐
                        ├─ ASP.NET Core / Smartstore ── EF Core ── one database
/admin (classic) ───────┤        services, permissions, hooks, events
/motor-panel (this) ────┘
```

## Status per module

| Module | Route | State | Not in the panel (and why) |
| --- | --- | --- | --- |
| Dashboard | `/motor-panel` | Fully functional | — (product list moved to its own page; old `/motor-panel?stock=…` links redirect) |
| Products | `/motor-panel/products`, `/motor-panel/product/new`, `/motor-panel/product/{id}`, drawer | Fully functional | Variant matrices, tax class, other languages: classic editor (linked), to avoid duplicating Smartstore's editors |
| Categories | `/motor-panel/categories` | Fully functional | ACL/store mappings, localized names: classic editor |
| Orders | `/motor-panel/orders` | Partially functional | Refunds, captures, voids and editing order items: payment module / classic admin. iyzico payments are excluded on purpose |
| Shipments | `/motor-panel/shipments`, order drawer | Fully functional | Live courier API: none selected (tracking links come from MotorShop.Tracking templates) |
| Customers | `/motor-panel/customers` | Read + details | Editing customers, roles, passwords: classic admin |
| Invoices | `/motor-panel/invoices` | Requires external credentials | No certified e-Arşiv/e-Fatura integrator; status only |
| Reports | `/motor-panel/reports` | Fully functional | Profit (product costs) and exports: classic reports |
| Pages & blog | `/motor-panel/content` | Fully functional for pages | Blog posts are edited in WordPress (MotorShop.WordPress), the panel links to it |
| Coupons | `/motor-panel/coupons` | Fully functional | Rule sets, manufacturer discounts, badge texts: classic editor |
| Settings | `/motor-panel/settings` | Partially functional | Currency, tax, payment methods and e-mail accounts are shown read-only and linked (they change prices and payments directly) |
| Hide all prices | dashboard card 4 | Fully functional | — |

## Phase 1: shell and dashboard

| Area | State | Notes |
| --- | --- | --- |
| Entry route, layout, header, tabs | Done | Light theme, independent of MotorDark CSS. Store name and logo come from the current `Store`; title and accent color from `MotorPanelSettings` (empty = store name / MotorDark accent `#ff5a1f`). Every tab is a panel page; each is shown only with its read permission. |
| Card "Toplam ürün" | Done | Opens the product list. |
| Card "Stokta yok" | Done | Opens the list filtered by `stock=OutOfStock`. |
| Card "Gizli fiyatlı ürünler" | Done | `CallForPrice` products; opens the list filtered by `price=CallForPrice`. Unpublished ≠ hidden price. |
| Card "Tüm fiyatları gizle" | Done (phase 2) | See phase 2. |
| Recent orders (5) | Done | Store-scoped like the classic dashboard (`ApplyCustomerFilter`). |
| Currency strip | Done | Configured rates only (`Currency.Rate`, `UpdatedOnUtc`). No external rate API. A TRY-only store shows a single-currency notice. |
| Product list | Done | Search by name or SKU (Smartstore's LINQ catalog search, same as the classic grid), category, stock, price visibility (incl. **"Fiyatsız"**: price 0 and not call-for-price), publication, sort by name/price/stock/newest, server-side paging (25/50/100). Deep links, back/forward, debounced search; works without JavaScript too. |

### Counting rules

* All counts exclude deleted (`Deleted`) and system products (`IsSystemProduct`); unpublished products are included.
* **Stokta yok**: `ManageStock` with `StockQuantity <= 0`, or `ManageStockByAttributes` without any *active* combination with stock > 0. Products that do not track stock are never out of stock.
* **Gizli fiyatlı**: `CallForPrice = true`. These cannot be added to the cart (`ShoppingCartValidator`).
* **Fiyatsız** (list filter and red badge): `Price <= 0` and not `CallForPrice`. Once published such a product can be bought for 0. Since PR #43 the supplier import marks unpriced products call-for-price; the filter finds products that were imported earlier or edited by hand.

## Phase 2: products, categories, price lock

| Area | State | Notes |
| --- | --- | --- |
| Inline edit (price, sale price, stock) | Done | Click the cell, Enter saves, Esc cancels. Turkish input (`1.250,50`, `9.999`) and `1250.50` are parsed on the server (`AmountParser`); at most 2 decimals. Each save sends the value the user saw; if the stored value changed meanwhile the server answers 409 and shows the current value. |
| Price visibility and publication toggles | Done | Badge buttons per row. |
| Product rules (`ProductRules`) | Done | A published product that is not call-for-price cannot get price 0 (price edit, "show price", publish); sale price must be > 0 and < price; tracked stock 0–1,000,000. Only rules touching the changed field are checked. A refused value is reloaded from the database, so the request-wide SaveChanges filter cannot persist it. |
| Bulk actions | Done | Selection on the page or "all N matching the filter" (max 5,000, saved in batches of 100): price ± percent (rounded to 2 decimals), price ± amount, hide/show price, set stock, track/untrack stock, publish/unpublish, add to/remove from category, delete (recycle bin). Products that would break a rule stay unchanged and are listed with the reason. One activity log entry per run. |
| Product editor (drawer) | Done | General, tags, pricing (incl. sale dates, call for price), inventory, categories, short/full description, SEO (slug via `IUrlService.SaveSlugAsync`), shipping dimensions, publication and visibility. Optimistic check on `UpdatedOnUtc`. Unsaved-changes warning on close and on page leave. Tax category, images, variants and other languages stay in the classic editor (linked). |
| Duplicate / delete | Done | Duplicate uses `IProductCloner` like the classic "copy" and is always created unpublished. Delete is Smartstore's soft delete (recycle bin). |
| Categories | Done | Tree with search, product counts, create/edit (parent, published, order, description, SEO, image upload ≤ 5 MB), move up/down (siblings renumbered), delete with an explanation of products and subcategories. A category cannot become a child of itself or a descendant. Deleting moves direct children to the parent by default (Smartstore alone would move the whole subtree to the root) or deletes the subtree. |
| "Tüm fiyatları gizle" | Done | `MotorPanelSettings.HideAllPrices`. Two decorators, active only while the module is installed: `IPermissionService` denies `Catalog.DisplayPrice` (Smartstore then hides prices and buy buttons everywhere) and `IShoppingCartValidator` refuses shopping cart items (add to cart, cart page, checkout, order placement). Wishlists keep working. Stored prices, orders and invoices are untouched. Confirmation dialog, `Configuration.Setting.Update` permission, activity log entry with who and when. |

Verified locally with the lock on: product page without price and buy button; `POST /cart/addproduct` refused with the lock message; a customer who added a product before the lock is sent from `/checkout` back to the cart, and `PlaceOrder` validates the cart again.

## Phase 3: orders, shipments, customers, invoice status

| Area | State | Notes |
| --- | --- | --- |
| Order list | Done | Search by order number (`#123`, id or GUID) or customer name/e-mail, date range (admin time zone), order/payment/shipping status, server paging, deep links. Store-scoped like the classic list. |
| Order details (drawer) | Done | Customer, billing and shipping address, items with shipped quantities, totals (subtotal, discounts, shipping, payment fee, VAT per rate, rounding, total, refunded), applied discounts, payment method and status, shipments, notes/history, invoice status. |
| Mark as paid | Done | `IOrderProcessingService.MarkOrderAsPaidAsync`, only when Smartstore allows it and **not for iyzico orders** (iyzico confirms its own payments). Meant for bank transfer and cash on delivery. |
| Cancel | Done (restricted) | `CancelOrderAsync` (restocks, e-mails the customer). The panel refuses orders whose money was collected (Paid, PartiallyRefunded, Authorized), because cancelling does not refund; those go through the payment provider and the classic admin. |
| Complete | Done (restricted) | `CompleteOrderAsync`, only for paid orders that are shipped, delivered or need no shipping, so Smartstore never captures or marks an order paid as a side effect. |
| Notes | Done | Order note, optionally shown to the customer with Smartstore's "new order note" e-mail. |
| Shipments | Done | New shipment with per-item quantities (partial shipments: the rest stays for later; Smartstore clamps to what is left), carrier (suggestions from MotorShop.Tracking's carrier list, read as a raw setting so there is no module dependency) and tracking number; the tracking URL is filled by MotorShop.Tracking when installed. Mark shipped / delivered with optional customer e-mail; delivered requires shipped first (Smartstore does not check that). |
| Shipping queue | Done | Orders not (fully) shipped, oldest first, with "new shipment"; shipments in transit with "delivered". |
| Customers | Done | Registered customers, guests with orders, or all; search by name, company, e-mail or billing name; order count, spend (not cancelled, refunds deducted), last order. Details: contact, addresses, roles, statistics, orders. Editing stays in the classic admin. |
| Invoices | Status only | No certified e-Arşiv/e-Fatura integrator is configured, so every order shows "Entegratör yapılandırılmadı". The panel issues no invoices, creates no invoice numbers and links Smartstore's order PDF only as "order summary, not a legal invoice". |
| Refunds, iyzico | Not in the panel | Excluded on purpose: refunds and iyzico payment state stay in the iyzico module and the classic admin. |

Verified locally on order #8 (bank transfer): note, shipment, deliver-before-ship refused, ship, ship again refused, complete-unpaid refused, mark paid, cancel-paid refused, deliver → Smartstore completed the order. Order #7 (unpaid) cancelled, second cancel refused. Not verified: a real partial shipment, because every QA order has quantity 1.

## Phase 5: reports, pages, coupons, settings

| Area | State | Notes |
| --- | --- | --- |
| Reports | Done | 7 / 30 / 90 days, 12 months or a custom range (max. two years), by order date in the admin time zone. **Gross revenue** = order totals of orders whose money was collected (Paid, PartiallyRefunded, Refunded); **refunds** are shown and deducted for **net revenue**; unpaid, failed and voided orders are never revenue and appear separately as "awaiting payment" (cancelled excluded). Also: paid orders, average order, units sold, shipping and VAT inside gross, orders by status (linked to the filtered order list), top 10 products, stock alerts (published, tracked, stock ≤ minimum). Revenue chart: one bar per day (≤ 92 days) or month, hover/focus tooltip, table view. |
| Pages | Done | Topics list with publish toggle; editor for title, short title, intro, body (HTML), SEO and slug. System topics carry a warning (legal texts). |
| Blog | Link | Posts are written in WordPress and shown by MotorShop.WordPress; the panel links to its admin page when installed. |
| Coupons | Done | List with state (active, scheduled, expired, used up), usage and limits. Create/edit: order total, subtotal, shipping, categories or SKUs; percent (0–100] or amount; coupon code (unique) or automatic; validity dates; usage limit total or per customer. End/restart sets or clears the end date (Smartstore has no active flag). Assignments update `HasDiscountsApplied` so Smartstore's price calculator sees them. Discount arithmetic stays in Smartstore. |
| Settings | Done | Store name, company data (name, address, VKN, trade register), contact e-mails, free shipping threshold, panel title and accent. Saved for all stores through `ISettingFactory`; a note appears when store-specific values exist. Currency, tax, payment methods (API keys only in their own modules) and e-mail accounts are read-only with links. There is no field for the tax office (vergi dairesi) because Smartstore has none and reusing another field would mislabel legal documents. |

Verified locally: report figures match the orders (1 paid, 6 awaiting, 1 cancelled); topic save, empty title refused; coupon percent 150 refused, duplicate code refused, category coupon without categories refused, unknown SKU listed, end → expired, restart → active, delete; settings with invalid e-mail, accent or free-shipping amount refused with Turkish messages; all ten panel pages without horizontal overflow at 375 px.

## Phase 6: simple admin for store employees

The panel was simplified for employees without technical knowledge. The dashboard design (header, colors,
cards, recent orders, currency strip, typography, responsive layout) is unchanged; the product list moved from
the dashboard to its own page.

| Area | State | Notes |
| --- | --- | --- |
| Navigation | Done | Main tabs in this order: Genel Bakış, Ürünler, Kategoriler, Siparişler, Kargolar. Müşteriler, Raporlar, Kuponlar, Sayfalar & Blog, Faturalar, Ayarlar and the classic admin are under "Diğer İşlemler" (a `<details>` menu, no JavaScript needed). Same routes and read permissions as before (`MotorPanelHeaderViewComponent.MainTabs/MoreTabs`, covered by `PanelContractTests`). |
| Dashboard | Done | Cards and recent orders as before, plus "Hızlı işlemler" (+ Yeni Ürün Ekle, Tüm ürünler, Taslak ürünler, Kategoriler) and the five newest products. Card links and old deep links (`/motor-panel?stock=OutOfStock`) open the product page with the same filter. |
| Product list (`/motor-panel/products`) | Done | Columns Ürün (photo, name, SKU) · Fiyat · Stok · Kategori · Durum · İşlemler. Actions: Düzenle (simple editor; variant-stock products go to the classic editor), Sitede Gör (also for drafts: admin preview), Yayında/Taslak toggle, "⋯" with advanced editor (drawer), classic editor, copy, delete. Search by name and SKU, category and status filters; stock, price visibility and page size under "Gelişmiş filtreler". Inline price/stock editing, bulk actions, sorting and paging as before. Publishing from the list (toggle or bulk) needs a category. |
| Yeni Ürün Ekle (`/motor-panel/product/new`) | Done | Name*, category* (searchable, full path "Kask › Açık Kask", new category inline when the user may create categories; the same name under the same parent returns the existing category), price* for publishing (1.250,50 · 4500 · 4.500,00), stock (empty = 0), short description, optional SKU (empty: unique `MP-yyMMdd-XXXXX`), up to 10 photos (JPG/PNG/WEBP/GIF, 10 MB each, checked by content, first = main, reorder, remove). "Taslak Kaydet" / "Yayınla" / "İptal", then a confirmation with "Sitede Gör". |
| Simple editor (`/motor-panel/product/{id}`) | Done | Ürün Bilgileri, Satış Bilgileri, Ürün Fotoğrafları (saved at once), Yayın Durumu, "Değişiklikleri Kaydet", "Sitede Gör". "Gelişmiş Ayarlar" shows SKU, MPN, tax class, sale price, price visibility and stock mode read-only and opens the advanced or classic editor. |
| Storefront draft notice | Done | `DraftNoticeWidgetSource` puts "Bu ürün taslak…" above an unpublished product page (zone `productdetails_top`). Only admins reach that page; customers get Smartstore's 404. |

### What the simple screens write

* **Create**: a Smartstore `Product` with the classic "add new product" defaults (simple product, full
  visibility, first product template, default delivery time, stock tracked, no backorders, order quantity 1–50,
  shipping enabled, reviews allowed), the "Genel KDV" tax class (like the DefaultProductTaxCategory migration;
  else the class most products use), one `ProductCategory`, the slug through `IUrlService`, photos through
  `IMediaService` into the catalog album as `ProductMediaFile`s, `MainPictureId` = first photo. Product, category
  mapping, slug and photos are saved in one database transaction; if a photo cannot be stored, nothing is kept.
  A product with price 0 is never published.
* **Simple editor**: only name, short description, price, stock (only when stock is tracked per product),
  published and the **main** category. The main category (first mapping) is replaced; other category mappings
  and their order are kept. SKU, MPN, GTIN, tax class, sale price and dates, compare price, call for price,
  visibility, shipping, dimensions, SEO, full description, stock mode and limits, variants are never written.
  An existing slug is kept when the name changes. Publishing needs a price above 0 (unless call for price) and a
  category.
* **Price visibility** ("Fiyat sitede görünsün", the opposite of call for price): shown in the simple editor and as a switch in the list. Supplier imports leave unpriced products with price 0 and the price hidden; entering a price for such a product (editor or inline list) shows it, the same rule the supplier import uses. A price an admin hid on purpose (stored price above 0) stays hidden until the box is ticked. A visible price must be above 0. If a save hook (CatalogImport sale guard) keeps the price hidden, the editor says so.
* **Concurrency**: the editor sends the version (`UpdatedOnUtc.Ticks`, as a string because it exceeds
  JavaScript's safe integers); a changed product answers 409 "Bu ürün sen düzenlerken başka biri tarafından
  değiştirildi…" and nothing is saved. Photo changes return the new version only when the editor was current.
* **Duplicate submissions**: buttons are disabled while saving and every form carries a random `SubmissionId`;
  the same id (per admin) returns the first product instead of a second one (in memory, one hour).
* **Storefront sync**: everything goes through the tracked `SmartDbContext`, so Smartstore's hooks (audit dates,
  caches, main picture, the CatalogImport sale guard) run as for the classic editor. No admin-only table, no
  client-only state.

### Tests

* `QuickCreateTests`, `SimpleEditTests`, `PhotoTests`, `CategoryServiceTests` run the real services on SQLite in
  memory (transactions, constraints and SQL translation as in production). `PanelContractTests` checks
  permissions, antiforgery, size limits and the navigation.
* `test/MotorShop.AdminPanel.Tests/e2e/simple-admin-e2e.mjs` drives a running store in headless Edge/Chrome:
  login, dashboard, list, new product with photos and quick category, draft hidden from customers, publish,
  price change (form and inline) and category change reaching the storefront, main photo, conflicts, server
  validation, antiforgery, a customer without admin rights, 390 px layouts, console errors. It deletes its test
  data. Never run it against production:

  ```
  node test/MotorShop.AdminPanel.Tests/e2e/simple-admin-e2e.mjs --base http://127.0.0.1:5111 --credentials <file: e-mail, password> [--customer <file>] [--photo <jpg>] [--out <dir>]
  ```

## Security

* Photo uploads: content-checked image formats only, 10 MB per photo, request size limited (`RequestSizeLimit`), saved through `IMediaService`.
* Every panel controller derives from `AdminController`: `[AuthorizeAdmin]` (backend access), `[ValidateAdminIpAddress]`, `[AutoValidateAntiforgeryToken]`. Scripts send the token in the `X-XSRF-Token` header.
* Every action checks the permission the classic admin uses for the same data (product, category, order, shipment, customer, topic, discount, setting, store). `PanelContractTests` fails if a new POST has no permission or opts out of antiforgery, or a GET has no permission (only the dashboard checks per block).
* Orders and shipments are limited to the admin's authorized stores, like the classic lists.
* `X-Robots-Tag: noindex, nofollow`, `Cache-Control: no-store`, `<meta name="robots">`.
* Output is Razor-encoded; only http(s) tracking links are rendered; the accent color is accepted only as `#rrggbb`; category images are limited to image types and 5 MB and stored through `IMediaService`.
* No payment secret is shown or edited. No card data is stored.
* Activity log entries for product edits, bulk runs, duplicates, deletes, categories, coupons, settings, order changes and the price lock.
* Verified locally: anonymous `GET /motor-panel` → 302 to login, anonymous XHR → 401, a signed-in customer without backend access → 302 to login, POST without token → 400, admin → 200.

## Installation

Fresh installs install every module. On an existing database (e.g. Supabase
production) the owner installs **MOTOR Yönetim Paneli** under
Admin → Plugins → Install → "Run tasks"; the app restarts. No migration, no
new table. Uninstall removes only the module's settings and language
resources.

## Audit: what exists and what the panel reuses

| Feature | Exists | Reuse in the panel | Files |
| --- | --- | --- | --- |
| Product CRUD, copy, soft delete | Yes | Inline and drawer edits through the tracked `SmartDbContext` (hooks run, like `ProductController.ProductUpdate`), copy via `IProductCloner`, soft delete via `Deleted` | `Areas/Admin/Controllers/ProductController*.cs`, `Catalog/Products/Services/ProductCloner.cs`, `Common/Hooks/SoftDeletableHook.cs` |
| Stock | Yes | `ProductHook` re-applies low-stock activity and back-in-stock mails on save; grid stock edits need the optimistic check of `CheckStockQuantityUpdate` | `Catalog/Products/Hooks/ProductHook.cs`, `ProductService.AdjustInventoryAsync` |
| Variants | Yes | Stock per `ProductVariantAttributeCombination`; complex matrices stay in the classic editor | `ProductController.Attributes.cs` |
| Categories | Yes | `ICategoryService`; circular parents are silently reset by `CategoryHook`, so the panel must validate first | `Catalog/Categories/Hooks/CategoryHook.cs` |
| Pricing, call for price | Yes | `Price`, `ComparePrice`, `SpecialPrice` + dates, `CallForPrice` | `Catalog/Products/Domain/Product.cs` |
| Hide prices | Partly | `Catalog.DisplayPrice` hides prices in the UI only; **the cart does not check it** (`ShoppingCartValidator` checks `CallForPrice`, not the permission) | `Checkout/Cart/Services/ShoppingCartValidator.cs` |
| Orders, payment, refunds | Yes | `IOrderProcessingService` (`CanRefundAsync`, `RefundAsync`, `PartiallyRefundAsync`, `MarkOrderAsPaidAsync`, `CancelOrderAsync`); never `RefundOfflineAsync` for iyzico orders | `Checkout/Orders/Services/IOrderProcessingService.cs` |
| iyzico | Yes | Refund lock, pending-refund block, reconciliation task | `MotorShop.Iyzico/Services/IyzicoPaymentService.cs` |
| Shipments, partial | Yes | `AddShipmentAsync` with per-item quantities, `ShipAsync`, `DeliverAsync`; tracking URLs from `MotorShop.Tracking` | `Areas/Admin/Controllers/ShipmentController.cs` |
| Customers | Yes | Customer query extensions; guests are hidden by default in the classic list | `Areas/Admin/Controllers/CustomerController.cs` |
| Discounts / coupons | Yes | `IDiscountService`, `Discount` entity | `Areas/Admin/Controllers/DiscountController.cs` |
| Reports | Partly | Dashboard view components and `OrderQueryExtensions` (no report service) | `Areas/Admin/Components/Dashboard*.cs` |
| Topics (pages) | Yes | `_db.Topics` | `Areas/Admin/Controllers/TopicController.cs` |
| Blog | Yes (external) | `MotorShop.WordPress` (PR #44) reads posts from a headless WordPress; content is edited in WordPress, the panel can only link to its configuration | `MotorShop.WordPress/Controllers/MotorWordPressAdminController.cs` |
| Order PDF | Yes | Not a legal invoice | `docs/motor/invoicing.md` |
| e-Arşiv / e-Fatura | No | Provider-neutral design only, no integrator chosen | `docs/motor/invoicing.md` |
| Audit log | Yes | `IActivityLogger` (needs an `ActivityLogType` row per keyword) | `Platform/Logging/Services/IActivityLogger.cs` |

## Remaining work (needs the owner or external services)

1. **e-Arşiv / e-Fatura**: choose a certified integrator; then implement the provider-neutral contract from `docs/motor/invoicing.md` and replace the status page.
2. **Courier API**: only if a carrier with an API is chosen; until then tracking links come from templates.
3. **Refunds in the panel**: deliberately not built (iyzico excluded); refunds stay in the iyzico module.
4. **Preview QA**: run the panel on the preview environment with real data, two devices; Playwright journeys once a stable test environment exists.
5. **Partial shipment with quantity > 1**: not exercised locally (all QA orders have quantity 1).

## Known gaps

* Blog posts live in WordPress (`MotorShop.WordPress`), not in the Smartstore database, so "Pages & Blog" can edit topics but only link to the blog source.
* The QA tenant used for local checks has USD as primary currency and a TRY rate of 1 (sample data). The panel shows what is configured.
