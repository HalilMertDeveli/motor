# MOTOR task registry

Shared registry for all MOTOR workstreams. The orchestrator session keeps it
current; workstream sessions report to the orchestrator, who updates this file.
Facts only: a PR being open is not a finished feature.

Rules: one task = one branch = one worktree = one PR against `preview`.
Only the orchestrator merges into `preview`, one merge at a time.
Nothing reaches `motor` without the owner's explicit approval (Gate F).

Last updated: 2026-10-22

## Phase 1: integration of existing work (done)

Merged into `preview` in dependency order:

| PR | Branch | Content | Merge |
| --- | --- | --- | --- |
| #2 | ci/motor-build-test | CI build + test, upstream publish workflows neutralized | 201290dc1d |
| #3 | chore/upstream-6.4.0.x-sync | Upstream 6.4.0.x fixes | 8ee67afd78 |
| #4 | chore/prod-hardening | MotorShop.Security module, checklist | 75e173b9fc |
| #5 | chore/prod-hardening-followup | Readiness check, production defaults, NuGet patches (stacked on #4) | 3b84316e08 |
| #7 | fix/docker-motordark-license | Docker images from source, Compose without secrets (retargeted motor → preview) | fb14de2d73 |
| #6 | test/fitment-service-tests | MotorShop.Fitment.Tests (stacked on #2); `.sln` conflict with #4 resolved by keeping both projects | 4e212e02de |
| #8 | fix/error-json-info-disclosure | F4/F5: no exception details in AJAX/JSON errors | 93ab5dc3e3 |
| #10 | fix/webapi-odata-error-disclosure | No exception details in Web API OData errors | b9b4db9adb |
| #9 | chore/imagesharp-4 | F1: ImageSharp 4.1.3 / Drawing 3.1.3 | 2cb2dd71a2 |
| #11 | feat/supabase-postgresql | PostgreSQL via `MOTOR_DB_CONNECTION_STRING` | f3bdc88809 |

Pre-merge verification: all ten branches merged in a scratch worktree, then
`dotnet build Smartstore.sln -c Release` (0 errors) and `dotnet test`
(1029 passed, 0 failed, 3 skipped: the known `[Ignore]`d Fitment bug tests).
The final tree is identical to the tested tree except for one `EndProject`
line in `Smartstore.sln`. GitHub CI on `preview` f3bdc88809: success
(run 39238354682).

### Runtime QA of `preview` f3bdc88809 (local, SQLite, sample data, port 5100)

| Check | Result |
| --- | --- |
| Headless install (`/install/install` + `/install/finalize`) | Pass |
| Storefront `/`, `/login`, `/cart`, `/search`, `/sitemap.xml` | 200 |
| `/admin` and `/checkout` as guest | 302 to login (expected) |
| `/healthz` (MotorShop.Security readiness) | 200 |
| Security headers (nosniff, frame-ancestors, X-Frame-Options, Referrer-Policy, Permissions-Policy) | Present |
| Admin login, module list | MotorShop.Fitment and MotorShop.Security installed; `MotorVehicle` and `ProductMotorVehicle` tables exist |
| Thumbnails after ImageSharp 4 (`/media/...?size=256`) | 200, correct dimensions |
| Add to cart (product page, AJAX) and cart totals | Pass via a direct request; one UI click attempt did not add the item, still to be re-checked in E2E QA |
| **Fresh install theme** | **Flex, not MotorDark.** Fix assigned to workstream C (seed + idempotent migration) |

Not verified yet: MotorDark rendering on preview, Fitment selector UI,
checkout to order completion, PostgreSQL install (workstream E), Docker image
of preview.

## Phase 2+: merged into `preview` after Phase 1

