# MOTOR on a Linux VPS: setup and operations runbook

How to run the MOTOR store 24/7 on a remote Linux server with Docker and Supabase
PostgreSQL, without the owner's computer. No credentials belong in this file or
anywhere else in the repository.

Related: [supabase.md](supabase.md) (database), [docker.md](docker.md) (image),
[security-operations.md](security-operations.md) (hardening), [licensing.md](licensing.md) (AGPL).

## 1. Architecture

```
 Customer / Admin browser
          │ HTTPS 443 (HTTP 80 → redirect)
          ▼
 ┌──────────────── Linux VPS (Docker Engine + Compose) ────────────────┐
 │  caddy  ── Let's Encrypt certificates, body limit, blocks /install   │
 │    │      and /healthz from the internet                             │
 │    │ edge network 10.203.40.0/24 (private)                           │
 │    ▼                                                                 │
 │  web    ── MOTOR image (Smartstore + MotorDark + MotorShop.Fitment   │
 │           + MotorShop.Security), no published port                   │
 │           volumes: tenants (media, DataProtection keys, Settings),   │
 │                    logs                                              │
 │  systemd timers: backup (nightly), monitor (5 min)                   │
 └────┬─────────────────────────────────────────────────────┬───────────┘
      │ PostgreSQL over TLS (VerifyFull)                    │ encrypted backups
      ▼                                                     ▼
 Supabase PostgreSQL (managed)                     Off-site bucket (rclone)

 GitHub: PR → preview (CI) → approved → motor → motor-image.yml → GHCR image
         → owner approves motor-deploy.yml (or runs deploy.sh on the server)
```

Nothing in production depends on a Windows PC, Docker Desktop, a mapped drive,
an open terminal or Claude Code. Docker restarts the containers after crashes
and reboots (`restart: unless-stopped`, Docker enabled at boot), and the
systemd timers run without a logged-in user.

## 2. Environments

| | Preview (staging) | Production |
|---|---|---|
| Code | `preview` branch image (manual build) | Approved `motor` image `sha-<commit>` |
| Server | Separate VPS, or a second stack with another `MOTOR_PROJECT_NAME`, domain and subnet | Own VPS |
| Database | Own Supabase project | Own Supabase project |
| Data | Test customers only | Real customers and orders |
| Payments | Test/offline methods only, no live credentials | Live credentials after a separate approval |

Never point a preview stack at the production database or volume.

## 3. Server

### Sizing

Measured on the test stack (sample catalog, Docker Desktop): web ~380 MiB RAM after
warm-up, Caddy ~12 MiB, 8 s from restart to the first page, cached pages
0.03–0.2 s. The server never builds the image (CI does), so no SDK memory is needed.

| | Start with | Grow when |
|---|---|---|
| CPU | 2 vCPU | Sustained CPU > 70 % at peak, slow admin imports |
| RAM | 4 GB | App memory > 2.5 GB (raise `MOTOR_WEB_MEMORY`) or swap in use; 8 GB for large catalogs / search index |
| Disk | 40–80 GB NVMe | Media + 3 image versions (~1.2 GB each) + 14 days of local backups > 70 % |
| Network | 1 Gbit/s, IPv4 + IPv6 | IPv6 lets the app use Supabase's direct connection |
| Location | Close to the Supabase region and the customers (EU / Turkey) | |

These are starting points; re-measure under real traffic before scaling.

### Preparation (Ubuntu 24.04 LTS or Debian 12)

1. Create a non-root user for operations, SSH keys only:
   `adduser motor`, add the owner's public key, set `PasswordAuthentication no` and
   `PermitRootLogin no` in `/etc/ssh/sshd_config`.
2. Firewall: allow only 22 (ideally from known IPs), 80 and 443 (tcp, plus 443/udp
   for HTTP/3): `ufw allow OpenSSH; ufw allow 80,443/tcp; ufw allow 443/udp; ufw enable`.
   Docker publishes 80/443 itself; no other port is published by the stack.
3. Automatic security updates: `apt install unattended-upgrades` and enable it.
4. Install Docker Engine and the Compose plugin from Docker's apt repository
   (not Docker Desktop), then `systemctl enable --now docker` and
   `usermod -aG docker motor`. Note: membership in `docker` is root-equivalent.
5. Time zone: `timedatectl set-timezone Europe/Istanbul`.
6. `apt install git gnupg curl openssl` (used by the scripts).

## 4. First deployment

All on the VPS as `motor`, under `/opt/motor`:

