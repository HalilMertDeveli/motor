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

## Phase 1 (this module version)

| Area | State | Notes |
| --- | --- | --- |
| Entry route, layout, header, tabs | Done | Light theme, independent of MotorDark CSS. Store name and logo come from the current `Store`; title and accent color from `MotorPanelSettings` (empty = store name / MotorDark accent `#ff5a1f`). |
| Card "Toplam ürün" | Done | Opens the product list. |
| Card "Stokta yok" | Done | Opens the list filtered by `stock=OutOfStock`. |
| Card "Gizli fiyatlı ürünler" | Done | `CallForPrice` products; opens the list filtered by `price=CallForPrice`. Unpublished ≠ hidden price. |
| Card "Fiyat görünürlüğü" | Read-only | Shows Smartstore's native "Display prices" permission for the Guests and Registered roles and links to the customer roles. The global "hide all prices" switch is **not** in this phase, see below. |
| Recent orders (5) | Done | Number links to the classic order editor; "Tüm siparişler" to the classic order list. Store-scoped like the classic dashboard (`ApplyCustomerFilter`). |
| Currency strip | Done | Configured rates only (`Currency.Rate`, `UpdatedOnUtc`). No external rate API. A TRY-only store shows a single-currency notice. |
| Product list | Done (read-only) | Search by name or SKU (Smartstore's LINQ catalog search, same as the classic grid), category, stock, price visibility (incl. **"Fiyatsız"**: price 0 and not call-for-price), publication, sort by name/price/stock/newest, server-side paging (25/50/100). Deep links, back/forward, debounced search; works without JavaScript too. |
| Product actions | Links | "Düzenle" opens the classic product editor, "Mağazada görüntüle" the published product page, "Yeni ürün" the classic create page. |
| Other tabs | Links | Orders, customers, reports (bestsellers), pages, settings, coupons, categories and shipments open the classic admin page; each tab is shown only with the matching read permission and marked with an "opens classic" icon. |

### Counting rules

* All counts exclude deleted (`Deleted`) and system products (`IsSystemProduct`); unpublished products are included.
* **Stokta yok**: `ManageStock` with `StockQuantity <= 0`, or `ManageStockByAttributes` without any *active* combination with stock > 0. Products that do not track stock are never out of stock.
* **Gizli fiyatlı**: `CallForPrice = true`. These cannot be added to the cart (`ShoppingCartValidator`).
* **Fiyatsız** (list filter and red badge): `Price <= 0` and not `CallForPrice`. Once published such a product can be bought for 0; supplier PR #43 turns these into call-for-price.

### Brand settings

There is no settings page yet. Until phase 5, set them under Admin →
Configuration → Settings → All settings: `MotorPanelSettings.PanelTitle`
(empty = store name) and `MotorPanelSettings.AccentColor` (`#rrggbb`, empty =
`#ff5a1f`). The logo is the store logo (Admin → Configuration → Stores).

## Security

* `MotorPanelController` derives from `AdminController`: `[AuthorizeAdmin]` (backend access permission), `[ValidateAdminIpAddress]`, `[AutoValidateAntiforgeryToken]`. Phase 1 has GET actions only.
* Each data block checks its own permission: `Catalog.Product.Read` (cards, list, `/motor-panel/products`), `Order.Read` (recent orders), `Customer.Role.Read` (price visibility card). Tabs check the permission of the target page.
* `X-Robots-Tag: noindex, nofollow`, `Cache-Control: no-store`, `<meta name="robots">`.
* All output is Razor-encoded. The accent color is accepted only as `#rrggbb`.
* Verified locally (SQLite QA tenant): anonymous `GET /motor-panel` → 302 to login, anonymous XHR → 401, a signed-in customer without backend access → 302 to login, admin → 200.

## Installation

Fresh installs install every module. On an existing database (e.g. Supabase
production) the owner installs **MOTOR Yönetim Paneli** under
Admin → Plugins → Install → "Run tasks"; the app restarts. No migration, no
new table. Uninstall removes only the module's settings and language
resources.

## Audit: what exists and what the next phases reuse

| Feature | Exists | Reuse in the panel | Files |
| --- | --- | --- | --- |
| Product CRUD, copy, soft delete | Yes | Classic editor now; later inline edits through `SmartDbContext` + hooks like `ProductController.ProductUpdate`, copy via `IProductCloner`, soft delete via `Deleted` | `Areas/Admin/Controllers/ProductController*.cs`, `Catalog/Products/Services/ProductCloner.cs`, `Common/Hooks/SoftDeletableHook.cs` |
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
| Topics (pages) | Yes | `_db.Topics`; **no blog/news module in this repository** | `Areas/Admin/Controllers/TopicController.cs` |
| Order PDF | Yes | Not a legal invoice | `docs/motor/invoicing.md` |
| e-Arşiv / e-Fatura | No | Provider-neutral design only, no integrator chosen | `docs/motor/invoicing.md` |
| Audit log | Yes | `IActivityLogger` (needs an `ActivityLogType` row per keyword) | `Platform/Logging/Services/IActivityLogger.cs` |

## Next phases (each its own branch and PR to `preview`)

1. **Global price hiding** (card 4 switch): needs a server-side cart/checkout guard
   (decorated `IShoppingCartValidator`) next to the display permission, an
   admin-only confirmation, an activity log entry and a test that a direct
   add-to-cart POST is refused. Kept out of phase 1 because it changes the storefront.
2. **Products and categories**: inline price/stock/visibility edits with
   Turkish number input and decimal parsing on the server, optimistic stock check,
   bulk actions in batches with a partial-failure report and activity log,
   category tree with cycle check.
3. **Orders, payments, shipments, customers**: order drawer over
   `IOrderProcessingService`, iyzico state, partial shipments.
4. **Invoices**: status view and provider-neutral contract; "Entegratör yapılandırılmadı" until the owner picks a certified provider.
5. **Reports, pages, coupons, settings**.
6. **QA**: Playwright journeys on the preview environment.

## Known gaps

* Topics are the only CMS content; a blog module is not part of this repository.
* The QA tenant used for local checks has USD as primary currency and a TRY rate of 1 (sample data). The panel shows what is configured.
