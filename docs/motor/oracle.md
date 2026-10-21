# MOTOR on Oracle Cloud Always Free + Supabase

Owner decision (2026-10-21): to start without hosting costs, the store runs on an Oracle
Cloud "Always Free" Ampere (ARM) VM with the repository's VPS stack (`deploy/vps`), and the
database stays on Supabase (`prosev-prod`, see [supabase.md](supabase.md)). Fly.io
([fly.md](fly.md)) remains the paid alternative.

## 1. VM (owner, in the Oracle console)

* Home region **Germany Central (Frankfurt)**: next to Supabase eu-central-1. It cannot be
  changed later, and Always Free resources exist only in the home region.
* Instance: `VM.Standard.A1.Flex`, 4 OCPU / 24 GB (the whole Always Free Ampere quota),
  Canonical Ubuntu 24.04 (aarch64), public IPv4, boot volume 100 GB.
* SSH: the deploy key's public half. The private key stays on the owner's machine.
* Subnet security list: ingress TCP 80 and 443 from `0.0.0.0/0`.
* Free Tier accounts can have idle Always Free instances reclaimed. Upgrading the account
  to Pay As You Go prevents that and stays free within the Always Free limits.

## 2. Server setup

Oracle's Ubuntu images also block ports with iptables on the VM, so open 80/443 there too
(`iptables -I INPUT 6 -p tcp -m multiport --dports 80,443 -j ACCEPT` plus
`netfilter-persistent save`), install Docker Engine with the compose plugin, clone the
repository and follow [vps-operations.md](vps-operations.md) for `.env`, secrets and timers.

ARM specifics:

* The registry image is amd64 only. Build on the server instead and tell `deploy.sh` not
  to pull:

  ```bash
  # deploy/vps/.env: MOTOR_IMAGE=motor-web and MOTOR_IMAGE_PULL=never
  tag=$(deploy/vps/scripts/build-image.sh origin/preview)
  deploy/vps/scripts/deploy.sh "$tag"
  ```

  `build-image.sh` builds from `git archive` of the ref, so only committed files reach the
  image. `install-wkhtmltopdf.sh` picks the arm64 or amd64 package automatically.

## 3. Address without a domain

Until the store's own domain exists, use `MOTOR_DOMAIN=<ip with dashes>.sslip.io`
(e.g. `203-0-113-10.sslip.io`): sslip.io resolves it to the IP, and Caddy gets a normal
Let's Encrypt certificate for it. Later, add the real domain's A record in Vercel DNS,
change `MOTOR_DOMAIN` and recreate Caddy.

## 4. First installation

`/install` is blocked by Caddy. For the first installation only, set
`MOTOR_INSTALL_ALLOW_IP=<owner's public IP>/32` in `.env`, recreate Caddy
(`docker compose up -d caddy`), let the owner run the installer at `https://<domain>/install`
(PostgreSQL; do not store media in the database; no sample data), then remove the variable
and recreate Caddy again. Afterwards run `deploy/supabase/02-verify-exposure.sql`.
