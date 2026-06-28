# Hosting the POS as a multi-tenant cloud SaaS (`pos.corebalt.co.ke`)

The cloud tier runs the **same `Pos.Api` binary** as the on-prem store server, switched into **HQ mode**
(`Deployment:Mode=Hq`). Each retailer (tenant) gets a subdomain: `acme.pos.corebalt.co.ke`. The active
tenant is resolved from that subdomain on every request; data is isolated per tenant in one shared
Postgres.

> **Status:** Phase 1 (the multi-tenant app) and Phase 2a (store→cloud sync of completed **sales**) are
> done and tested. On-prem store servers push their outbox to the cloud, which projects sales into the
> **Synced sales** back-office page. Other read-models (stock, cash-up sessions) are the next Phase 2 step.

---

## 0. The Cloudflare TLS gotcha (read first)

Cloudflare's free **Universal SSL** covers `corebalt.co.ke` and `*.corebalt.co.ke` — but **NOT**
`*.pos.corebalt.co.ke` (a second-level wildcard). So tenant subdomains won't get a valid Cloudflare edge
cert on the free plan.

**Our approach:** Cloudflare for **DNS only** on the `*.pos` records (grey cloud), and the origin (Caddy)
obtains a free Let's Encrypt **wildcard** cert for `*.pos.corebalt.co.ke` via the ACME **DNS-01**
challenge using a Cloudflare API token. No per-tenant cert work, ever.

---

## 1. Cloudflare DNS records

In the `corebalt.co.ke` zone, point both the apex of the POS namespace and its wildcard at your VPS IP.
Set them **DNS only (grey cloud)** so the origin's wildcard cert is what browsers see:

| Type | Name                  | Content        | Proxy        |
|------|-----------------------|----------------|--------------|
| A    | `pos`                 | `<VPS_IP>`     | DNS only 🌫️  |
| A    | `*.pos`               | `<VPS_IP>`     | DNS only 🌫️  |

Then create a **scoped API token** (My Profile → API Tokens → Create Token) with
**Zone › DNS › Edit** on `corebalt.co.ke`. Caddy uses it only for the DNS-01 challenge.

> You *can* later put the apex `pos` behind the orange cloud if you buy Cloudflare Advanced Certificate
> Manager, but it's not needed — the origin serves valid TLS for everything.

---

## 2. The VPS

Any small Linux box with Docker + Docker Compose, ports **80** and **443** open. 2 vCPU / 4 GB is plenty
to start.

```bash
git clone <this repo> && cd pos/deploy/cloud
cp .env.example .env
# Fill .env:
#   TENANT_BASE_DOMAIN=pos.corebalt.co.ke
#   POSTGRES_PASSWORD   = openssl rand -base64 36
#   JWT_KEY             = openssl rand -base64 48
#   ADMIN_API_TOKEN     = openssl rand -hex 32
#   CLOUDFLARE_API_TOKEN= <the scoped token from step 1>
#   ACME_EMAIL          = ops@corebalt.co.ke

docker compose -f docker-compose.cloud.yml up -d --build
```

What comes up:
- **db** — Postgres 17 (shared, one DB for all tenants; data on the `pgdata` volume).
- **api** — the HQ host; auto-applies EF migrations on first boot (empty DB → straight through).
- **caddy** — fetches the `*.pos.corebalt.co.ke` wildcard cert via DNS-01 and reverse-proxies to the API,
  forwarding the original `Host` header so subdomain resolution works.

Check it: `curl https://pos.corebalt.co.ke/healthz` → `{"status":"ok"}`.

> ⚠️ **Back up the `dpkeys` volume.** The Data-Protection key ring there decrypts every tenant's M-Pesa /
> eTIMS secrets — lose it and those secrets are unrecoverable.

---

## 3. Onboard a tenant (admin-provisioned)

Tenants are created by you (the vendor), not self-serve. From anywhere with the admin token:

```bash
curl -sS https://pos.corebalt.co.ke/admin/tenants \
  -H "X-Admin-Token: $ADMIN_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "slug": "acme",
        "displayName": "Acme Supermarket",
        "kraPin": "P051234567X",
        "managerUsername": "manager",
        "managerPassword": "ChangeMe!123"
      }'
```

This creates the subdomain registry row, the merchant profile, baseline entitlements, and the first
manager — all in one transaction, and returns a one-time **`syncToken`** in the response. The tenant is
now live at **`https://acme.pos.corebalt.co.ke`**, and a manager signing in there sees **"Acme
Supermarket"** in the back office.

> **Save the `syncToken`** — it's shown only once. Configure it on that tenant's on-prem store server(s)
> so they can push sales up (see "Connect an on-prem store" below). The cloud stores only its hash.

