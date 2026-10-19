# MOTOR security operations

Production hardening reference for the MOTOR store (Smartstore 6.4, ASP.NET Core 10).
It complements [production-checklist.md](production-checklist.md). No credentials belong in
this file or anywhere else in the repository.

## 1. Where configuration comes from

Later sources override earlier ones:

| # | Source | In git | Use for |
|---|--------|--------|---------|
| 1 | `appsettings.json` | yes | Smartstore defaults |
| 2 | `appsettings.Production.json` | yes | MOTOR production defaults, no host or secret values |
| 3 | Environment variables (`Smartstore__ReverseProxy__Enabled=true`, `AllowedHosts=...`) | no | Container / service settings |
| 4 | `Config/connections.json`, `Config/connections.Production.json` | no (ignored) | Connection strings |
| 5 | `Config/usersettings.json`, `Config/usersettings.Production.json` | no (ignored) | Host names, proxy networks, overrides |

`Config/*.json` is loaded after the environment variables, so a value in a `Config` file wins
over the same value set in Docker or IIS. The installer also writes the tenant connection
string to `App_Data/Tenants/<tenant>/Settings.txt`.

`ASPNETCORE_ENVIRONMENT` defaults to `Production` when unset. Never set it to `Development`
on a public host.

Example server file: [config/usersettings.Production.example.json](config/usersettings.Production.example.json).

## 2. Production settings

### Hosts and reverse proxy

- `AllowedHosts`: the store domains, separated by `;`. The default `*` accepts any `Host`
  header (cache poisoning, password-reset links with a foreign host).
- `Smartstore:ReverseProxy`:
  - `KnownProxies` / `KnownNetworks`: the proxy that terminates TLS. Without them only
    loopback proxies are trusted. Behind Docker, nginx, IIS ARR or a cloud load balancer the
    client IP and the HTTPS scheme are then wrong. Never use `0.0.0.0/0`: any client could
    fake its IP and scheme.
  - `AllowedHosts`: the values accepted from `X-Forwarded-Host`.
  - `ForwardLimit`: the number of proxies in front of the app (1 for a single nginx).
- The proxy must overwrite, not append to, `X-Forwarded-For`, `X-Forwarded-Proto` and
  `X-Forwarded-Host` that come from clients.

MotorShop.Security logs a warning at start-up for each of these that is missing
(Admin > System > Log, source `MotorShop.Security.ProductionConfigurationCheck`).

### HTTPS and HSTS

- Terminate TLS at the proxy. The app container listens on HTTP only.
- Admin > Configuration > Stores: enable SSL and set the HTTPS store URL. Smartstore then
  redirects GET requests to HTTPS (`HttpsUrlFilter`). This needs a correct
  `X-Forwarded-Proto` from a trusted proxy, otherwise the redirect loops.
- Outside `Development` the app sends `Strict-Transport-Security: max-age=31536000; includeSubDomains`
  on HTTPS responses (`Smartstore:HstsMaxAge`, days). `includeSubDomains` covers every
  subdomain of the store domain. Check that all of them serve HTTPS before go-live.
  The localhost host never gets the header.
- Deploy outside the source tree. If `Smartstore.sln` is found above the content root, or a
  debugger is attached, Smartstore treats the process as a dev environment. HTTPS
  redirection, canonical-host redirection and minification are then switched off. The start-up
  check warns about this.

### Error handling

`appsettings.Production.json` sets `UseDeveloperExceptionPage: false` and
`ShowCustomErrors: false`. Visitors see the generic error page. Exception details go
only to the log.

Remaining: AJAX requests that fail return JSON with the exception message
(`ErrorController`, upstream behaviour). See section 9.

### Razor runtime compilation

`Smartstore:EnableRazorRuntimeCompilation` must stay `true`. Theme views (Flex and MotorDark)
are not precompiled (`Themes\**` is excluded in `Smartstore.Web.csproj`) and are compiled
from disk at runtime. The consequence: any `.cshtml` the app identity can write becomes
executable code. Keep `Views/`, `Areas/`, `Themes/` and `Modules/` read-only for the app
identity (see section 4).

## 3. Security headers

