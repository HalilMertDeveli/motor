#!/usr/bin/env sh
# MOTOR: encrypted backup of the database and the application data volume.
#
#   deploy/vps/scripts/backup.sh [--reason "text"]
#
# Produces in backups/<UTC timestamp>/:
#   db.dump.gpg       pg_dump -Fc of the Supabase database (custom format)
#   tenants.tar.gz.gpg App_Data/Tenants volume: Settings.txt, media,
#                     DataProtection keys, installed modules
#   SHA256SUMS        checksums of the encrypted files
#   meta.txt          image tag, reason, sizes (no secrets)
#
# Everything is encrypted with the owner's GPG *public* key
# (secrets/backup-public.asc). The private key never lives on the VPS, so a
# stolen server or bucket does not expose customer data or session keys.
# Optional off-site copy with rclone (secrets/rclone.conf), optional
# success/failure ping (MOTOR_BACKUP_PING_URL, e.g. healthchecks.io).
#
# Read-only for production data: it never writes to the database or volume.
set -eu
umask 077

cd "$(dirname "$0")/.."
. scripts/lib.sh

REASON=scheduled
[ "${1:-}" = "--reason" ] && REASON=${2:-manual}

[ -f .env ] || die ".env missing"
[ -f secrets/backup-public.asc ] || die "secrets/backup-public.asc missing (owner's GPG public key)"
command -v gpg >/dev/null || die "gpg not installed"

PG_CLIENT_IMAGE=$(env_get MOTOR_BACKUP_PG_IMAGE); PG_CLIENT_IMAGE=${PG_CLIENT_IMAGE:-postgres:17-alpine}
KEEP_DAYS=$(env_get MOTOR_BACKUP_KEEP_DAYS); KEEP_DAYS=${KEEP_DAYS:-14}
REMOTE=$(env_get MOTOR_BACKUP_RCLONE_REMOTE)

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
OUT=backups/$STAMP
WORK=$(mktemp -d)
GPG_HOME=""
trap 'rm -rf "$WORK" ${GPG_HOME:+"$GPG_HOME"}' EXIT
mkdir -p "$OUT"
chmod 700 backups "$OUT"

fail() {
    log "FAILED: $*"
    ping_url MOTOR_BACKUP_PING_URL /fail
    exit 1
}

# Import the public key into a throwaway keyring.
# gpg needs a local filesystem for its agent socket, so not inside $WORK.
GPG_HOME=$(mktemp -d /tmp/motor-gnupg.XXXXXX)
GNUPGHOME=$GPG_HOME; export GNUPGHOME
gpg --batch --quiet --import secrets/backup-public.asc || fail "cannot import public key"
RECIPIENT=$(gpg --batch --with-colons --list-keys | awk -F: '/^fpr:/ {print $10; exit}')
[ -n "$RECIPIENT" ] || fail "no key in secrets/backup-public.asc"
encrypt() { gpg --batch --yes --trust-model always --encrypt --recipient "$RECIPIENT" --output "$2" "$1"; }

log "start ($REASON) -> $OUT"
ping_url MOTOR_BACKUP_PING_URL /start

# 1. Database (pg_dump runs in a client container; credentials via env file, not argv)
npgsql_to_docker_env "$(env_get MOTOR_DB_CONNECTION_STRING)" "$WORK/pg.env"
# Only schema public, and only tables the app role owns: Supabase's own schemas
# (auth, storage, ...) and objects created by other roles are not MOTOR data and
# are not readable by motor_app.
docker run --rm --env-file "$WORK/pg.env" \
    -v "$(pwd)/secrets/supabase-ca.crt:/run/secrets/supabase-ca.crt:ro" \
    -v "$WORK:/out" "$PG_CLIENT_IMAGE" sh -ec '
        psql -tA -c "select format(\$\$%I.%I\$\$, schemaname, tablename) from pg_tables where schemaname = \$\$public\$\$ and tableowner <> current_user" > /out/foreign-tables.txt
        set --
        while IFS= read -r t; do [ -n "$t" ] && set -- "$@" "--exclude-table=$t"; done < /out/foreign-tables.txt
        pg_dump --format=custom --schema=public --no-owner --no-privileges --compress=6 --file=/out/db.dump "$@"
    ' || fail "pg_dump failed"
[ -s "$WORK/foreign-tables.txt" ] && log "skipped tables not owned by the app role: $(tr '\n' ' ' < "$WORK/foreign-tables.txt")"
encrypt "$WORK/db.dump" "$OUT/db.dump.gpg" || fail "encrypting db dump failed"

# 2. Application data volume (read-only mount)
VOLUME=$(volume_name tenants)
docker volume inspect "$VOLUME" >/dev/null 2>&1 || fail "volume $VOLUME not found"
docker run --rm -v "$VOLUME:/data:ro" -v "$WORK:/out" alpine:3.20 \
    tar -C /data -czf /out/tenants.tar.gz . || fail "volume archive failed"
encrypt "$WORK/tenants.tar.gz" "$OUT/tenants.tar.gz.gpg" || fail "encrypting volume archive failed"

# 3. Checksums and metadata
(cd "$OUT" && sha256sum db.dump.gpg tenants.tar.gz.gpg > SHA256SUMS)
{
    echo "created_utc=$STAMP"
    echo "reason=$REASON"
    echo "image_tag=$(env_get MOTOR_IMAGE_TAG)"
    echo "db_dump_bytes=$(wc -c < "$WORK/db.dump")"
    echo "tenants_tar_bytes=$(wc -c < "$WORK/tenants.tar.gz")"
    echo "recipient=$RECIPIENT"
} > "$OUT/meta.txt"

# 4. Off-site copy
if [ -n "$REMOTE" ]; then
    [ -f secrets/rclone.conf ] || fail "MOTOR_BACKUP_RCLONE_REMOTE set but secrets/rclone.conf missing"
    docker run --rm -v "$(pwd)/secrets/rclone.conf:/config/rclone/rclone.conf:ro" \
        -v "$(pwd)/$OUT:/data:ro" rclone/rclone:1.68 \
        copy /data "$REMOTE/$STAMP" --immutable || fail "off-site copy failed"
    log "off-site copy -> $REMOTE/$STAMP"
fi

# 5. Local retention: only timestamped folders created by this script
find backups -mindepth 1 -maxdepth 1 -type d -name '20*T*Z' -mtime +"$KEEP_DAYS" -exec rm -rf {} +

log "done: $(du -sh "$OUT" | cut -f1)"
ping_url MOTOR_BACKUP_PING_URL ""
