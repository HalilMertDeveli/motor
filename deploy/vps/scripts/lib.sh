# MOTOR VPS scripts: shared helpers. Sourced, not executed.
# Expects the working directory to be deploy/vps.

die() { echo "$(basename "$0"): $*" >&2; exit 1; }
log() { echo "$(date -u +%FT%TZ) $(basename "$0"): $*"; }

# Reads KEY=value from .env without executing it (the file may contain ';', '$', spaces).
env_get() {
    sed -n "s/^$1=//p" "${MOTOR_ENV_FILE:-.env}" | tail -n 1
}

# Converts an Npgsql connection string into libpq environment variables for
# pg_dump/pg_restore. Only the keys MOTOR uses are mapped. Output is a list of
# "--env NAME=value" arguments for docker run, written to the file given in $2
# (never to stdout, so the password does not reach logs).
npgsql_to_docker_env() {
    printf '%s' "$1" | tr ';' '\n' | while IFS= read -r pair; do
        key=$(printf '%s' "${pair%%=*}" | tr '[:upper:]' '[:lower:]' | tr -d ' ')
        val=${pair#*=}
        case "$key" in
            host|server) echo "PGHOST=$val" ;;
            port) echo "PGPORT=$val" ;;
            database) echo "PGDATABASE=$val" ;;
            username|userid|user) echo "PGUSER=$val" ;;
            password) echo "PGPASSWORD=$val" ;;
            sslmode)
                mode=$(printf '%s' "$val" | tr '[:upper:]' '[:lower:]')
                case "$mode" in
                    verifyfull) mode=verify-full ;;
                    verifyca) mode=verify-ca ;;
                esac
                echo "PGSSLMODE=$mode" ;;
            rootcertificate) echo "PGSSLROOTCERT=$val" ;;
        esac
    done > "$2"
    chmod 600 "$2"
}

# Volume name of the compose project, e.g. motor_tenants
volume_name() {
    project=$(env_get MOTOR_PROJECT_NAME)
    echo "${project:-motor}_$1"
}

ping_url() {
    url=$(env_get "$1")
    [ -n "$url" ] || return 0
    curl -fsS -m 10 --retry 3 -o /dev/null "$url$2" || true
}
