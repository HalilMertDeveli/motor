# MOTOR on Supabase PostgreSQL

MOTOR uses Smartstore's built-in PostgreSQL provider (`Smartstore.Data.PostgreSql`, Npgsql).
No custom driver is needed. Supabase is only the managed PostgreSQL server. The storefront
never talks to Supabase's Data API, Auth or Storage. No credentials belong in this file or
anywhere else in the repository.

## 1. Environments

| Environment | Database | Data |
|---|---|---|
| Development / CI | Disposable `postgres:17-alpine` container | Sample or test data only |
| Preview (staging) | Its own Supabase project | Test customers only, no real payment credentials |
| Production | Its own Supabase project | Real orders and customers |

Never point a preview stack at the production project. Use separate projects, not separate
schemas, so that credentials, backups and the Data API settings are separate too.

## 2. Connection from the server

The app reads the connection string from the environment variable
`MOTOR_DB_CONNECTION_STRING` (and optionally `MOTOR_DB_PROVIDER=postgresql`):

* When it is set, it always wins over `App_Data/Tenants/<tenant>/Settings.txt`, both at
  runtime and in the installer. The installer ignores the database fields of the form.
* `Settings.txt` then holds the placeholder `env:MOTOR_DB_CONNECTION_STRING` instead of the
  password, so the persistent `App_Data` volume and its backups contain no database secret.
  An existing secret in `Settings.txt` is replaced by the placeholder on the next start.
* If `Settings.txt` holds the placeholder but the variable is missing, the app refuses to
  start. It does not fall back to the public installer.
* If `MOTOR_DB_PROVIDER` does not match the provider of an existing installation, the app
  refuses to start.

Code: `src/Smartstore/Data/DataSettings.Environment.cs`, used by `DataSettings.Load/Save`
and `InstallationService`.

### Which Supabase endpoint

Copy the host and user from the project's **Connect** dialog in the dashboard. Do not guess them.

| Endpoint | Port | Use for MOTOR |
|---|---|---|
| Direct connection `db.<project-ref>.supabase.co` | 5432 | Preferred **if the VPS has IPv6**. Supabase's direct endpoint is IPv6-only unless the paid IPv4 add-on is enabled. |
| Session pooler (Supavisor) `<region>.pooler.supabase.com`, user `motor_app.<project-ref>` | 5432 | Use when the VPS is IPv4-only. Session mode keeps one server connection per client connection, which is compatible with EF Core, prepared statements and Smartstore's migrations. |
| Transaction pooler | 6543 | **Do not use.** It breaks session state and prepared statements that EF Core and the migrator rely on. |

### Connection string template

Placeholders only. Fill them in on the server's `.env` file, never in git:

```
Host=<host from the Connect dialog>;Port=5432;Database=postgres;Username=<motor_app or motor_app.<project-ref>>;Password=<password>;SSL Mode=VerifyFull;Root Certificate=/run/secrets/supabase-ca.crt;Maximum Pool Size=10;Minimum Pool Size=0;Connection Idle Lifetime=60;Timeout=15;Command Timeout=60;Keepalive=30;Include Error Detail=false
```

* `SSL Mode=VerifyFull` together with `Root Certificate`: encrypted **and** the server
  certificate is checked against Supabase's CA (dashboard: Database settings, SSL
  configuration, download the certificate). Store it on the server next to `.env` and mount
  it read-only. `SSL Mode=Require` alone encrypts but does not validate the server.
* Also enable **Enforce SSL** on the project so that unencrypted connections are refused.
* `Maximum Pool Size`: the pooler's pool per project is small and depends on the compute
  size. Keep the app's pool below it, with headroom for migrations, the SQL editor and
  backups. Start with 10 and raise it only when measurements show waits.
* The installer's default builder would use `Maximum Pool Size=1024`. The environment
  string replaces it, which is one more reason to always use the environment variable.

## 3. One-time project setup

Run per project, in the SQL editor, as `postgres`:

1. `deploy/supabase/01-motor-app-role.sql`: creates the non-superuser role `motor_app`,
   creates `citext` in schema `extensions` (Smartstore stores all strings as `citext`),
   sets the role's search path and server-side timeouts, and revokes Data API access
   (`anon`, `authenticated`) to schema `public`. Safe to re-run.
2. Set the password in a separate statement that you do not save:
   `ALTER ROLE motor_app WITH PASSWORD '<generated>';`
3. Dashboard → API settings: **turn the Data API off**, or at least remove `public` from
   the exposed schemas. MOTOR does not use it. Do not publish the anon or service-role keys
   anywhere, and do not give them to the app.
4. Dashboard → Database: enable **Enforce SSL** and, if available, **Network restrictions**
   limited to the VPS's IP address.

## 4. Installation against Supabase

1. Start the stack with `MOTOR_DB_CONNECTION_STRING` and `MOTOR_DB_PROVIDER=postgresql` set.
2. Open `/install` once, from the owner's browser over HTTPS, choose PostgreSQL and fill in
   the admin account. The database fields are ignored. The database already exists in
   Supabase, so the installer only creates the schema.
3. After installation, run `deploy/supabase/02-verify-exposure.sql`. Every row must show
   `ok = true`. Repeat after every MOTOR upgrade, because migrations can add tables.

The installer deletes a database only if it could not connect to it before starting. On
Supabase the database `postgres` always exists, so a failed installation does not drop it.
Clean up the schema manually before you retry.

## 5. Backups and data protection

Supabase backups cover the database only, never the VPS's `App_Data` (media,
DataProtection keys). See the VPS operations runbook for the full backup plan.

* Production needs a paid Supabase plan: daily backups with point-in-time recovery as an
  add-on, and no pausing of inactive projects. Check the current plan terms before going
  live; this is an owner decision with costs.
* Independently of the plan, a nightly logical `pg_dump` taken from the VPS gives an
  off-site copy that the owner controls.

## 6. What was verified, and how

On a local `postgres:17-alpine` container, with
`deploy/supabase/test/00-supabase-emulation.sql` approximating Supabase's roles and default
grants:

* `01-motor-app-role.sql` runs, and also re-runs without errors.
* `02-verify-exposure.sql` reports no access for `anon` and `authenticated`, including for
  a table created by `postgres` before the script ran.
* Smartstore installs and runs as `motor_app`, using only `MOTOR_DB_CONNECTION_STRING`
  (see the PR for the exact commands and results).

Verified on the real project `prosev-prod` (eu-central-1, 2026-10-20):

* On Supabase, `postgres` is not a superuser. `01-motor-app-role.sql` therefore grants
  `motor_app` to `postgres`; without it the default-privilege statements fail with
  "permission denied to change default privileges". The local emulation did not show this.
* The session pooler of this project is `aws-1-eu-central-1.pooler.supabase.com:5432`
  (the `aws-0-...` host answers "tenant/user not found"). Always take the host from the
  Connect dialog.
* `motor_app` logs in through the pooler with `SSL Mode=VerifyFull` against
  "Supabase Root 2021 CA" (`prod-ca-2021.crt`, also at `deploy/fly/supabase-ca.crt`);
  `search_path` and `citext` resolve as intended.
* The password never has to leave the owner's machine: compute the
  `SCRAM-SHA-256$4096:...` verifier locally and run `ALTER ROLE motor_app PASSWORD
  '<verifier>'`. PostgreSQL stores a pre-hashed verifier as is.

Not verified yet: pool limits per compute size, the Data API toggle, and backup/PITR
behaviour.

Hosting on Fly.io: [fly.md](fly.md).
