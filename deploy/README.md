# Deploying Corebalt POS (on-prem Windows)

App-side foundation for the ops track. The MSI/installer + scheduled backups come next; this makes the
apps **installable** on a retailer's Windows machines with **no developer tools on the client**.

## Build the packages (on a dev/build machine)

```powershell
pwsh deploy/publish-server.ps1   # -> dist/store-server  (self-contained win-x64, runs as a service)
pwsh deploy/publish-till.ps1     # -> dist/till          (self-contained win-x64, per-lane)
```

Both are **self-contained** (the .NET runtime is bundled) and **single-folder**. Copy the folder to the
target machine — nothing else to install except Postgres on the store server.

## Store server (one per branch)

1. Install **PostgreSQL** locally on a **dedicated non-default port** (e.g. 5544) with a generated
   password — do not rely on 5432/defaults (clients hit port conflicts too).
2. Copy `dist/store-server` to the box (e.g. `C:\Corebalt\store-server`).
3. Fill **`appsettings.Production.json`** from `appsettings.Production.json.template` (DB connection
   string, `Urls` LAN bind, `StoreServer` identity, `Jwt:Key`, `Ops` paths + `Ops:PgDumpPath`). This file
   holds secrets and is **never committed**.
4. Open the listen port (e.g. 5080) in the Windows firewall so tills can reach it.
5. Install + start the Windows Service (runs headless, starts on boot):

   ```powershell
   sc.exe create "CorebaltPOS" binPath= "C:\Corebalt\store-server\Pos.Api.exe" start= auto
   sc.exe description "CorebaltPOS" "Corebalt POS store server (API + back-office)"
   sc.exe start "CorebaltPOS"
   ```

   The same binary runs as a console app for diagnostics — just run `Pos.Api.exe` in a terminal.

On start the service **safely auto-migrates**: if the database already holds client data and migrations
are pending, it takes a timestamped `pg_dump` backup to `Ops:BackupDirectory` **first**; if the backup
fails it refuses to migrate and the service start fails loudly. The applied schema version is recorded to
`schema-version.json`. Structured rolling logs go to `Ops:LogDirectory` (31 days retained).

## Till (one per lane)

1. Copy `dist/till` to the till PC (e.g. `C:\Corebalt\till`).
2. Set `Till:BaseUrl` to the store server's LAN URL (e.g. `http://192.168.1.10:5080`) and `Till:RegisterId`
   (a unique GUID per lane) in `appsettings.json`.
3. Run `Pos.Till.exe` (add a Startup shortcut for kiosk use).

## Default ports / paths

| Thing | Default | Set in |
| --- | --- | --- |
| Store-server listen | `http://0.0.0.0:5080` | `Urls` |
| Postgres | `localhost:5544` | `ConnectionStrings:Pos` |
| Logs / backups / DP keys | under the install folder, or `C:\ProgramData\Corebalt POS\*` | `Ops:*` |
