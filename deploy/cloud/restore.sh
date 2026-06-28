#!/usr/bin/env bash
# DESTRUCTIVE restore of the Corebalt POS cloud tier from a backup pair.
#   usage: ./restore.sh <pos-YYYYMMDD-HHMMSS.dump> [dpkeys-YYYYMMDD-HHMMSS.tar.gz]
# Stops the API, restores Postgres (--clean), optionally restores the DP key ring, restarts the API.
set -euo pipefail

DUMP="${1:?usage: restore.sh <pos-*.dump> [dpkeys-*.tar.gz]}"
KEYS="${2:-}"
DB_CONTAINER="${POS_DB_CONTAINER:-cloud-db-1}"
API_CONTAINER="${POS_API_CONTAINER:-cloud-api-1}"
DPKEYS_VOLUME="${POS_DPKEYS_VOLUME:-cloud_dpkeys}"

[ -f "$DUMP" ] || { echo "no such dump: $DUMP"; exit 1; }
echo "About to OVERWRITE the live database in $DB_CONTAINER with: $DUMP"
[ -n "$KEYS" ] && echo "…and replace the DP key ring from: $KEYS"
read -r -p "Type 'yes' to proceed: " ok; [ "$ok" = "yes" ] || { echo "aborted"; exit 1; }

echo "stopping API so it can't write mid-restore…"
docker stop "$API_CONTAINER" >/dev/null

echo "restoring Postgres…"
docker exec -i "$DB_CONTAINER" pg_restore -U pos -d pos --clean --if-exists --no-owner < "$DUMP"

if [ -n "$KEYS" ]; then
    [ -f "$KEYS" ] || { echo "no such keys archive: $KEYS"; docker start "$API_CONTAINER" >/dev/null; exit 1; }
    echo "restoring DP key ring…"
    docker run --rm -v "$DPKEYS_VOLUME":/keys -v "$(cd "$(dirname "$KEYS")" && pwd)":/in alpine \
        sh -c "rm -rf /keys/* && tar xzf /in/$(basename "$KEYS") -C /keys"
fi

echo "starting API…"
docker start "$API_CONTAINER" >/dev/null
echo "restore complete. Check: curl -sS https://pos.corebalt.co.ke/healthz"
