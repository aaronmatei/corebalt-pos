# Corebalt POS installers (Inno Setup)

Turnkey on-prem installers for a non-technical retailer — one for the **store server**, one for the
**till**. Builds on the part‑1 self-contained publishes + the Windows-Service host + safe
auto-migration. No developer tools required on the client machine.

## Build the installers (on a build machine)

Prereqs: **.NET 10 SDK**, **[Inno Setup 6](https://jrsoftware.org/isdl.php)** (`ISCC.exe`), and internet
access the first time (to download portable Postgres).

```powershell
powershell -ExecutionPolicy Bypass -File deploy/fetch-postgres.ps1      # once: portable PG + vc_redist
powershell -ExecutionPolicy Bypass -File deploy/build-installers.ps1 -Version 1.0.0
# -> dist/installers/CorebaltPOS-StoreServer-1.0.0.exe
#    dist/installers/CorebaltPOS-Till-1.0.0.exe
```

`build-installers.ps1` publishes both apps self-contained, ensures the portable Postgres is staged under
`deploy/installer/pgsql`, and compiles both `.iss` scripts.

## Store-server installer (branch server / back-office PC, run as admin)

On a **fresh** install it:
1. installs the self-contained server to `Program Files\Corebalt POS\Store Server\app`;
2. unpacks the **bundled portable Postgres** to `…\pgsql` and `initdb`s an **isolated cluster** under
   `C:\ProgramData\Corebalt POS\data` on a **dedicated port (5544)** with a **generated strong password**
   — no system-wide Postgres, no 5432 conflict;
3. registers Postgres as its own service (`CorebaltPOSPostgres`, NetworkService) and creates `pos`;
4. writes the locked-down install config `app\appsettings.Production.json` (connection string, LAN
   `Urls`, generated `Jwt:Key`, this install's tenant/store GUIDs, `Ops` paths) — Administrators+SYSTEM only;
5. registers the store-server service (`CorebaltPOS`, auto-start) and starts it — **first start
   auto-migrates the empty DB** (fresh → no backup needed);
6. opens the inbound LAN firewall port (default **5080**);
7. offers to open `http://localhost:5080/` → the operator lands in the **setup wizard**.

**Upgrade** (re-run a newer build over the same folder): stops the service, replaces only `app\` (and the
provisioning script), restarts — the service's safe auto-migration takes a **pre-migration `pg_dump`
backup** then applies pending migrations. Config, the database, and backups are **preserved** (the
portable PG binaries and the config are not re-written on upgrade).

**Uninstall**: stops + removes both services and the app binaries, but **keeps the database and backups**
in `C:\ProgramData\Corebalt POS` (the client's data) and tells the operator where they are.

All provisioning is done by `installer/scripts/provision-server.ps1` (a transcript is written to
`…\Corebalt POS\logs\install-provision.log`).

## Till installer (each lane PC, run as admin)

Installs the self-contained till + Start-Menu/desktop shortcuts, then prompts for the **store-server
address** (`host:port`) and the **lane number**, writing them to `appsettings.json`
(`installer/scripts/provision-till.ps1`). A stable `RegisterId` GUID is generated once and **preserved**
on re-install. The till holds no data — uninstall removes the app only.

## Defaults

| Thing | Default | Service |
| --- | --- | --- |
| Store-server listen | `http://0.0.0.0:5080` | `CorebaltPOS` |
| Postgres | `localhost:5544` (isolated cluster) | `CorebaltPOSPostgres` |
| Data / backups / logs / DP keys | `C:\ProgramData\Corebalt POS\*` | — |

## ⚠️ Mandatory clean-VM test (do NOT validate on a dev box)

A dev machine already has the .NET runtime, the MSVC runtime, and (often) a conflicting Postgres on 5432,
so it cannot prove the turnkey path. Test end-to-end on a **fresh Windows 10/11 VM**:

**Server VM**
1. Snapshot a clean Windows VM (no .NET, no Postgres). Copy `CorebaltPOS-StoreServer-x.y.z.exe`.
2. Run it as admin; accept the default port. The wizard finishes without errors.
3. Verify both services are **Running**: `Get-Service CorebaltPOS, CorebaltPOSPostgres`.
4. Verify the cluster + DB: `…\pgsql\bin\psql -h localhost -p 5544 -U postgres -l` shows `pos`; and
   `schema-version.json` (in the install dir) lists the latest migration.
5. Verify the firewall rule exists and `http://localhost:5080/` opens the **setup wizard**; complete it.
6. From another LAN machine, browse `http://<server-ip>:5080/` to confirm LAN reachability.

**Till VM/PC**
7. On a second machine, run `CorebaltPOS-Till-x.y.z.exe`; enter `<server-ip>:5080` and a lane number.
8. Launch the till, PIN-login the manager created in the wizard, **open a shift, ring a sale, close the
   shift** — confirming a LAN till transacts against the server end to end.

**Upgrade / uninstall**
9. Re-run a higher-version server installer → service restarts, data intact, a backup is written if the
   build added migrations.
10. Uninstall the server → services gone, `C:\ProgramData\Corebalt POS` (DB + backups) preserved.