```sh
git clone --branch motor https://github.com/HalilMertDeveli/motor.git /opt/motor
cd /opt/motor/deploy/vps
cp .env.example .env && chmod 600 .env       # fill in, see below
# secrets/supabase-ca.crt   Supabase CA (Database settings → SSL configuration)
# secrets/backup-public.asc owner's GPG public key (section 6)
chmod 600 secrets/*
docker login ghcr.io                          # read-only token if the package is private
docker compose config --quiet                 # validates .env
```

`.env` values: `MOTOR_IMAGE` / `MOTOR_IMAGE_TAG` (from the image workflow summary),
`MOTOR_DOMAIN`, `MOTOR_ACME_EMAIL`, `MOTOR_DB_CONNECTION_STRING` (see
[supabase.md](supabase.md)), backup and monitoring settings.

DNS: create `A` (and `AAAA`) records for the domain pointing to the VPS before
the first start, otherwise Let's Encrypt cannot issue the certificate.

### Install the store (once)

```sh
docker compose -f compose.yml -f compose.install.yml up -d web
# on the owner's computer:
ssh -L 8080:127.0.0.1:8080 motor@<vps>
# browser: http://localhost:8080/install → PostgreSQL, admin account, language.
#   The database fields are ignored: the connection comes from .env.
docker compose up -d          # back to the normal stack, port 8080 closes
```

Then run `deploy/supabase/02-verify-exposure.sql` in the Supabase SQL editor
(all rows `ok = true`), open `https://<domain>/admin`, and install/enable
MotorShop.Security (then set `MOTOR_HEALTH_PATH=/healthz/ready`).

### Enable backups and monitoring

```sh
sudo cp systemd/motor-*.service systemd/motor-*.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now motor-backup.timer motor-monitor.timer
scripts/backup.sh --reason first        # run once by hand and check backups/
```

Add an **external** uptime check (e.g. UptimeRobot, Better Stack or
healthchecks.io HTTP check) on `https://<domain>/`: the VPS cannot report its
own total outage.

## 5. Routine operations

| Task | Command (in `/opt/motor/deploy/vps`) |
|---|---|
| Status | `docker compose ps` |
| App logs | `docker compose logs -f --tail 200 web` (also `App_Data/Logs` in the `logs` volume, and Admin → System → Log) |
| Proxy logs | `docker compose logs -f caddy` |
| Restart the app | `docker compose restart web` (sessions survive: keys are in the volume) |
| Resource use | `docker stats` |
| Deploy a release | `scripts/deploy.sh sha-1a2b3c4` |
| Roll back | `scripts/deploy.sh --rollback` |
| Backup now | `scripts/backup.sh --reason manual` |
| Self-check | `scripts/monitor.sh` |
| Server reboot | `sudo reboot`: containers and timers come back by themselves |
| Clean old images | `docker image prune -a --filter "until=720h"` (keeps recent tags for rollback) |

### Release flow

1. Feature branch → PR to `preview` → CI green → owner QA on the preview stack.
2. Owner approves → separate PR from the feature branch to `motor` → merge.
3. `motor-image.yml` builds `ghcr.io/<owner>/motor-web:sha-<short>`.
4. Deploy, either on the VPS with `scripts/deploy.sh sha-<short>` or with the
   manual `motor-deploy.yml` workflow (environment `production`, owner approval).
5. `deploy.sh` backs up first, waits for the health check, smoke-tests
   `https://<domain>/` and rolls back automatically on failure.

Smartstore applies database migrations at startup. A rollback of the image does
not roll back the schema: if a release with migrations failed, restore the
pre-deploy backup into a new database (section 6), check it, and switch to it
deliberately, or fix forward.

## 6. Backups and recovery

### What is backed up

| Data | Where it lives | Backed up by |
|---|---|---|
| Products, customers, orders, settings | Supabase PostgreSQL | Supabase (plan-dependent) **and** nightly `pg_dump` by `backup.sh` |
| Product media, uploads | `tenants` volume (`Media`) | `backup.sh` (volume archive) |
| DataProtection keys (logins, cookies, encrypted settings) | `tenants` volume | `backup.sh` |
| `Settings.txt`, installed modules list | `tenants` volume | `backup.sh` |
| Certificates | `caddy-data` volume | Not backed up: Caddy re-issues them |
| `.env`, `secrets/` | VPS only | **Owner** keeps an offline copy (password manager) |

Supabase backups never include the VPS volume, and the volume never includes the
database. Both are needed for a restore.

### Encryption

Backups are encrypted to the owner's GPG public key. Create the key pair on the
owner's own computer, not on the server:

```sh
gpg --quick-gen-key "MOTOR backups <owner@example.com>" ed25519 cert,sign never
gpg --quick-add-key <fingerprint> cv25519 encr never
gpg --armor --export <fingerprint>            > backup-public.asc   # → VPS secrets/
gpg --armor --export-secret-keys <fingerprint> > backup-private.asc # offline only
```

