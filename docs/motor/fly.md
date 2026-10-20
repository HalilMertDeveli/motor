# MOTOR on Fly.io + Supabase

Hosting chosen by the owner on 2026-10-20: the app runs on Fly.io, the database is
Supabase PostgreSQL, and Vercel is used for DNS only.

| Part | Where | Notes |
|---|---|---|
| App | Fly.io app `prosev`, region `fra` (Frankfurt), one always-on machine | `deploy/fly/fly.toml` |
| Files | Fly volume `motor_tenants` on `/app/App_Data/Tenants` | media, `Settings.txt` placeholder, data protection keys, installed modules |
| Database | Supabase project `prosev-prod` (eu-central-1), role `motor_app` | [supabase.md](supabase.md) |
| DNS | Vercel (when the store's domain is connected) | CNAME/A/AAAA to Fly, see section 6 |

**Why not Vercel for the app:** Smartstore is a long-running ASP.NET Core process with a
writable `App_Data` (media, keys, modules) and an in-process task scheduler. Vercel has no
.NET runtime and its serverless functions have neither a persistent process nor a disk.

The repository's VPS stack (`deploy/vps`) stays valid as an alternative host.

## 1. Image

GitHub Actions is not required. Build on any machine with Docker, from the repository root:

```bash
sha=$(git rev-parse --short=7 HEAD)
docker build -t motor-web:$sha --build-arg MOTOR_REVISION=$(git rev-parse HEAD) .
docker build -t registry.fly.io/prosev:$sha --build-arg BASE_IMAGE=motor-web:$sha deploy/fly
fly auth docker
docker push registry.fly.io/prosev:$sha
```

`deploy/fly/Dockerfile` adds two things to the regular image:

* It starts as root only to `chown` the volume's mount point to the `app` user (Fly mounts
  volumes owned by root), then drops privileges with `setpriv` before the app starts.
* The public Supabase CA certificate at `/etc/motor/supabase-ca.crt` for `SSL Mode=VerifyFull`.

## 2. One-time setup

```bash
fly apps create prosev --org personal
fly volumes create motor_tenants --app prosev --region fra --size 3
fly secrets import --app prosev --stage < <local file with MOTOR_DB_CONNECTION_STRING=...>
```

The connection string (session pooler, see [supabase.md](supabase.md)):

```
Host=aws-1-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=motor_app.<project-ref>;Password=<password>;SSL Mode=VerifyFull;Root Certificate=/etc/motor/supabase-ca.crt;Maximum Pool Size=10;Minimum Pool Size=0;Connection Idle Lifetime=60;Timeout=15;Command Timeout=60;Keepalive=30;Include Error Detail=false
```

Keep the file outside the repository and delete it after the import. Never paste it into chat,
an issue or a PR.

## 3. First deployment and installation

The installer must not be reachable from the internet. Deploy the first time without public
IP addresses, install through a private tunnel, and only then allocate the addresses:

```bash
fly deploy -c deploy/fly/fly.toml --image registry.fly.io/prosev:<sha> --no-public-ips \
  --env "AllowedHosts=prosev.fly.dev;localhost"
fly proxy 8080:8080 --app prosev
```

Open `http://localhost:8080/install` in the owner's browser (Chrome treats `localhost` as a
secure context, so the production `Secure` cookies work over the tunnel). Use `localhost`,
not `127.0.0.1`: host filtering answers `400 Bad Request` for any host not in `AllowedHosts`.
The first start takes about 2–3 minutes (Razor runtime compilation of the theme views).

The tunnel reaches the machine over Fly's private IPv6 network, which is why
`deploy/fly/Dockerfile` makes the app listen on `[::]` (dual stack) instead of `0.0.0.0`.

**Fly trial accounts stop every machine after 5 minutes** ("Trial machine stopping. To run
for longer than 5m0s, add a credit card"). Add a card at <https://fly.io/trial> before the
installation; an installation that is cut off half-way leaves a partial schema in Supabase
that has to be dropped before retrying.

* database fields are ignored (the environment variable wins), choose PostgreSQL;
* **do not** store media in the database (the volume holds media; the Supabase Free plan has
  500 MB);
* no sample data for the production store.

Then remove `localhost` from the allowed hosts and go public:

```bash
fly deploy -c deploy/fly/fly.toml --image registry.fly.io/prosev:<sha>
fly ips allocate-v6 --app prosev
fly ips allocate-v4 --shared --app prosev
```

Afterwards run `deploy/supabase/02-verify-exposure.sql` in the Supabase SQL editor; every
row must report `ok = true`.

## 4. Updates and rollback

Build and push a new tag (section 1), then `fly deploy -c deploy/fly/fly.toml --image
registry.fly.io/prosev:<sha>`. Smartstore runs pending migrations at start-up, so take a
database backup first (section 5). Rollback: deploy the previous tag, which `fly releases
--app prosev --image` lists. A rollback does not undo migrations.

## 5. Backups

* Supabase Free has **no** backups. Before the store takes real orders, upgrade the project
  to Pro (daily backups; point-in-time recovery as an add-on). Owner decision with costs.
* Fly volume snapshots are taken daily and kept for 5 days by default
  (`fly volumes snapshots list <volume-id>`). They cover media and the data protection keys.
* An off-site `pg_dump` (see `deploy/vps/scripts/backup.sh` for the exact flags) can be run
  from any machine with the connection string.

## 6. Domain (Vercel DNS)

1. `fly certs add <domain> --app prosev` and follow the DNS records it prints.
2. In Vercel (Domains → DNS records) add exactly those records (A/AAAA for the apex, or a
   CNAME to `prosev.fly.dev` for `www`), plus the `_acme-challenge` CNAME if asked.
3. Add the domain to `AllowedHosts` and the health check `Host` in `deploy/fly/fly.toml`
   (`;`-separated, the first one is used for the health check) and redeploy.

## 7. Costs (check current prices before changing plans)

* Fly: one `shared-cpu-2x` machine with 2 GB RAM plus a 3 GB volume, billed monthly by usage.
* Supabase: Free plan for staging; Pro for production.
* Vercel: DNS only, no compute.
