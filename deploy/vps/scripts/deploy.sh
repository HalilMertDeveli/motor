#!/usr/bin/env sh
# MOTOR: deploy an approved image tag on the VPS, with automatic rollback.
#
#   deploy/vps/scripts/deploy.sh sha-1a2b3c4      # deploy that tag
#   deploy/vps/scripts/deploy.sh --rollback       # back to the previous tag
#
# Steps: validate config, back up (if backup.sh exists), pull, recreate the
# web container, wait for the health check, smoke-test https://<domain>/.
# On failure the previous tag is started again. Never touches the database
# schema by itself: Smartstore runs its migrations at startup, which is why a
# database backup is taken first.
set -eu

cd "$(dirname "$0")/.."
STATE_DIR=state
mkdir -p "$STATE_DIR"

die() { echo "deploy: $*" >&2; exit 1; }

[ -f .env ] || die ".env missing (copy .env.example)"
[ -f secrets/supabase-ca.crt ] || die "secrets/supabase-ca.crt missing"

current_tag() { sed -n 's/^MOTOR_IMAGE_TAG=//p' .env | tail -n 1; }
set_tag() {
    # Rewrite only the MOTOR_IMAGE_TAG line; keep the file's permissions.
    tmp=$(mktemp .env.XXXXXX)
    chmod 600 "$tmp"
    sed "s/^MOTOR_IMAGE_TAG=.*/MOTOR_IMAGE_TAG=$1/" .env > "$tmp"
    cat "$tmp" > .env
    rm -f "$tmp"
}

case "${1:-}" in
    "") die "usage: deploy.sh <image-tag> | --rollback" ;;
    --rollback)
        [ -s "$STATE_DIR/previous-tag" ] || die "no previous tag recorded"
        NEW_TAG=$(cat "$STATE_DIR/previous-tag") ;;
    *) NEW_TAG=$1 ;;
esac

case "$NEW_TAG" in
    latest|"") die "refusing mutable or empty tag '$NEW_TAG'" ;;
    *[!A-Za-z0-9._-]*) die "invalid tag '$NEW_TAG'" ;;
esac

OLD_TAG=$(current_tag)
echo "deploy: $OLD_TAG -> $NEW_TAG"

if [ -x scripts/backup.sh ]; then
    echo "deploy: pre-deploy backup"
    scripts/backup.sh --reason "pre-deploy $NEW_TAG" || die "backup failed, nothing deployed"
fi

set_tag "$NEW_TAG"
docker compose config --quiet || { set_tag "$OLD_TAG"; die "compose config invalid"; }
# MOTOR_IMAGE_PULL=never: the image is built on this server (scripts/build-image.sh),
# so it must exist locally instead of being pulled from the registry.
if [ "$(sed -n 's/^MOTOR_IMAGE_PULL=//p' .env | tail -n 1)" = "never" ]; then
    image=$(sed -n 's/^MOTOR_IMAGE=//p' .env | tail -n 1)
    docker image inspect "$image:$NEW_TAG" >/dev/null 2>&1 || { set_tag "$OLD_TAG"; die "image $image:$NEW_TAG not built on this server"; }
else
    docker compose pull web || { set_tag "$OLD_TAG"; die "pull failed, still on $OLD_TAG"; }
fi

wait_healthy() {
    cid=$(docker compose ps -q web)
    [ -n "$cid" ] || return 1
    # start_period is 180s; allow up to 6 minutes for migrations on first start of a version.
    i=0
    while [ $i -lt 72 ]; do
        status=$(docker inspect -f '{{.State.Health.Status}}' "$cid" 2>/dev/null || echo missing)
        case "$status" in
            healthy) return 0 ;;
            unhealthy|missing) return 1 ;;
        esac
        i=$((i + 1))
        sleep 5
    done
    return 1
}

smoke_test() {
    domain=$(sed -n 's/^MOTOR_DOMAIN=//p' .env | tail -n 1 | cut -d';' -f1)
    # MOTOR_SMOKE_URL / MOTOR_SMOKE_INSECURE: test stacks on other ports or with a private CA only.
    url=$(sed -n 's/^MOTOR_SMOKE_URL=//p' .env | tail -n 1)
    url=${url:-https://$domain/}
    insecure=""
    [ "$(sed -n 's/^MOTOR_SMOKE_INSECURE=//p' .env | tail -n 1)" = "1" ] && insecure="-k"
    code=$(curl -s $insecure -o /dev/null -w '%{http_code}' --max-time 30 \
        -A "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 motor-deploy" \
        "$url")
    echo "deploy: $url -> $code"
    [ "$code" = "200" ]
}

docker compose up -d --remove-orphans

if wait_healthy && smoke_test; then
    [ "$OLD_TAG" != "$NEW_TAG" ] && echo "$OLD_TAG" > "$STATE_DIR/previous-tag"
    echo "$(date -u +%FT%TZ) $NEW_TAG ok" >> "$STATE_DIR/deploy.log"
    echo "deploy: $NEW_TAG is live"
    exit 0
fi

echo "deploy: $NEW_TAG failed health or smoke test; rolling back to $OLD_TAG" >&2
docker compose logs --tail 100 web >&2 || true
echo "$(date -u +%FT%TZ) $NEW_TAG failed, rolled back to $OLD_TAG" >> "$STATE_DIR/deploy.log"
set_tag "$OLD_TAG"
docker compose up -d --remove-orphans
wait_healthy && echo "deploy: rolled back to $OLD_TAG" >&2
echo "deploy: if the new version ran migrations, check the database before retrying (docs/motor/vps-operations.md)" >&2
exit 1