| PR | Branch | Content | Merge |
| --- | --- | --- | --- |
| #13 | fix/fitment-cookie-year-range | Per-part encoded vehicle cookie (`v2|`), one [min,max] year rule, admin input validation; `[Ignore]` removed | d065c92ef7 |
| #14 | infra/motor-vps-docker | VPS Compose (Caddy + Supabase), image and manual deploy workflows, F7 upload limit | 9d342812c4 |
| #16 | infra/motor-backup-monitoring | GPG-encrypted backups, isolated restore, monitoring, systemd timers (stacked on #14) | c6aa378849 |
| #17 | docs/motor-vps-operations | VPS setup and operations runbook | 1f500f4959 |
| #15 | fix/drop-amazonpay-bouncycastle | F3: AmazonPay out of the build, Portable.BouncyCastle removed | 191ecc13df |
| #19 | fix/fitment-postgres-fk | Root cause: fresh installs build tables from the EF model and only stamp module migrations; FKs now in the EF mapping, idempotent `MappingIntegrity` migration for existing DBs, sample vehicles only with sample data | 382d4acd28 |
| #18 | feat/legal-checkout | MotorShop.Legal: pre-contractual info + distance sales agreement at confirm, server-side acknowledgment, per-order record with document hashes, placeholder topics ("TASLAK – hukuk onayı gerekli") | b0ab932211 |
| #20 | infra/motor-nonroot-image | F8: non-root `app` user, port 8080, VPS health check `/healthz/ready` | 97f48ce8a7 |

| #22 | feat/shipment-tracking | MotorShop.Tracking: carrier-neutral tracking URLs (https templates only, encoded numbers), Turkish shipment e-mails as localized values (only where empty) | a957396693 |
| #12 | chore/motor-task-registry | This registry | 0884ae85e1 |
| #23 | docs/order-ops-invoicing | Order operations audit (stock at placement, idempotent paid/cancel/refund, QueuedEmail retry) and provider-neutral e-Arşiv/e-Fatura boundary | 1d5d363cd1 |
| #26 | feat/tr-localization | tr-TR language pack, 25 e-mail templates, TRY (1.234,56 ₺), configurable KDV 20/10/1/0 with gross prices, Türkiye + 81 provinces, MotorDark texts as resources, MotorDark default theme, customer theme switcher off; insert-only migration for existing DBs; SQLite NOCASE fixed for tr-TR | f72cd669b2 |
| #27 | fix/legal-confirm-terms-text | Replaces the consent-style Smartstore terms text at the buy buttons with a payment-obligation notice | 8465abf71b |
| #21 | feat/payment-iyzico | MotorShop.Iyzico: Checkout Form (order first, redirect), server-side retrieve + signature verification, webhook HMAC (V3), per-order lock, idempotency, refund/cancel, auto-cancel of unpaid orders; mocks only | ab2589cae1 |
| #28 | fix/migration-version-collision | **Release blocker fix:** #21 and #26 both used migration version 2026-10-19 12:00:00, so every fresh install failed; iyzico moved to 12:30, plus a duplicate-version test | 456053c71b |
| #29 | docs/motor-registry-2026-10-20 | Registry update | 5f042be3e1 |
| #31 | fix/iyzico-hardening | Refund in-flight guard, TRY-only currency default, paidPrice equality/cap, Payments.Manual hidden and deactivated (filter + install + migration), admin nits; merged with a fix for a malformed tr-tr resource | a0fa3385e1 |
| #32 | fix/tr-culture-safety | 57 identifier call sites invariant/ordinal under tr-TR (CSS classes, slugs, permission names, upload filter bypass for .PIF/.MSI/.CGI, media types) + 14 tr-TR tests | c472442887 |
| #24 | feat/catalog-import-pipeline | MotorShop.CatalogImport with review fixes (stale approvals withdrawn, source permission errors, admin XSS, transactional publish, duplicate SKU/MPN/GTIN conflicts, relational rollback test), migration 2026-10-20 09:00 | a9476ca17e |
| #25 | feat/motorcycle-reference-catalog | Sourced reference data; research datasets only with `-p:IncludeCatalogResearch=true`, per-vehicle sources | 9294a45c0c |
| #33 | feat/motordark-ux-performance | Sticky mobile buy bar, responsive WebP hero, a11y and performance (home mobile perf 57→66, product CLS 0.187→0) | e0f49fff16 |
| #30 + #34 | feat/motosev-catalog-import, fix/supplier-catalog-rules | MotorShop.SupplierCatalog (Motosev package importer) made to follow the rules: nothing published automatically, only own-supplier products updated, zip limits, HTML sanitized, KDV tax category | 84071a4643 |
| #35 | feat/supplier-image-quality | Storefront picture sizes 512/1024 px, quality 90; `tools/supplier-images` rebuilds supplier pictures from the PDF originals (genuine pixels, per-page pairing) | 7804f02e4e |
| #36 | feat/supplier-images-ai | Optional AI upscaling of supplier pictures with a review list (owner-approved exception) | 9b1c08add1 |
| #37 | feat/prosev-rebrand-payment | Prosev brand, 2026 catalog categories, catalog-style design, dealer payment terms | c0aef97622 |
| #38 | feat/helmet-type-tiles | Categories without a picture show their first product's photo; helmet types as five photo tiles | edfba6ddf7 |
| #40 | feat/official-product-images | `official-images.mjs`: official photos from motosev.com and shad.es instead of PDF crops (691 matched, 109 uncertain, 348 not found) | 251fb58d90 |
| #42 | chore/deploy-supabase-hosting | Fly.io hosting with Supabase PostgreSQL (runbook `docs/motor/fly.md`), Oracle ARM path | c98d52adb0 |
| #43 | fix/supplier-unpriced-call-for-price | Unpriced supplier products become "Fiyat için arayın", not 0,00 ₺ | 523aaff756 |
| #44 | feat/wordpress-headless-cms | MotorShop.WordPress headless blog module | 077542ef11 |
| #45, #49, #50, #51 | feature/motor-premium-admin-foundation, feature/motor-panel-products, -orders, -reports | MotorShop.AdminPanel at `/motor-panel`: dashboard, products/categories/price lock, orders/shipments/customers/invoice status, reports/pages/coupons/settings (one-time module install per store) | a12e2901f4, fcb7c7db1f, 163e594bc1, 52354f2d7f |
| #46 | fix/iyzico-admin-antiforgery | iyzico admin controller derives from `AdminController` (IP check, antiforgery), per-action permissions | cc971d55f3 |
| #54 | feat/google-customer-auth | Google customer sign-in and registration; explicit account linking (no e-mail auto-link), Registered role only | a76e1a2303 |
| #56, #57 | feat/admin-prosev-branding, feat/admin-turkish-complete | Prosev look and name for the admin area; fully Turkish admin (country and system names) | a786fa52d8, 1aae7bb4ce |