- List tenants: `GET /admin/tenants` with the same header.
- Slugs are validated (DNS-safe, 2–63 chars) and reserved names (`www`, `admin`, `api`, `pos`, …) are
  refused. Unknown subdomains return 404.
- Optional onboarding fields: `vatRegistered`, `vatNumber`, `phone`, `email`, `address`, `currency`,
  `licenseKey` (a Corebalt-signed entitlements key — unlocks paid features).

---

## 4. Operating notes

- **Upgrades:** `git pull && docker compose -f docker-compose.cloud.yml up -d --build`. The API takes a
  `pg_dump` backup of the populated DB before applying any new migration (and refuses to start if that
  backup fails).
- **Backups:** the `pgdata`/`backups`/`dpkeys` volumes are the state. Snapshot them (or the whole VPS)
  regularly; offsite the dumps.
- **Cookies/HTTPS:** back-office sessions use host-only cookies, so a session on `acme.pos…` is never
  sent to `globex.pos…`. All traffic is HTTPS at Caddy.
- **Secrets:** never commit `.env` or any filled `appsettings.Production.json`.

---

## 5. Connect an on-prem store (push sales to the cloud)

On each on-prem store server (the StoreServer-mode install), add an `HqSync` block to
`appsettings.Production.json` and restart the service:

```json
"HqSync": {
  "Enabled": true,
  "CloudBaseUrl": "https://pos.corebalt.co.ke",
  "TenantSlug": "acme",
  "SyncToken": "hqs_…the token from onboarding…",
  "IntervalSeconds": 15,
  "BatchSize": 200
}
```

The store's `HqSyncPushWorker` then ships its transactional outbox to the cloud every interval and the
cloud projects it per branch into back-office pages:
- completed **sales** → `hq_sales` (**Synced sales**)
- **returns/refunds** → `hq_credit_notes` (**Synced returns**)
- **stock-on-hand** → `hq_stock_on_hand` (**Stock on hand**) — a running sum of movement deltas
- closed **cash-up shifts** (Z) → `hq_sessions` (**Branch cash-ups**)

The store acks only what the cloud durably accepted (at-least-once, idempotent, NAT-friendly — the store
always initiates). The cloud manager can confirm every branch is pushing on the **Sync status** page
(last-received time + a stale warning per branch).

If a sync token leaks, rotate it (the old one stops working immediately):

```bash
curl -sS -X POST https://pos.corebalt.co.ke/admin/tenants/acme/rotate-sync-token \
  -H "X-Admin-Token: $ADMIN_API_TOKEN"   # → { "slug": "acme", "syncToken": "hqs_…" }
```

> Do NOT also enable the optional `CorebaltErp` sale forwarder on the same store — both consume the same
> outbox `processed_at_utc` marker and would each miss what the other drains.

## 5b. Backups (do this before real customers)

The cloud state is two volumes: `cloud_pgdata` (the DB) and **`cloud_dpkeys`** (the Data-Protection key
ring that decrypts every tenant's M-Pesa/eTIMS secrets — losing it is unrecoverable). `deploy/cloud/backup.sh`
dumps both, verifies, prunes by retention, and (recommended) pushes off-VPS via rclone.

```bash
# one-time: install rclone + configure an offsite remote (Cloudflare R2 is a natural fit)
curl https://rclone.org/install.sh | sudo bash
rclone config                      # create a remote, e.g. name it "r2" → S3-compatible → R2 keys/endpoint
chmod +x ~/corebalt-pos/deploy/cloud/backup.sh ~/corebalt-pos/deploy/cloud/restore.sh

# test a run now
POS_BACKUP_RCLONE_REMOTE="r2:corebalt-pos-backups" ~/corebalt-pos/deploy/cloud/backup.sh

# schedule nightly at 02:30 (crontab -e)
30 2 * * * POS_BACKUP_RCLONE_REMOTE="r2:corebalt-pos-backups" /root/corebalt-pos/deploy/cloud/backup.sh >> /var/log/pos-backup.log 2>&1
```

**Restore** (destructive — stops the API, restores DB + key ring, restarts):
```bash
~/corebalt-pos/deploy/cloud/restore.sh /root/pos-backups/pos-YYYYMMDD-HHMMSS.dump \
                                        /root/pos-backups/dpkeys-YYYYMMDD-HHMMSS.tar.gz
```
> Test a restore at least once (e.g. on a scratch box) — an untested backup isn't a backup.

## 6. What's next (rest of Phase 2 / M1)

Sales, returns, stock-on-hand and cash-up sessions all sync today. Remaining hardening: central
catalog/pricing push HQ→store (M2), inter-branch transfers (M3), a dedicated reprojector to backfill new
read-models from the durable `sync_inbox` without re-syncing, and per-store sync-token rotation. The
product name on the stock page comes denormalized from the movement push (the store enriches each movement
with the product's Sku/Name); a full catalog projection would also cover never-moved products.
