# Corebalt POS — production deployment & operations runbook

This is the **as-built** record of the live multi-tenant cloud deployment at `pos.corebalt.co.ke`,
co-hosted on the same VPS as the Corebalt ERP. It captures the real architecture, the exact steps, the
operational recipes, and the gotchas we hit (so you don't re-hit them).

> `CLOUD.md` describes the clean greenfield path (a dedicated VPS with the bundled Caddy owning 80/443).
> **This document overrides it** for the actual co-hosted-with-ERP setup. `INSTALLER.md` covers the
> Windows store-server installer in depth.

---

## 1. Architecture (as deployed)

```
                         Cloudflare DNS (grey-cloud / DNS-only)
   pos.corebalt.co.ke ─┐   *.pos.corebalt.co.ke ─┐
                       ▼                          ▼
                 VPS 167.233.110.202  (one box, two Corebalt products)
   ┌───────────────────────────────────────────────────────────────────────┐
   │  erp-caddy-1  (Caddy container — owns :80/:443, the ONLY front proxy)   │
   │     ├─ erp.corebalt.co.ke / *.erp / api-erp …  → ERP services           │
   │     └─ pos.corebalt.co.ke / *.pos …            → cloud-api-1:8080  ◄──┐  │
   │  networks: erp_default                                               │  │
   ├─────────────────────────────────────────────────────────────────────┼──┤
   │  cloud-api-1  (Pos.Api, Deployment:Mode=Hq)   on erp_default + cloud_default
   │  cloud-db-1   (Postgres 17, shared multi-tenant DB)                      │
   └───────────────────────────────────────────────────────────────────────┘
```

Key facts:
- The **POS does NOT run its own Caddy.** `erp-caddy-1` is the single proxy; the POS API is attached to
  its network (`erp_default`) and reached as `cloud-api-1:8080`.
- TLS for `*.pos.corebalt.co.ke` is **on-demand** (Caddy mints a cert the first time a tenant subdomain
  is hit). erp-caddy has **no Cloudflare DNS plugin**, so this is HTTP-01 / TLS-ALPN-01, not a DNS wildcard.
- Caddy's on-demand `ask` is **global** (one endpoint for the whole proxy). It points at the POS's
  `/hq/tls-check`, which validates POS tenants and **delegates non-POS hosts back to the ERP's checker**
  (`Deployment:TlsCheckDelegateUrl=http://backend:8080/public/tenant-host-check`). So one shared ask
  authorizes certs for both products.
- **Coupling to know:** the ERP's new-cert issuance now passes through `cloud-api-1`. If the POS is down,
  *existing* certs keep working (cached) but *new* tenant certs (both products) pause until it's back.

| Thing | Value |
|---|---|
| VPS IP | `167.233.110.202` |
| POS base domain | `pos.corebalt.co.ke` |
| Front proxy | container `erp-caddy-1`, network `erp_default` |
| ERP Caddyfile (host) | `/root/corebalt/deploy/Caddyfile` (single-file bind mount — see gotcha #1) |
| POS repo on VPS | `~/corebalt-pos` |
| POS compose | `~/corebalt-pos/deploy/cloud/docker-compose.cloud.yml` (+ `.override.yml`) |
| POS containers | `cloud-api-1`, `cloud-db-1` |
| State volumes | `cloud_pgdata` (DB), **`cloud_dpkeys`** (key ring — back this up!) |

---

## 2. Cloudflare DNS

In the `corebalt.co.ke` zone, both **DNS only (grey cloud)** so the origin serves its own TLS:

| Type | Name    | Content            | Proxy    |
|------|---------|--------------------|----------|
| A    | `pos`   | `167.233.110.202`  | DNS only |
| A    | `*.pos` | `167.233.110.202`  | DNS only |

---

## 3. Deploy / upgrade the POS cloud (on the VPS)

```bash
cd ~/corebalt-pos && git pull
cd deploy/cloud

# .env holds POSTGRES_PASSWORD, JWT_KEY, ADMIN_API_TOKEN, TENANT_BASE_DOMAIN, ACME_EMAIL  (gitignored)
# The override attaches the API to the ERP network AND sets the shared-ask delegate. Do NOT run the
# bundled caddy service.
cat > docker-compose.cloud.override.yml <<'EOF'
networks:
  erp_default:
    external: true
services:
  api:
    networks: [default, erp_default]
    environment:
      Deployment__TlsCheckDelegateUrl: "http://backend:8080/public/tenant-host-check"
EOF

docker compose -f docker-compose.cloud.yml -f docker-compose.cloud.override.yml up -d --build db api
docker exec erp-caddy-1 wget -qO- --header="Host: pos.corebalt.co.ke" http://cloud-api-1:8080/healthz   # {"status":"ok"}
```
(That builds only `db`+`api` — never `caddy`. `git pull` + the same command is also the **upgrade** path;
the API auto-migrates the DB on start, backing up first if data exists.)

---

## 4. Reverse-proxy integration (one-time, on the ERP Caddyfile)

Edit `/root/corebalt/deploy/Caddyfile`, then **`docker restart erp-caddy-1`** (see gotcha #1 — a reload
will NOT pick up edits on this single-file mount).

1. Repoint the global on-demand `ask` to the POS (it delegates ERP hosts back to the ERP backend):
   ```
   on_demand_tls {
       ask http://cloud-api-1:8080/hq/tls-check
   }
   ```
2. Add the POS apex + wildcard:
   ```
   pos.corebalt.co.ke {
       reverse_proxy cloud-api-1:8080
   }
   *.pos.corebalt.co.ke {
       tls { on_demand }
       reverse_proxy cloud-api-1:8080
   }
   ```
**Before** repointing the global ask, validate the combined check still serves the ERP:
```bash
chk() { docker exec erp-caddy-1 wget -qO- "http://cloud-api-1:8080/hq/tls-check?domain=$1" >/dev/null 2>&1 && echo "$1 ALLOW" || echo "$1 refuse"; }
chk acme.pos.corebalt.co.ke      # ALLOW (POS tenant)
chk <an-erp-tenant>.erp.corebalt.co.ke   # ALLOW (delegated to ERP backend) — must pass or the ERP loses cert issuance
```
Rollback: `cp /root/corebalt/deploy/Caddyfile.bak /root/corebalt/deploy/Caddyfile && docker restart erp-caddy-1`.

---

## 5. Onboard a tenant (auto-TLS — no Caddy edits)

```bash
ADMIN=$(grep ADMIN_API_TOKEN ~/corebalt-pos/deploy/cloud/.env | cut -d= -f2)
curl -sS https://pos.corebalt.co.ke/admin/tenants -H "X-Admin-Token: $ADMIN" -H "Content-Type: application/json" \
  -d '{"slug":"acme","displayName":"Acme Supermarket","kraPin":"P051234567X","managerUsername":"manager","managerPassword":"<set>"}'
# → returns syncToken (save it). The tenant is live at https://acme.pos.corebalt.co.ke;
#   the cert auto-issues on first visit. Have the manager change their password on first login.
```
- List tenants: `GET /admin/tenants` (same header).
- Rotate a leaked sync token: `POST /admin/tenants/{slug}/rotate-sync-token` (old token dies immediately).

---

## 6. Connect a store server (per branch)

Stores run on-prem (Windows) via the installer and push to the cloud. See `INSTALLER.md` for the build,
then:
1. Install `CorebaltPOS-StoreServer-x.y.z.exe` (Administrator) on the store box → provisions service +
   portable Postgres + opens the back-office.
2. Back-office `/setup` wizard → merchant profile + first manager → create cashier PIN + products.
3. Connect to the cloud (one elevated command — ships with the installer):
   ```powershell
   powershell -ExecutionPolicy Bypass -File "C:\Program Files\Corebalt POS\Store Server\scripts\connect-cloud.ps1" `
     -TenantSlug acme -SyncToken hqs_xxxx
   ```
4. Install the till, ring a sale → within ~15s it shows on `acme.pos.corebalt.co.ke` → **Synced sales** /
   **Sync status**. Each branch is its own install (own `StoreId`), all using the tenant's one sync token.

---

## 7. Backups (nightly, offsite) — do this before real customers

State = `cloud_pgdata` + **`cloud_dpkeys`** (the key ring that decrypts every tenant's M-Pesa/eTIMS
secrets; a DB dump without it is useless). `deploy/cloud/backup.sh` handles both.

```bash
curl https://rclone.org/install.sh | sudo bash
rclone config            # remote "r2" → S3-compatible → Cloudflare R2 (bucket corebalt-pos-backups)
chmod +x ~/corebalt-pos/deploy/cloud/backup.sh ~/corebalt-pos/deploy/cloud/restore.sh

POS_BACKUP_RCLONE_REMOTE="r2:corebalt-pos-backups" ~/corebalt-pos/deploy/cloud/backup.sh   # test run
# cron (crontab -e), 02:30 nightly:
30 2 * * * POS_BACKUP_RCLONE_REMOTE="r2:corebalt-pos-backups" /root/corebalt-pos/deploy/cloud/backup.sh >> /var/log/pos-backup.log 2>&1
```
Restore (destructive): `deploy/cloud/restore.sh <pos-*.dump> <dpkeys-*.tar.gz>`. **Test a restore once.**

---

## 8. Operate

- **Is sync healthy?** Each tenant's back-office → **Sync status** page (per-branch last-received +
  staleness). Or `docker exec cloud-db-1 psql -U pos -d pos -c "select store_id, count(*), max(received_at_utc) from sync_inbox group by store_id;"`.
- **Logs:** `docker compose -f docker-compose.cloud.yml logs -f api` (and `… logs caddy` on erp-caddy for TLS).
- **Secrets:** `.env` (gitignored) + the locked install configs. The `ADMIN_API_TOKEN` is platform-root.
- **Upgrade:** `git pull` then the §3 `up -d --build` line. Auto-migrates (backs up first if populated).

---

## 9. Gotchas we actually hit (and the fixes)

1. **Single-file Caddyfile bind mount → `caddy reload` ignores edits.** Editing the file with an editor
   (nano/vim/`sed -i`) changes the inode; the container keeps the old one. **Always `docker restart
   erp-caddy-1` after editing**, or append inode-safely with `cat >>`. Verify with
   `docker exec erp-caddy-1 grep <host> /etc/caddy/Caddyfile`.
2. **TLS "internal error" for a POS host** = Caddy has no cert: usually the host isn't an active tenant
   (on-demand `ask` refused it → onboard it first), or the block/edit didn't reach the running config (#1).
3. **Container can't reach the cloud** for sync → it was a wrong `HqSync__SyncToken` giving **401** (not a
   network issue; a 401 means it *reached* the cloud). Rotate the token and re-set it. (Hairpin to the
   public URL from a container on this VPS works fine.)
4. **Shared on-demand `ask`:** never point the global `ask` at the POS without first deploying
   `/hq/tls-check` AND confirming `chk <erp-host>` returns ALLOW — otherwise the ERP silently loses
   new-cert issuance.
5. **Port 80 may be firewalled** at the provider; that's fine — Caddy falls back to TLS-ALPN-01 on 443.
6. **Don't keep a per-tenant EXPLICIT Caddy block once `*.pos` on-demand is enabled.** A host that matches
   BOTH an explicit `acme.pos.corebalt.co.ke { … }` block AND the `*.pos` on-demand block gets two
   conflicting TLS automation policies and its handshake wedges (TLS internal error) — even though the
   cert is on disk. Tenants with NO explicit block (served purely by `*.pos` on-demand) work fine. Fix:
   delete the explicit per-tenant blocks, keep only the `pos.corebalt.co.ke` apex + the `*.pos` wildcard,
   `docker restart erp-caddy-1`. (We hit this on `acme` after it had been added explicitly during early
   debugging; `globex` — wildcard-only — was unaffected.)
7. **`cloud-api-1` is multi-homed** (cloud_default + erp_default); Docker's embedded DNS can occasionally
   return "server misbehaving" resolving it from erp-caddy. If it recurs, give it a stable alias on
   erp_default (`aliases: [pos-api]` in the override) and point Caddy's `reverse_proxy`/`ask` at `pos-api`.