Promotions to `motor`, each with the owner's explicit approval: #47 (010f934ea9), #48 (24bbd151cc), #52 (d1420d73d8), #53 (a9bd7b8f2f), #55 (56625ac5d1), #58 (558515f755). Since 2026-10-22 `motor` = `preview` = 558515f755. #41 (Fly.io dashboard files) went straight to `motor`; `fly.toml` exists only there.

GitHub CI on `preview` after #19 and #18: success (runs 39243291820, 39243351579). After #22 and #23: success. **Since 2026-10-20 GitHub Actions jobs do not start** (account billing: "recent account payments have failed or your spending limit needs to be increased"). #26, #27 and #21 had green PR checks before merge; #28 was merged on local evidence only.

### Verification of `preview` on 2026-10-20 (local, CI blocked)

- `ab2589cae1`: Release build 0 errors; tests Core 298, Smartstore 697, Web 34, Fitment 81, Iyzico 85, Legal 20, Tracking 15: all 1230 passed, 0 failed, 0 skipped.
- Fresh install on `ab2589cae1` failed (migration version collision), fixed by #28.
- Fresh install with #28 (SQLite, `PrimaryLanguage=tr-TR`, sample data): success; `<html lang="tr">`, MotorDark active, no theme switcher, prices like `969,00 ₺`; cart 2 × 29,95 ₺ = 59,90 ₺ with KDV 5,45 ₺ (10 % extracted from the gross price); `/healthz` and `/healthz/ready` 200.
- Legal checkout (2026-10-19, upgraded store): confirm without acknowledgment blocked server-side; an order with acknowledgment stores the record and document snapshots.

## Workstreams