| Header | Set by | Value / setting |
|--------|--------|-----------------|
| `X-Content-Type-Options` | MotorShop.Security | `nosniff` |
| `Referrer-Policy` | MotorShop.Security | `MotorShop:Security:ReferrerPolicy`, default `strict-origin-when-cross-origin` |
| `Permissions-Policy` | MotorShop.Security | `MotorShop:Security:PermissionsPolicy`, default turns camera, microphone, geolocation, USB and motion sensors off |
| `Content-Security-Policy` | Smartstore | `Smartstore:ContentSecurityPolicy`: `frame-ancestors 'self'` (clickjacking), optional `upgrade-insecure-requests` |
| `Content-Security-Policy-Report-Only` | MotorShop.Security | `MotorShop:Security:ContentSecurityPolicyReportOnly`, off by default |
| `Strict-Transport-Security` | ASP.NET Core HSTS | see section 2 |
| `X-Powered-By`, `Server` | removed | MotorShop.Security; the `generator` meta tag no longer carries the version either |

The enforced CSP deliberately has no `script-src` / `default-src`. Smartstore, its themes,
the admin area and payment modules use inline scripts and `eval` (Vue, Select2, widget
injection). A strict policy breaks checkout and admin. Introduce one in steps:

1. Set `ContentSecurityPolicyReportOnly` (a starter policy is in the example config) and
   browse the storefront, checkout, my-account and admin. Collect the console violations.
2. When iyzico is added, include the origins its documentation lists for script, frame and
   connect.
3. Move to enforcement only after a full checkout passes without violations.

`Permissions-Policy` does not mention `payment`. If a future provider needs the Payment
Request API, keep it that way, or add `payment=(self "https://provider.example")`.

## 4. Secrets and file system

### What is secret

| Secret | Location | Notes |
|--------|----------|-------|
| Database connection string | `App_Data/Tenants/<tenant>/Settings.txt` or `Config/connections*.json` | plaintext |
| DataProtection key ring | `App_Data/Tenants/<tenant>/DataProtection-Keys/*.xml` | plaintext XML, see section 5 |
| SMTP passwords, API keys of modules | Database `Setting` table | plaintext in the DB |
| Admin accounts | Database | hashed passwords |

### Rules

- Nothing above goes into git. `.gitignore` excludes `App_Data/*` (except shipped samples),
  `Config/usersettings*`, `Config/connections*`, `DataProtection-Keys/` and `*.pfx`.
  `Dockerfile.dockerignore` excludes `App_Data`, `Config/usersettings*`, `Config/connections*` and
  `DataProtection-Keys` from images.
- Inject the connection string at deploy time: a mounted `Config/connections.Production.json`,
  or the tenant `Settings.txt` on a persistent volume. Not baked into an image.
- Never print secrets in CI logs, chat or tickets. Rotate any secret that leaked
  (section 6).

### File permissions

Run the app as a dedicated, non-admin identity:

| Path | App identity |
|------|--------------|
| `App_Data/` (tenants, logs, keys, media) | read/write |
| `Modules/` | read; write only while installing modules through the admin |
| `wwwroot/`, `Views/`, `Areas/`, `Themes/`, `*.dll`, `appsettings*.json` | read only |
| `Config/` | read only |

Linux (container or VM), with `motor` being the service user:

```bash
chown -R root:motor /app && chmod -R o-rwx /app
chown -R motor:motor /app/App_Data && chmod -R 750 /app/App_Data
find /app/App_Data/Tenants -name Settings.txt -exec chmod 640 {} \;
```

Windows / IIS (application pool identity `IIS AppPool\MOTOR`):

```bat
icacls C:\sites\motor /inheritance:r /grant:r "Administrators:(OI)(CI)F" "IIS AppPool\MOTOR:(OI)(CI)RX"
icacls C:\sites\motor\App_Data /grant:r "IIS AppPool\MOTOR:(OI)(CI)M"
```

### Logging

- EF Core sensitive-data logging is off (`DbFactoryDbContextOptionsExtensions`). Do not enable
  it in production. It writes SQL parameter values (customer data, tokens) to the log.
- Request logging records path, status, user id and user name, never request bodies. Keep it
  that way. Never log card data, payment tokens or provider API responses in full.
