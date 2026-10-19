# MOTOR with Docker

The MOTOR image is built from this repository. It contains the Smartstore
application, every module in `Smartstore.sln` (including **MotorShop.Fitment**),
the **MotorDark** theme, the localization resources and `LICENSE`. Do not run the
upstream `ghcr.io/smartstore/smartstore-linux` image for MOTOR: it has none of
the MOTOR code.

| File | Purpose |
| --- | --- |
| `Dockerfile` | Builds and publishes the solution inside the container (SDK pinned to `global.json`). |
| `NoBuild.Dockerfile` | Packages an existing `build/artifacts/...` publish output. |
| `docker-compose.yml` | Web app, volumes, health check. |
| `docker-compose.mysql.yml` / `.sqlserver.yml` / `.postgres.yml` | One database each. Use exactly one. |
| `.env.example` | All settings. Copy to `.env`; never commit `.env`. |

## Prerequisites

- Docker Engine 24+ (or Docker Desktop) with Compose v2.24+.
- x86-64 host (the wkhtmltopdf package in the image is amd64 only).
- About 4 GB RAM for the build (SQL Server needs 2 GB more at runtime).
- Internet access during the build (NuGet, Debian packages, wkhtmltopdf).

## Configure

```bash
cp .env.example .env
```

Set `MOTOR_DB_PASSWORD` and `MOTOR_DB_ROOT_PASSWORD` (for example
`openssl rand -base64 24`), choose the database in `COMPOSE_FILE`, and adjust
`MOTOR_HTTP_PORT` if 8080 is taken. Compose refuses to start while a password is
empty.

Keep one `.env` per environment: the developer machine and the server each have
their own, and a QA stack gets its own `MOTOR_PROJECT_NAME`, so containers,
network and volumes never mix. App settings that are not about Docker go into
environment variables (`Smartstore__...`, see below) or
`Config/usersettings.Production.json` on the server.

## Build and start

```bash
MOTOR_REVISION=$(git rev-parse HEAD) docker compose build
docker compose up -d
docker compose ps
```

`MOTOR_REVISION` is stored in the image labels
(`org.opencontainers.image.revision`); without it the label says `unknown`.
The first build takes several minutes. The web container is `healthy` once
`http://127.0.0.1:${MOTOR_HTTP_PORT}/` answers.

Without Compose:

```bash
docker build -t motor-web:local .
```

## Initialize the database

Open `http://127.0.0.1:8080/` (or your port). A new installation redirects to
`/install`. Enter:

| Database file | Server | Database | User | Password |
| --- | --- | --- | --- | --- |
| mysql | `db` | `MOTOR_DB_NAME` | `MOTOR_DB_USER` | `MOTOR_DB_PASSWORD` |
| postgres | `db` | `MOTOR_DB_NAME` | `MOTOR_DB_USER` | `MOTOR_DB_PASSWORD` |
| sqlserver | `db` (SQL authentication) | `MOTOR_DB_NAME` | `sa` | `MOTOR_DB_ROOT_PASSWORD` |

Use a real admin e-mail and a strong admin password. Leave sample data off for
production. The installer offers English (`en-US`) and German (`de-DE`) as
primary language; add Turkish afterwards. When the installer finishes it calls
`/install/finalize`, the app stops, and Compose restarts it
(`restart: unless-stopped`). Then, in the admin area:

1. **Configuration > Themes**: select **MotorDark** (a new install starts with
   Flex).
2. **Configuration > Plugins**: check that **Motor Fitment (make / model / year)**
   is installed. The installer installs every module in the image, including
   **Smartstore.DevTools**: uninstall DevTools on production.

Headless install (QA only, disposable data): `POST /install/install` with the
installer form fields, then `POST /install/finalize?restart=true`. Without the
finalize call the running process stays in setup mode. The installer response
echoes the submitted passwords, so never run it over an untrusted network.

The connection string ends up in `App_Data/Tenants/Default/Settings.txt` inside
the `tenants` volume, never in the image.

For production on SQL Server, create a dedicated login with rights on the MOTOR
database only and install with that login instead of `sa`.

## Persistent data