| ID | Agent / session | Scope | Branch(es) | Owns | Depends on | Status | PR |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A | Orchestrator | Integration, merges into preview, registry, QA gate | chore/motor-task-registry | `docs/motor/TASKS.md`, `Smartstore.sln` conflict resolution | — | Ongoing | #12 |
| B | "MOTOR repository Git protocol" (session ended) | VPS compose, Caddy/HTTPS, image/deploy workflows, backup/restore/monitoring, VPS docs | infra/motor-vps-docker, infra/motor-backup-monitoring, docs/motor-vps-operations | `deploy/vps/**`, `.github/workflows/motor-image.yml`, `motor-deploy.yml`, `docs/motor/vps-*.md` | #7, #11 | Merged; follow-ups unowned (see blockers) | #14, #16, #17 |
| C | tr-localization (session hit its limit) | tr-TR default, TRY, KDV, Turkish e-mails, TR addresses, MotorDark texts, default theme | feat/tr-localization | Locale resources, MotorDark view text, language/currency/tax/theme settings and seeds | Phase 1 | Merged | #26 |
| D | payment-iyzico | iyzico Checkout Form module | feat/payment-iyzico | `src/Smartstore.Modules/MotorShop.Iyzico/**`, its tests | Phase 1 | Merged; review items 3–5 and Payments.Manual moved to D2 | #21 |
| D2 | iyzico-hardening | iyzico review items 3-5, Payments.Manual off | fix/iyzico-hardening | MotorShop.Iyzico | #21, #28 | Merged | #31 |
| C2 | tr-culture-audit | Turkish-I casing on identifiers under tr-TR | fix/tr-culture-safety | Targeted call sites + tests | #26 | Merged | #32 |
| E | fitment-fixes | Pipe in vehicle cookie, invalid year ranges, missing ProductMotorVehicle FKs on PostgreSQL | fix/fitment-cookie-year-range, fix/fitment-postgres-fk | `src/Smartstore.Modules/MotorShop.Fitment/**`, `test/MotorShop.Fitment.Tests/**` | #6, #11 | Done | #13, #19 |
| E-cat | catalog sessions | Staged catalog import; sourced motorcycle reference data | feat/catalog-import-pipeline, feat/motorcycle-reference-catalog | `MotorShop.CatalogImport/**`, `docs/motor/catalog-research/**` | Fitment PRs | Merged after review fixes | #24, #25 |
| F | legal-shipping-ops | Distance-sales acknowledgments, shipment tracking, order-ops audit, e-Arşiv boundary | feat/legal-checkout, feat/shipment-tracking, docs/order-ops-invoicing, fix/legal-confirm-terms-text | `MotorShop.Legal/**`, `MotorShop.Tracking/**`, `docs/motor/invoicing.md`, `docs/motor/order-operations.md` | Phase 1 | Done | #18, #22, #23, #27 |
| G | motordark-ux | MotorDark UX, mobile, accessibility, performance | feat/motordark-ux-performance | MotorDark theme files | #26 | Merged | #33 |
| H | Orchestrator | End-to-end QA, release gates | — | — | D2, G, C2, E-cat | Partial: Turkish fresh-install smoke and cart KDV done; full journey with iyzico mock, shipment and mobile pending | — |
| I | Motor Premium admin panel | `/motor-panel` admin (phases 1-5) | feature/motor-premium-admin-foundation, feature/motor-panel-* | `src/Smartstore.Modules/MotorShop.AdminPanel/**` | #37 | Merged; write actions not yet tested on the live demo | #45, #49, #50, #51 |
| J | Google giriş ve kayıt | Google customer login/registration and account linking | feat/google-customer-auth | Identity external login flow, Google.Auth configure page | — | Merged; a real Google sign-in waits for the owner's redirect URI update | #54 |
| K | Admin Prosev look | Admin branding and complete Turkish admin | feat/admin-prosev-branding, feat/admin-turkish-complete | Admin theme, admin resources | #54 | Merged | #56, #57 |
| L | Official photos | Supplier/manufacturer photos (motosev.com, shad.es) | feat/official-product-images | `tools/supplier-images/official-images.mjs` | #35 | Merged; the uncertain photos are being removed (owner decision 2026-10-22, session "Belirsiz ürün fotoğraflarını kaldır") | #40 |
| M | canlı / deploy | Live demo on the owner's PC via Cloudflare quick tunnel; Fly.io + Supabase path | chore/deploy-supabase-hosting | `deploy/**`, `docs/motor/fly.md`, `docs/motor/oracle.md` | — | Demo runs `motor` 558515f755; the URL changes on every restart. Permanent address in progress (session "Kalıcı adres") | #42 |
| N | catalog-fixes | Store installed against Supabase prosev-prod and catalog import there | — | Supabase prosev-prod | #42 | In progress: Data API exposure checks 7/7 passed, import running | — |

## Known blockers and open findings

