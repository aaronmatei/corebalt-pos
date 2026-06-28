#!/usr/bin/env bash
# Nightly backup of the Corebalt POS cloud tier:
#   1. Postgres dump (custom format, compressed) of the shared multi-tenant DB
#   2. The Data-Protection key ring (cloud_dpkeys) — decrypts EVERY tenant's M-Pesa/eTIMS secrets,
#      so a DB backup without this is useless. THIS is the irreplaceable piece.
#   3. Integrity-check the dump, push off-VPS via rclone (recommended), and prune by retention.
#
# Run from cron on the VPS host. Config via env (all optional except a real offsite for production):
#   POS_BACKUP_DIR              local dir for dumps        (default /root/pos-backups)
#   POS_BACKUP_RETENTION_DAYS   keep this many days local  (default 14)
#   POS_BACKUP_RCLONE_REMOTE    rclone remote:path offsite (e.g. r2:corebalt-pos-backups; empty = local only)
#   POS_DB_CONTAINER            postgres container name     (default cloud-db-1)
#   POS_DPKEYS_VOLUME           DP key-ring volume          (default cloud_dpkeys)
set -euo pipefail

BACKUP_DIR="${POS_BACKUP_DIR:-/root/pos-backups}"
RETENTION_DAYS="${POS_BACKUP_RETENTION_DAYS:-14}"
RCLONE_REMOTE="${POS_BACKUP_RCLONE_REMOTE:-}"
DB_CONTAINER="${POS_DB_CONTAINER:-cloud-db-1}"
DPKEYS_VOLUME="${POS_DPKEYS_VOLUME:-cloud_dpkeys}"
STAMP="$(date +%Y%m%d-%H%M%S)"

mkdir -p "$BACKUP_DIR"
DUMP="$BACKUP_DIR/pos-$STAMP.dump"
KEYS="$BACKUP_DIR/dpkeys-$STAMP.tar.gz"

echo "[$(date -Is)] backup start → $BACKUP_DIR"

# 1. Postgres (pg_dump 17 inside the container matches the server version)
docker exec "$DB_CONTAINER" pg_dump -U pos -d pos -Fc > "$DUMP"

# 2. Data-Protection key ring
docker run --rm -v "$DPKEYS_VOLUME":/keys -v "$BACKUP_DIR":/out alpine \
    tar czf "/out/$(basename "$KEYS")" -C /keys . 2>/dev/null

# 3. Integrity check — a dump that won't list is a dump that won't restore
docker exec -i "$DB_CONTAINER" pg_restore --list < "$DUMP" > /dev/null
echo "[$(date -Is)] verified: $(basename "$DUMP") ($(du -h "$DUMP" | cut -f1)) + $(basename "$KEYS")"

# 4. Offsite (a same-VPS backup does NOT survive losing the VPS)
if [ -n "$RCLONE_REMOTE" ]; then
    rclone copy "$DUMP" "$RCLONE_REMOTE" --no-traverse
    rclone copy "$KEYS" "$RCLONE_REMOTE" --no-traverse
    echo "[$(date -Is)] offsite → $RCLONE_REMOTE"
else
    echo "[$(date -Is)] WARNING: POS_BACKUP_RCLONE_REMOTE unset — backups are LOCAL ONLY (no VPS-loss protection)"
fi

# 5. Local retention
find "$BACKUP_DIR" -name 'pos-*.dump'      -mtime +"$RETENTION_DAYS" -delete
find "$BACKUP_DIR" -name 'dpkeys-*.tar.gz' -mtime +"$RETENTION_DAYS" -delete

echo "[$(date -Is)] backup ok"