Keep the private key (and its passphrase) in two offline places. Without it,
no backup can be restored.

### Targets

| | Target | How |
|---|---|---|
| RPO (data loss) | ≤ 24 h with nightly dumps; minutes with Supabase PITR (paid add-on) | nightly timer, `deploy.sh` pre-deploy backup |
| RTO (downtime) | ≤ 4 h for a full server loss | new VPS from this runbook + restore |
| Retention | 14 days local, 30–90 days off-site (bucket lifecycle) | `MOTOR_BACKUP_KEEP_DAYS`, provider rules |
| Restore test | Monthly, into an isolated database and volume | `restore.sh` |

### Restore (test or disaster recovery)

Always into a new, empty database (fresh Supabase project or branch prepared with
`01-motor-app-role.sql`) and a new volume, never over live data:

```sh
echo 'MOTOR_RESTORE_DB_CONNECTION_STRING=<Npgsql string of the EMPTY target>' > /tmp/target.env
chmod 600 /tmp/target.env
scripts/restore.sh backups/20261019T033000Z ~/backup-private.asc /tmp/target.env motor_tenants_restored
shred -u ~/backup-private.asc /tmp/target.env     # remove the key from the server afterwards
```

`restore.sh` refuses the live database, the live volume and non-empty targets.
To verify, start a test stack (another `MOTOR_PROJECT_NAME`, its own domain or an
SSH tunnel) on the restored volume and database, check the storefront, an order
and an admin login. Switching production to a restored database is a manual,
approved step: stop `web`, change `MOTOR_DB_CONNECTION_STRING` (and the volume if
needed), start, verify.

### Full server loss

1. New VPS (section 3), clone, put `.env` and `secrets/` back from the owner's
   offline copy.
2. Fetch the latest backup from the off-site bucket. If the Supabase project is
   fine, keep the database; otherwise restore the dump into a new, prepared
   project with `restore.sh` and put its connection string into `.env`.
3. Restore the volume with `restore.sh` into `motor_tenants_restored` (it refuses
   the live name on purpose), then, before the first start, copy it into the
   stack's volume:
   `docker run --rm -v motor_tenants_restored:/from:ro -v motor_tenants:/to alpine:3.20 cp -a /from/. /to/`
4. `docker compose up -d`, point DNS to the new IP, verify storefront, admin
   login and the latest order.

## 7. Security notes

- Secrets live only in `.env` and `secrets/` on the server (mode 600) and in the
  owner's password manager; never in git, images, PRs, logs or chat.
- The app trusts proxy headers only from the edge subnet; host filtering answers
  only `MOTOR_DOMAIN`.
- `/install` and `/healthz` are not reachable from the internet.
- Docker volumes are readable by root only (`/var/lib/docker/volumes`); backups
  leave the server only encrypted.
- Open items: the image runs as root inside the container (non-root image is a
  planned follow-up), and the DataProtection key ring is not encrypted at rest
  inside the app.

## 8. Cloudflare (optional, later)

If DNS moves to Cloudflare with the proxy (orange cloud) enabled:

- SSL/TLS mode **Full (strict)**: Caddy keeps a valid certificate. HTTP-01
  challenges keep working through the proxy; otherwise use Caddy's DNS challenge.
- Trusted proxy: Caddy must take the client IP from Cloudflare. Add
  `trusted_proxies static <Cloudflare IP ranges>` and `client_ip_headers CF-Connecting-IP`
  in the Caddyfile's global `servers` block, and restrict ports 80/443 on the
  firewall to Cloudflare's ranges.
- Cache only static assets (`/Themes/*`, `/lib/*`, `/bundle/*`, `/media/*`
  images). **Never cache** HTML pages, `/cart`, `/checkout*`, `/customer*`,
  `/login`, `/register`, `/admin*`, payment callbacks or any response with
  `Set-Cookie`. Use "Cache Level: Standard" with no "Cache Everything" rule on
  those paths.
- Payment providers (e.g. iyzico) call back over HTTPS to the store domain;
  allow them through WAF rules and do not challenge `/payment*` callback paths.

## 9. What the owner still has to provide

- VPS (provider, region, size per section 3) and SSH access for operations.
- Domain and DNS access.
- Two Supabase projects (preview, production) on a plan with backups for
  production; the CA certificate; `motor_app` passwords.
- GHCR read token for the server (if the package is private).
- GPG key pair for backups; an off-site bucket (rclone config); healthchecks /
  uptime monitor accounts.
- Approval for each production deployment.