| Item | State | Needed |
| --- | --- | --- |
| Six Labors licence key | PR #9 ships upstream Smartstore's assembly-scoped community key (`src/Smartstore/sixlabors.lic`, expires 2028-01-07). Release builds fail without a key. | **Owner:** apply for MOTOR's own key at licensing.sixlabors.com and replace the file, or provide it as the `SixLaborsLicenseKey` CI secret/build arg. Blocks Gate F, not preview. |
| ProductMotorVehicle FKs on PostgreSQL | **Fixed** by #19 (root cause applies to every module: constraints must be in the EF mapping) | All module PRs (iyzico, catalog import, legal) were told to declare constraints in the EF mapping |
| Fitment sample vehicles on later module install | The `Initial` migration seeder still adds approximate sample vehicles when the module is installed into an existing store | Low: production is a fresh install without sample data. Optional follow-up: drop the seeder |
| MotorDark not default on fresh install | **Fixed** by #26 (verified on a fresh tr-TR install) | — |
| GitHub Actions billing | **Resolved** 2026-10-21: the repository is public (gitleaks clean), CI runs again on PRs to preview/motor | Stacked PRs that target feature branches get no CI; run `motor-ci.yml` via workflow_dispatch on their branch |
| Migration version collisions | Fixed by #28 for iyzico; #24 has the same collision | `Smartstore.Core.Tests/Data/MigrationVersionTests` now fails on duplicates |
| Smartstore "Credit card (manual)" (Payments.Manual) | **Fixed** by #31 (hidden by filter, removed from active methods on install and by migration; verified on a fresh install) | — |
| Turkish-I casing under tr-TR | **Fixed** by #32 for identifiers; display text intentionally culture-aware | Open notes: the SEO character map turns Ü into "ue" (German) |
| Catalog research datasets (#25) | Excluded from every build unless `-p:IncludeCatalogResearch=true` | **Owner (legal):** may they ship; may Yamaha facts be used |
| Motosev product pictures | Official photos from motosev.com and shad.es (#40); the owner confirmed Motosev's permission for the brand photos on 2026-10-22. Uncertain matches are being removed | — |
| Live demo address | Cloudflare quick tunnel from the owner's PC; the hostname changes on every restart and breaks the Google redirect URI | Permanent address (named tunnel on the owner's domain, or Fly.io with a card) in progress |
| **AI upscaling exception** | Owner decision 2026-10-20: Motosev 2026 catalog photos may be enlarged with Real-ESRGAN (no generated photos); pictures with text or strong change are listed for review before publishing | See `docs/motor/product-images.md` |
| F2 SQLitePCLRaw 2.1.11 (CVE-2025-6965) | Open; SQLite provider only | Production uses PostgreSQL; never use SQLite in production. Move to 3.x with upstream |
| F6 DataProtection keys unencrypted at rest | Partly mitigated (#14/#16: root-only volume, GPG-encrypted backups) | Key-ring encryption with a certificate is application work; unowned |
| F8 Containers run as root, HTTP :80 | **Fixed** by #20 (verified: non-root install + runtime + volume upgrade path) | `Nano.Dockerfile` (Windows) unchanged |
| Health check path | **Fixed** by #20: defaults to `/healthz/ready` | — |
| Fresh-clone local build | `Smartstore.Data.*.dll` are not copied to `Smartstore.Web/bin` on the first build of a fresh clone (upstream glob evaluation); a second build of `Smartstore.Web` fixes it | Docker and CI unaffected (publish re-evaluates); local only |
| iyzico sandbox | No credentials | **Owner:** iyzico merchant + sandbox account; set credentials via environment/admin, never in chat |
| Supabase / VPS | Supabase `prosev-prod` exists and the store is being installed there; Fly app `prosev` prepared, blocked until a card is added | **Owner:** card for Fly.io, or a domain for a named tunnel |
| Business data | Not available | **Owner:** tax rates, real prices, stock, verified fitment data, carrier, approved legal texts, e-invoice provider |

## Release gates

| Gate | State |
| --- | --- |
| A Infrastructure | CI green on preview; local runtime smoke passed; non-root image verified locally (NoBuild); PostgreSQL install with FKs verified by workstream E on a disposable container; full source-build image and real Supabase/VPS not yet verified |
| B Turkish storefront | Mostly met on local SQLite: Turkish storefront, TRY, KDV totals, Turkish e-mail templates present. Open: Turkish-I audit (C2), real SMTP delivery, PostgreSQL run |
| C Payments | Not reached: module and hardening merged with mocks; real sandbox blocked on credentials |
| D Catalog & operations | Partial: Fitment constraints fixed, order operations audited, shipment tracking, catalog import and supplier import merged; Motosev catalog imported unpublished in QA; real prices/stock policy and publishing review pending |
| E Preview release candidate | Not reached |
| F Production | Owner-approved promotions to `motor` done (#47 … #58). The live demo runs `motor` 558515f755 on the owner's PC; a permanent host is still missing |
