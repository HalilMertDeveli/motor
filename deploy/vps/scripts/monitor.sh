#!/usr/bin/env sh
# MOTOR: periodic self-check on the VPS (systemd timer every 5 minutes).
#
# Checks: app container healthy, proxy running, public HTTPS answers, TLS
# certificate valid for > 14 days, disk usage, age of the last backup.
# Reports to MOTOR_MONITOR_PING_URL (healthchecks.io style: "" = ok,
# "/fail" = problem, body = details). Without a ping URL it only logs.
#
# An external uptime monitor (outside the VPS) is still required: this script
# cannot report when the whole server is down.
set -u

cd "$(dirname "$0")/.."
. scripts/lib.sh

PROBLEMS=""
problem() { PROBLEMS="$PROBLEMS$1; "; }

PROJECT=$(env_get MOTOR_PROJECT_NAME); PROJECT=${PROJECT:-motor}
DOMAIN=$(env_get MOTOR_DOMAIN | cut -d';' -f1)
DISK_MAX=$(env_get MOTOR_MONITOR_DISK_MAX_PERCENT); DISK_MAX=${DISK_MAX:-85}
BACKUP_MAX_HOURS=$(env_get MOTOR_MONITOR_BACKUP_MAX_HOURS); BACKUP_MAX_HOURS=${BACKUP_MAX_HOURS:-26}

web=$(docker compose ps -q web 2>/dev/null)
if [ -z "$web" ]; then
    problem "web container not running"
else
    health=$(docker inspect -f '{{.State.Health.Status}}' "$web" 2>/dev/null || echo unknown)
    [ "$health" = "healthy" ] || problem "web health=$health"
fi

caddy=$(docker compose ps -q caddy 2>/dev/null)
[ -n "$caddy" ] && [ "$(docker inspect -f '{{.State.Running}}' "$caddy" 2>/dev/null)" = "true" ] || problem "caddy not running"

if [ -n "$DOMAIN" ]; then
    code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 \
        -A "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 motor-monitor" \
        "https://$DOMAIN/") || true
    code=${code:-000}
    [ "$code" = "200" ] || problem "https://$DOMAIN/ -> $code"

    end=$(echo | openssl s_client -servername "$DOMAIN" -connect "$DOMAIN:443" 2>/dev/null | openssl x509 -noout -enddate 2>/dev/null | cut -d= -f2)
    if [ -n "$end" ]; then
        end_s=$(date -d "$end" +%s 2>/dev/null || echo 0)
        days=$(( (end_s - $(date +%s)) / 86400 ))
        [ "$days" -gt 14 ] || problem "TLS certificate expires in $days days"
    else
        problem "cannot read TLS certificate"
    fi
fi

used=$(df -P / | awk 'NR==2 {gsub("%", "", $5); print $5}')
[ "${used:-0}" -lt "$DISK_MAX" ] || problem "disk / at ${used}%"
used_docker=$(df -P /var/lib/docker 2>/dev/null | awk 'NR==2 {gsub("%", "", $5); print $5}')
[ "${used_docker:-0}" -lt "$DISK_MAX" ] || problem "disk /var/lib/docker at ${used_docker}%"

latest=$(find backups -mindepth 1 -maxdepth 1 -type d -name '20*T*Z' 2>/dev/null | sort | tail -n 1)
if [ -z "$latest" ] || [ ! -f "$latest/SHA256SUMS" ]; then
    problem "no complete backup found"
else
    age_h=$(( ($(date +%s) - $(stat -c %Y "$latest/SHA256SUMS")) / 3600 ))
    [ "$age_h" -le "$BACKUP_MAX_HOURS" ] || problem "last backup ${age_h}h old"
fi

url=$(env_get MOTOR_MONITOR_PING_URL)
if [ -z "$PROBLEMS" ]; then
    log "ok"
    [ -n "$url" ] && curl -fsS -m 10 --retry 3 -o /dev/null "$url" || true
    exit 0
fi

log "PROBLEM: $PROBLEMS"
[ -n "$url" ] && curl -fsS -m 10 --retry 3 -o /dev/null --data-raw "$PROBLEMS" "$url/fail" || true
exit 1
