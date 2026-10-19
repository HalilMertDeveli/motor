#!/usr/bin/env sh
# MOTOR: restore a backup into an ISOLATED target for verification or recovery.
#
#   deploy/vps/scripts/restore.sh <backup-dir> <private-key.asc> <target-env-file> <target-volume>
#
#   backup-dir        backups/<timestamp> (or a copy fetched from off-site storage)
#   private-key.asc   owner's GPG private key, brought to the server only for the restore
#   target-env-file   file with one line MOTOR_RESTORE_DB_CONNECTION_STRING=<Npgsql string>
#                     of an EMPTY database (e.g. a fresh Supabase branch/project)
#   target-volume     name of a NEW docker volume for the restored App_Data/Tenants
#
# Safety: refuses to run against the live connection string from .env, refuses
# an existing target volume, and refuses a non-empty target database. It never
# writes to the production database or volume. Switching production over to a
# restored target is a manual, separate decision (docs/motor/vps-operations.md).
set -eu
umask 077

cd "$(dirname "$0")/.."
. scripts/lib.sh

[ $# -eq 4 ] || die "usage: restore.sh <backup-dir> <private-key.asc> <target-env-file> <target-volume>"
BACKUP=$1; KEY=$2; TARGET_ENV=$3; TARGET_VOLUME=$4

[ -f "$BACKUP/db.dump.gpg" ] && [ -f "$BACKUP/tenants.tar.gz.gpg" ] || die "$BACKUP is not a MOTOR backup"
[ -f "$KEY" ] || die "private key $KEY not found"
[ -f "$TARGET_ENV" ] || die "target env file $TARGET_ENV not found"

TARGET_CS=$(MOTOR_ENV_FILE=$TARGET_ENV env_get MOTOR_RESTORE_DB_CONNECTION_STRING)
[ -n "$TARGET_CS" ] || die "MOTOR_RESTORE_DB_CONNECTION_STRING missing in $TARGET_ENV"
if [ -f .env ] && [ "$TARGET_CS" = "$(env_get MOTOR_DB_CONNECTION_STRING)" ]; then
    die "target is the live production database; refusing"
fi
if docker volume inspect "$TARGET_VOLUME" >/dev/null 2>&1; then
    die "volume $TARGET_VOLUME exists; choose a new name"
fi
case "$TARGET_VOLUME" in
    "$(volume_name tenants)") die "target volume is the live volume; refusing" ;;
esac

PG_CLIENT_IMAGE=$(env_get MOTOR_BACKUP_PG_IMAGE 2>/dev/null || true); PG_CLIENT_IMAGE=${PG_CLIENT_IMAGE:-postgres:17-alpine}

WORK=$(mktemp -d)
GPG_HOME=""
trap 'rm -rf "$WORK" ${GPG_HOME:+"$GPG_HOME"}' EXIT
# gpg needs a local filesystem for its agent socket, so not inside $WORK.
GPG_HOME=$(mktemp -d /tmp/motor-gnupg.XXXXXX)
GNUPGHOME=$GPG_HOME; export GNUPGHOME

log "verifying checksums"
(cd "$BACKUP" && sha256sum -c SHA256SUMS) || die "checksum mismatch"

gpg --batch --quiet --import "$KEY" || die "cannot import private key"
gpg --batch --quiet --output "$WORK/db.dump" --decrypt "$BACKUP/db.dump.gpg" || die "cannot decrypt db dump"
gpg --batch --quiet --output "$WORK/tenants.tar.gz" --decrypt "$BACKUP/tenants.tar.gz.gpg" || die "cannot decrypt volume archive"

npgsql_to_docker_env "$TARGET_CS" "$WORK/pg.env"
CA_MOUNT=""
[ -f secrets/supabase-ca.crt ] && CA_MOUNT="-v $(pwd)/secrets/supabase-ca.crt:/run/secrets/supabase-ca.crt:ro"

log "checking that the target database is empty"
# shellcheck disable=SC2086
TABLES=$(docker run --rm --env-file "$WORK/pg.env" $CA_MOUNT "$PG_CLIENT_IMAGE" \
    psql -tAc "select count(*) from pg_tables where schemaname = 'public'") || die "cannot connect to target database"
[ "$TABLES" = "0" ] || die "target database has $TABLES tables in public; refusing"

log "restoring database"
# shellcheck disable=SC2086
docker run --rm --env-file "$WORK/pg.env" $CA_MOUNT -v "$WORK:/in:ro" "$PG_CLIENT_IMAGE" \
    sh -ec '
        # Schema public already exists on the target (and motor_app may not create schemas):
        # restore everything except its CREATE SCHEMA and COMMENT ON SCHEMA entries.
        pg_restore --list /in/db.dump | grep -vE "^[0-9]+; [0-9]+ [0-9]+ (SCHEMA - public|COMMENT - SCHEMA public) " > /tmp/restore.list
        pg_restore --no-owner --no-privileges --exit-on-error --use-list=/tmp/restore.list --dbname="$PGDATABASE" /in/db.dump
    ' \
    || die "pg_restore failed"

log "restoring volume into $TARGET_VOLUME"
docker volume create "$TARGET_VOLUME" >/dev/null
docker run --rm -v "$TARGET_VOLUME:/data" -v "$WORK:/in:ro" alpine:3.20 \
    tar -C /data -xzf /in/tenants.tar.gz || die "volume restore failed"

# shellcheck disable=SC2086
COUNTS=$(docker run --rm --env-file "$WORK/pg.env" $CA_MOUNT "$PG_CLIENT_IMAGE" \
    psql -tA -F ' ' -c "select (select count(*) from pg_tables where schemaname = 'public'), (select count(*) from \"Product\"), (select count(*) from \"Order\"), (select count(*) from \"Customer\")")
set -- $COUNTS
log "restored: ${1:-?} tables, ${2:-?} products, ${3:-?} orders, ${4:-?} customers; volume $TARGET_VOLUME"
log "next: run 02-verify-exposure.sql on the target and start a test stack against it"