- Logs live in `App_Data/Logs` and the `Log` table. Both contain personal data (IP, user
  name). Restrict access and set a retention period (Admin > System > Scheduled tasks >
  "Delete logs").

## 5. DataProtection keys

ASP.NET Core DataProtection protects authentication cookies, antiforgery tokens, TempData and
any data a module protects with `IDataProtector`.

- Location: `App_Data/Tenants/<tenant>/DataProtection-Keys/` (dev: `App_Data/DataProtection-Keys`).
- At rest the keys are **not encrypted** (`NullXmlEncryptor` in `WebStarter`). Whoever can read
  the folder can forge login cookies. Apply the permissions from section 4.
- Keys roll over automatically every 90 days. Old keys stay in the folder to decrypt existing
  data. **Never delete key files.**
- Back up the folder with the database. They belong together: restoring one without the other
  signs every user out and makes protected values unreadable.
- Docker: the folder must be on a persistent volume (`App_Data`). Otherwise every container
  restart creates a new key ring.
- More than one app instance: all instances need the same key ring (shared volume) and the
  same `Smartstore:ApplicationName`.
- Optional, later: encrypt the key ring at rest with a certificate
  (`ProtectKeysWithCertificate`) or a cloud KMS. This needs a code change in `WebStarter`
  (it forces `NullXmlEncryptor`). A key ring encrypted with a lost certificate cannot be
  recovered, so plan certificate backup first.

## 6. Credential rotation

| Credential | Procedure |
|------------|-----------|
| Database password | 1. Create the new password / login on the DB server. 2. Update `Settings.txt` or `Config/connections.Production.json`. 3. Restart the app; check `/healthz/ready` = `Healthy`. 4. Remove the old password. |
| DataProtection key ring (suspected leak) | 1. Back up the folder. 2. Stop the app; move the old key files out. 3. Start the app (new key). 4. Every user is signed out. Values protected with the old keys are lost, so re-enter module secrets that were stored protected. |
| SMTP / module API keys | Generate a new key at the provider, enter it in the admin, test (send a test mail), revoke the old key. |
| Admin accounts | Unique account per person, strong password, 2FA where available. Remove accounts of people who leave. Review Admin > Customers > role "Administrators" quarterly. |
| Payment provider keys (future) | Sandbox and live keys are separate. Rotate live keys at least yearly and immediately after staff changes or a suspected leak: create the new key at the provider, deploy it, run one live-mode verification transaction, then revoke the old key. |

## 7. Future iyzico integration: handling credentials

Not implemented yet. Guidance for the module:

- **Hosted checkout.** Use iyzico's hosted checkout form / payment page so that card data never
  touches MOTOR servers. That keeps the PCI DSS scope at SAQ A.
- **Storing API key and secret key.** Smartstore settings are stored in plaintext in the `Setting`
  table. Pick one of:
  1. Protect the values before they are saved: `IDataProtectionProvider.CreateProtector("MotorShop.Iyzico.Credentials")`.
     Store only the protected string in the setting and unprotect it at call time. The value is
     then only as safe as the DataProtection key ring (section 5), and it cannot be read after
     the key ring is lost. Document the re-entry step.
  2. Or keep the secret out of the database: `MotorShop:Iyzico:SecretKey` from an environment
     variable / `Config/usersettings.Production.json` / a secret store, bound via options.
     The admin then shows only "configured: yes/no".
- Never echo the secret back into the admin form (write-only field). Never log it, and never log
  request or response bodies that contain tokens.
- Separate sandbox and live settings with an explicit "live mode" switch. The live keys exist only
  on the production server.
- Verify the signature of every callback / webhook and re-query the payment status from iyzico
  before marking an order paid. Never trust the redirect parameters alone.
- Extend the CSP report-only policy with the iyzico origins before enforcing anything (section 3).
- No live payment tests from development machines.

## 8. Health checks and always-on operation

Provided by MotorShop.Security (module must be installed):

| Endpoint | Meaning | Response |
|----------|---------|----------|
| `GET/HEAD /healthz` | Liveness: the process serves requests. No dependency checked. | `200 Healthy` |
| `GET/HEAD /healthz/ready` | Readiness: database accepts connections (5 s limit) and the task scheduler is active. | `200 Healthy`; `200 Degraded` while the task scheduler is not active yet; `503 Unhealthy` when the database is unreachable |

