# MOTOR production checklist

Settings and steps that a production deployment of the MOTOR store needs on top of
the stock Smartstore defaults. Values that depend on the server (host name, proxy
network, paths) go into `src/Smartstore.Web/Config/usersettings.Production.json`
on the server. That file and `Config/connections*.json` are ignored by git and
Docker and must never be committed.

Details on secrets, key backup, credential rotation, security headers and health
checks: [security-operations.md](security-operations.md). A full example server file:
[config/usersettings.Production.example.json](config/usersettings.Production.example.json).

## 1. Application settings

Example `Config/usersettings.Production.json`:

```json
{
  "AllowedHosts": "motor.example.com;www.motor.example.com",
  "Smartstore": {
    "ReverseProxy": {
      "Enabled": true,
      "KnownNetworks": [ "10.0.0.0/8" ],
      "KnownProxies": []
    }
  }
}
```

- `AllowedHosts`: the real domain names instead of `*`.
- `ReverseProxy`: list the proxy or load balancer addresses in front of the app.
  With the defaults only loopback proxies are trusted, so behind Docker, IIS ARR,
  nginx or a cloud load balancer the client IP and the HTTPS scheme come out wrong.
- Keep `EnableRazorRuntimeCompilation` at `true`. Theme views (Flex and MotorDark)
  are excluded from build-time Razor compilation (`Themes\**` in
  `Smartstore.Web.csproj`) and are only compiled at runtime. Turning it off makes
  the store fall back to the plain views.

## 2. Modules

- Install **MotorShop.Security** (Admin > Plugins). It adds
  `X-Content-Type-Options`, `Referrer-Policy` and `Permissions-Policy`, removes the
  `X-Powered-By: Smartstore <version>` header and the Kestrel `Server` header, and
  answers `GET /healthz` (liveness) and `GET /healthz/ready` (database and task
  scheduler; `503` when the database is unreachable) for probes and uptime monitors.
  At start-up it logs a warning for each unsafe production setting it finds.
  - It only runs once installed. After installing it, check the headers with
    `curl -I https://<host>/`.
- Do not install **Smartstore.DevTools** in production (MiniProfiler, debug output).

## 3. Hosting

- The task scheduler starts with the first request and needs a process that keeps
  running. On IIS:
  - application pool start mode `AlwaysRunning`
  - idle time-out `0`
  - site `preloadEnabled=true`
- Writable folders for the app identity: `App_Data`, `Modules`, and the media
  path (default `App_Data/Tenants/<tenant>/Media`).
- Data protection keys live in `App_Data/Tenants/<tenant>/DataProtection-Keys`,
  unencrypted. Restrict access to the app identity and include the folder in
  backups. Losing it signs everyone out and invalidates protected data.

## 4. Data and backups

- Daily database backup plus `App_Data/Tenants/<tenant>` (settings, media, keys).
  Test a restore before go-live.
- `App_Data/Tenants/<tenant>/Settings.txt` holds the connection string in plain
  text. Restrict file access; never copy it into the repository or an image.
- Do not deploy local test tenants or demo data.

## 5. Docker

- `Dockerfile` (build in the container) and `NoBuild.Dockerfile` (from a host
  publish) both include the MotorDark theme and `LICENSE`.
- The images listen on HTTP port 80 only. Terminate TLS at the proxy and
  configure `ReverseProxy` as above.

## 6. Licence

Smartstore is AGPL v3. A public store running this modified code must offer its
complete source to users, for example through a public repository linked from the
storefront. Keep the "Smartstore" footer notice.