| Volume | Path in container | Content |
| --- | --- | --- |
| `<project>_tenants` | `/app/App_Data/Tenants` | `Settings.txt`, media files, data protection keys, SQLite files |
| `<project>_logs` | `/app/App_Data/Logs` | Application log files |
| `<project>_mysql-data` (or `mssql-data`, `postgres-data`) | database data directory | Database |

Back up the database and the `tenants` volume together. Example for MySQL:

```bash
docker compose exec db sh -c 'mysqldump -u root -p"$MYSQL_ROOT_PASSWORD" --single-transaction "$MYSQL_DATABASE"' > motor.sql
docker run --rm -v motor_tenants:/data -v "$PWD":/backup alpine tar czf /backup/motor-tenants.tgz -C /data .
```

The data protection keys in the `tenants` volume are stored unencrypted.
Losing them signs everyone out; leaking them lets someone forge cookies.

## Production notes

- The container speaks plain HTTP on port 80. Terminate TLS at a reverse proxy
  (nginx, Caddy, Traefik, a load balancer) and keep `MOTOR_HTTP_BIND=127.0.0.1`
  when the proxy runs on the same host.
- Tell the app which proxies to trust, and which host names to answer:

  ```yaml
  # docker-compose.override.yml on the server, or extra lines in the web service
  environment:
    AllowedHosts: "motor.example.com;www.motor.example.com"
    Smartstore__ReverseProxy__KnownNetworks__0: "172.16.0.0/12"
  ```

- `Config/*.json` files are loaded after environment variables: a mounted
  `Config/usersettings.Production.json` overrides `Smartstore__...` variables.
- The container health check requests `/`. Once MotorShop.Security is
  installed, `/healthz` (liveness) is the cheaper probe.
- Open item: the container still runs as root. Moving to the non-root `app`
  user needs the volumes re-owned and a port above 1024.
- Keep `ASPNETCORE_ENVIRONMENT=Production`. With `Development` the app moves the
  data protection keys out of the `tenants` volume and loses them on every
  container rebuild.
- Pin exact database image tags (`MOTOR_MYSQL_TAG` etc.) and tag MOTOR images
  with the git commit (`MOTOR_IMAGE_TAG=$(git rev-parse --short HEAD)`), so a
  running container maps to an exact source version (see
  [licensing.md](licensing.md)).
- Database passwords are read from `.env`. On a server, restrict the file
  (`chmod 600 .env`) or move to Docker secrets or a secret manager. The official
  MySQL and PostgreSQL images also accept `*_PASSWORD_FILE` variables; SQL
  Server does not.
- Database ports are not published. For a one-off query use
  `docker compose exec db ...`.
- Install **MotorShop.Security** once it is merged (see
  `production-checklist.md`), and do not install Smartstore.DevTools.

## Update

```bash
git pull
docker compose build
docker compose up -d
```

Smartstore applies pending database migrations on startup. Back up first.

## Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| Build fails with `RZ2009` in an Admin view | Wrong SDK. The `Dockerfile` pins `SDK_TAG` to `global.json`; keep both in sync. |
| `required variable MOTOR_DB_PASSWORD is missing a value` | Set the passwords in `.env`. |
| `Bind for 127.0.0.1:8080 failed: port is already allocated` | Change `MOTOR_HTTP_PORT`. |
| Installer cannot connect to the database | Server must be `db`, not `localhost`. Wait until `docker compose ps` shows the database `healthy`. |
| Store shows Flex instead of MotorDark | Select MotorDark under Configuration > Themes. Check the image: `docker compose exec web ls Themes`. |
| Web container `unhealthy` right after the first start | The first start compiles views and can take a few minutes. See `docker compose logs -f web`. |
| Need the app logs | `docker compose logs web`, or the files in the `logs` volume. |
| SQL Server container exits | Password too weak, or less than 2 GB RAM for Docker. |

## Shutdown and cleanup

```bash
docker compose stop        # stop, keep everything
docker compose down        # remove containers and network, keep volumes
docker compose down -v     # also DELETE the database and tenant volumes
docker image rm motor-web:local
```

`down -v` destroys all store data. Never run it against production.