The body is only the status word. Check names, errors and connection details are written to the
log, never to the response. Paths and the timeout can be changed under `MotorShop:Security`.

- Liveness probe → `/healthz` (restart the container when it fails).
- Readiness / load-balancer probe → `/healthz/ready` (take the instance out of rotation on 503).
- Docker: `HEALTHCHECK CMD curl -fsS http://localhost/healthz/ready || exit 1` (needs curl in
  the image; the Docker setup decides).

Background jobs: the Smartstore task scheduler is activated by the first HTTP request after
start-up and then calls the app itself over HTTP. Consequences:

- Something must send that first request after every start: the readiness probe does it. Until then
  `/healthz/ready` reports `Degraded`.
- The process must keep running: IIS application pool `startMode=AlwaysRunning`, idle time-out `0`,
  site `preloadEnabled=true`; containers with `restart: unless-stopped`.
- The app must reach its own base URL. Set `Smartstore:TaskSchedulerBaseUrl` explicitly when the
  auto-detected URL (from the first request) is not reachable from inside the server/container.

## 9. Open findings (follow-ups)

| # | Finding | Risk | Proposed remediation |
|---|---------|------|----------------------|
| F1 | `SixLabors.ImageSharp` 3.1.12: CVE-2026-106113/-106110/-106115 (high), -106114/-106116 (moderate). Fixed only in 4.1.2 (major version). | High CVEs are in code MOTOR does not call (HistogramEqualization, TIFF CCITT encoder). The decoder issues (ICC profile, BigTIFF) are reachable through image uploads → memory/CPU DoS. | Separate branch: upgrade ImageSharp 4.x + ImageSharp.Drawing and re-test media/thumbnails/PDF. Check the Six Labors licence terms for 4.x. Until then: only trusted admins upload media; keep customer avatar and file-upload attributes off. |
| F2 | `SQLitePCLRaw.lib.e_sqlite3` 2.1.11: CVE-2025-6965 (bundled SQLite < 3.50.2). No 2.1.x fix. | Only relevant with the SQLite provider. MOTOR production uses SQL Server. | Do not use SQLite in production. Move to SQLitePCLRaw 3.x together with upstream. |
| F3 | `Portable.BouncyCastle` 1.9.0 (unmaintained since 2021, superseded by `BouncyCastle.Cryptography`). Referenced by `Smartstore.Core` only so that the AmazonPay module (Amazon.Pay.API.SDK 2.7.4) can load it. No code outside AmazonPay uses it. | Known BC issues fixed only in BouncyCastle.Cryptography ≥ 2.3.1 (e.g. CVE-2024-29857, -30171, -30172). Reachable only through the AmazonPay module. | Do not install AmazonPay (not offered in Turkey). Separate branch: drop the AmazonPay module from the MOTOR build and remove the package from `Smartstore.Core`, or wait for an upstream/SDK switch. |
| F4 | AJAX/JSON error responses include `exception.Message` (`ErrorController` → `ErrorModel.ErrorMessage`) even with `ShowCustomErrors=false`. | Information disclosure (e.g. database error texts) to any visitor who triggers a server error on an AJAX endpoint. | Upstream-compatible change: return a generic message for 500 when `ShowCustomErrors` is false, keep the message for intended user-facing exceptions. Needs a review of admin/storefront AJAX error handling. |
| F5 | `Views/Error/AccessDenied.cshtml` renders the exception message with `Html.Raw`. | XSS if an `AccessDeniedException` message ever contains user input. Not exploitable in the current code paths found. | Encode the message (upstream). |
| F6 | DataProtection key ring unencrypted at rest. | Read access to `App_Data` = cookie forgery. | Section 5: file permissions now; certificate/KMS encryption later. |
| F7 | `Smartstore:MaxRequestBodySize` is 2 GB. | Large-upload DoS. | Lower it at the proxy (`client_max_body_size`) and in `usersettings.Production.json` once the largest legitimate upload (imports, backups) is known. |
| F8 | Default Docker images run the app as root and listen on HTTP. | Container breakout impact; TLS must be in front. | Docker session: non-root user, `App_Data` volume, TLS proxy. |
