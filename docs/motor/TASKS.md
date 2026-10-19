# MOTOR task registry

Shared registry for all MOTOR workstreams. The orchestrator session keeps it
current; workstream sessions report to the orchestrator, who updates this file.
Facts only: a PR being open is not a finished feature.

Rules: one task = one branch = one worktree = one PR against `preview`.
Only the orchestrator merges into `preview`, one merge at a time.
Nothing reaches `motor` without the owner's explicit approval (Gate F).

Last updated: 2026-10-19

## Phase 1: integration of existing work (done)

Merged into `preview` in dependency order (`preview` = f3bdc88809):

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
The final `preview` tree is identical to the tested tree except for one
`EndProject` line in `Smartstore.sln`.

## Active workstreams

| ID | Agent / session | Scope | Branch(es) | Owns | Depends on | Status | PR |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A | Orchestrator | Integration, merges into preview, registry, QA gate | chore/motor-task-registry | `docs/motor/TASKS.md`, `Smartstore.sln` conflict resolution | — | Phase 1 done | — |
| B | "MOTOR repository Git protocol" | VPS compose, Caddy/HTTPS, image/deploy workflows, backup/restore/monitoring, VPS docs | infra/motor-vps-docker, infra/motor-backup-monitoring, docs/motor-vps-operations | `deploy/vps/**`, `.github/workflows/motor-image.yml`, `motor-deploy.yml`, `docs/motor/vps-*.md` | #7, #11 | In progress | — |
| C | tr-localization | tr-TR default, TRY, KDV-inclusive prices, Turkish emails, TR addresses, MotorDark text to resources | feat/tr-localization | Locale resources, MotorDark view text, language/currency/tax settings and seeds | Phase 1 | In progress | — |
| D | payment-iyzico | iyzico Checkout Form module: server-side verification, webhooks, 3DS, idempotency, refund/cancel | feat/payment-iyzico | `src/Smartstore.Modules/MotorShop.Iyzico/**`, its test project | Phase 1; tax/identity decisions from C before finalizing | In progress | — |
| E | fitment-fixes | Pipe in vehicle cookie, invalid year ranges, missing ProductMotorVehicle FKs on PostgreSQL | fix/fitment-cookie-year-range, fix/fitment-postgres-fk | `src/Smartstore.Modules/MotorShop.Fitment/**`, `test/MotorShop.Fitment.Tests/**` | #6, #11 | In progress | — |
| F | legal-shipping-ops | Distance-sales acknowledgments, provider-neutral shipment tracking, order-ops audit, e-Arşiv boundary | feat/legal-checkout, feat/shipment-tracking, docs/order-ops-invoicing | Topics/checkout settings, shipment tracking, `docs/motor/invoicing.md` | Phase 1 | In progress | — |
| G | not started | MotorDark UX, mobile, performance | — | MotorDark CSS/JS/layout | C (shares MotorDark views) | Waiting | — |
| H | Orchestrator | End-to-end QA, release gates | — | — | C, D, E, F | Waiting | — |

## Known blockers and open findings

| Item | State | Needed |
| --- | --- | --- |
| Six Labors licence key | PR #9 ships upstream Smartstore's assembly-scoped community key (`src/Smartstore/sixlabors.lic`, expires 2028-01-07). Release builds fail without a key. | **Owner:** apply for MOTOR's own key at licensing.sixlabors.com and replace the file, or provide it as the `SixLaborsLicenseKey` CI secret/build arg. Blocks Gate F, not preview. |
| ProductMotorVehicle FKs on PostgreSQL | Missing in a PostgreSQL install (finding from #11) | Workstream E |
| Fitment cookie pipe / year range | Pinned by `[Ignore]`d tests | Workstream E |
| F3 BouncyCastle 1.9.0 via AmazonPay | Open | Drop AmazonPay from the MOTOR build (not offered in Turkey) |
| F6 DataProtection keys unencrypted at rest | Open | Workstream B (deployment) |
| F7 `MaxRequestBodySize` 2 GB | Open | Workstream B (proxy limit + production setting) |
| F8 Containers run as root, HTTP only | Open (Dockerfiles have no `USER`) | Workstream B |
| iyzico sandbox | No credentials | **Owner:** iyzico merchant + sandbox account; set credentials via environment/admin, never in chat |
| Supabase / VPS | No access | **Owner:** Supabase projects (staging + production), VPS, domain |
| Business data | Not available | **Owner:** tax rates, real prices, stock, verified fitment data, carrier, approved legal texts, e-invoice provider |

## Release gates

| Gate | State |
| --- | --- |
| A Infrastructure | CI and integration done; Docker/PostgreSQL runtime on preview and real Supabase/VPS not yet verified |
| B Turkish storefront | Not reached |
| C Payments | Not reached (sandbox blocked on credentials) |
| D Catalog & operations | Not reached |
| E Preview release candidate | Not reached |
| F Production | Requires explicit owner approval |
