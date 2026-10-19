# MOTOR task registry

Shared registry for all MOTOR workstreams. The orchestrator session keeps it
current; workstream sessions report to the orchestrator, who updates this file.
Facts only: a PR being open is not a finished feature.

Rules: one task = one branch = one worktree = one PR against `preview`.
Only the orchestrator merges into `preview`, one merge at a time.
Nothing reaches `motor` without the owner's explicit approval (Gate F).

Last updated: 2026-10-19

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

GitHub CI on `preview` after #19 and #18: success (runs 39243291820, 39243351579).

## Workstreams

| ID | Agent / session | Scope | Branch(es) | Owns | Depends on | Status | PR |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A | Orchestrator | Integration, merges into preview, registry, QA gate | chore/motor-task-registry | `docs/motor/TASKS.md`, `Smartstore.sln` conflict resolution | — | Ongoing | #12 |
| B | "MOTOR repository Git protocol" (session ended) | VPS compose, Caddy/HTTPS, image/deploy workflows, backup/restore/monitoring, VPS docs | infra/motor-vps-docker, infra/motor-backup-monitoring, docs/motor-vps-operations | `deploy/vps/**`, `.github/workflows/motor-image.yml`, `motor-deploy.yml`, `docs/motor/vps-*.md` | #7, #11 | Merged; follow-ups unowned (see blockers) | #14, #16, #17 |
| C | tr-localization | tr-TR default, TRY, KDV-inclusive prices, Turkish emails, TR addresses, MotorDark text to resources, MotorDark as default theme | feat/tr-localization | Locale resources, MotorDark view text, language/currency/tax/theme settings and seeds | Phase 1 | In progress | — |
| D | payment-iyzico | iyzico Checkout Form module: server-side verification, webhooks, 3DS, idempotency, refund/cancel | feat/payment-iyzico | `src/Smartstore.Modules/MotorShop.Iyzico/**`, its test project | Phase 1; tax/identity decisions from C before finalizing | In progress | — |
| E | fitment-fixes | Pipe in vehicle cookie, invalid year ranges, missing ProductMotorVehicle FKs on PostgreSQL | fix/fitment-cookie-year-range, fix/fitment-postgres-fk | `src/Smartstore.Modules/MotorShop.Fitment/**`, `test/MotorShop.Fitment.Tests/**` | #6, #11 | Done | #13, #19 |
| E-cat | "Motorcycle catalog research and integration" | Staged, validated catalog import; sourced motorcycle reference data (research candidates only) | feat/catalog-import-pipeline, feat/motorcycle-reference-catalog | `src/Smartstore.Modules/MotorShop.CatalogImport/**`, its tests, `docs/motor/catalog-research/**`, CI module check entry | Fitment PRs | In progress | — |
| F | legal-shipping-ops | Distance-sales acknowledgments, provider-neutral shipment tracking, order-ops audit, e-Arşiv boundary | feat/legal-checkout, feat/shipment-tracking, docs/order-ops-invoicing | `src/Smartstore.Modules/MotorShop.Legal/**`, shipment tracking, `docs/motor/invoicing.md` | Phase 1 | Legal checkout merged; shipment tracking and ops/invoicing in progress | #18 |
| G | not started | MotorDark UX, mobile, performance | — | MotorDark CSS/JS/layout | C (shares MotorDark views) | Waiting | — |
| H | Orchestrator | End-to-end QA, release gates | — | — | C, D, E, F | Waiting | — |

## Known blockers and open findings

| Item | State | Needed |
| --- | --- | --- |
| Six Labors licence key | PR #9 ships upstream Smartstore's assembly-scoped community key (`src/Smartstore/sixlabors.lic`, expires 2028-01-07). Release builds fail without a key. | **Owner:** apply for MOTOR's own key at licensing.sixlabors.com and replace the file, or provide it as the `SixLaborsLicenseKey` CI secret/build arg. Blocks Gate F, not preview. |
| ProductMotorVehicle FKs on PostgreSQL | **Fixed** by #19 (root cause applies to every module: constraints must be in the EF mapping) | All module PRs (iyzico, catalog import, legal) were told to declare constraints in the EF mapping |
| Fitment sample vehicles on later module install | The `Initial` migration seeder still adds approximate sample vehicles when the module is installed into an existing store | Low: production is a fresh install without sample data. Optional follow-up: drop the seeder |
| MotorDark not default on fresh install | Found in preview runtime QA | Workstream C |
| F2 SQLitePCLRaw 2.1.11 (CVE-2025-6965) | Open; SQLite provider only | Production uses PostgreSQL; never use SQLite in production. Move to 3.x with upstream |
| F6 DataProtection keys unencrypted at rest | Partly mitigated (#14/#16: root-only volume, GPG-encrypted backups) | Key-ring encryption with a certificate is application work; unowned |
| F8 Containers run as root, HTTP :80 | **Fixed** by #20 (verified: non-root install + runtime + volume upgrade path) | `Nano.Dockerfile` (Windows) unchanged |
| Health check path | **Fixed** by #20: defaults to `/healthz/ready` | — |
| Fresh-clone local build | `Smartstore.Data.*.dll` are not copied to `Smartstore.Web/bin` on the first build of a fresh clone (upstream glob evaluation); a second build of `Smartstore.Web` fixes it | Docker and CI unaffected (publish re-evaluates); local only |
| iyzico sandbox | No credentials | **Owner:** iyzico merchant + sandbox account; set credentials via environment/admin, never in chat |
| Supabase / VPS | No access | **Owner:** Supabase projects (staging + production), VPS, domain |
| Business data | Not available | **Owner:** tax rates, real prices, stock, verified fitment data, carrier, approved legal texts, e-invoice provider |

## Release gates

| Gate | State |
| --- | --- |
| A Infrastructure | CI green on preview; local runtime smoke passed; non-root image verified locally (NoBuild); PostgreSQL install with FKs verified by workstream E on a disposable container; full source-build image and real Supabase/VPS not yet verified |
| B Turkish storefront | Not reached (workstream C) |
| C Payments | Not reached (sandbox blocked on credentials) |
| D Catalog & operations | Not reached |
| E Preview release candidate | Not reached |
| F Production | Requires explicit owner approval |
