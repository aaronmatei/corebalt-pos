<#
  FRESH-INSTALL provisioning for the Corebalt POS store server, invoked by the Inno Setup installer with
  admin rights. It is idempotent-safe but is only meant to run on a FIRST install (the installer skips it
  on upgrades). It:

    1. initdb's an ISOLATED, bundled-Postgres cluster under ProgramData on a DEDICATED port (not 5432),
    2. generates a STRONG random DB password + JWT signing key + this install's tenant/store GUIDs,
    3. registers + starts Postgres as its OWN Windows service and creates the `pos` database,
    4. writes the install-level appsettings.Production.json (secrets, LAN listen URL, Ops paths) and
       locks it down (Administrators + SYSTEM only),
    5. registers + starts the store-server Windows service (first start auto-migrates the empty DB), and
    6. opens the inbound LAN firewall port.

  Everything writable (the PG cluster, backups, logs, DP keys) lives under ProgramData - NEVER under
  Program Files, which is read-only for services. Run from the installer; not intended to be run by hand.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $InstallDir,   # Program Files\...\Store Server  (read-only binaries)
  [int]    $AppPort = 5080,                               # LAN listen port tills connect to
  [int]    $PgPort  = 5544,                               # dedicated, non-default Postgres port
  [string] $DataRoot = "$env:ProgramData\Corebalt POS"    # writable runtime root
)

$ErrorActionPreference = 'Stop'
$AppService = 'CorebaltPOS'
$PgService  = 'CorebaltPOSPostgres'
$PgAccount  = 'NT AUTHORITY\NetworkService'  # the PG service account (matches the EDB convention)

$pgBin     = Join-Path $InstallDir 'pgsql\bin'
$appExe    = Join-Path $InstallDir 'app\Pos.Api.exe'
$config    = Join-Path $InstallDir 'app\appsettings.Production.json'
$dataDir   = Join-Path $DataRoot 'data'
$backupDir = Join-Path $DataRoot 'backups'
$logDir    = Join-Path $DataRoot 'logs'
$keysDir   = Join-Path $DataRoot 'dp-keys'

New-Item -ItemType Directory -Force -Path $DataRoot, $backupDir, $logDir, $keysDir | Out-Null
Start-Transcript -Path (Join-Path $logDir 'install-provision.log') -Append | Out-Null
try {
  function New-Secret([int]$bytes) {
    $b = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    # URL/JSON-safe, no shell-hostile characters.
    [Convert]::ToBase64String($b).Replace('+','A').Replace('/','B').Replace('=','')
  }

  $dbPassword = New-Secret 24
  $jwtKey     = New-Secret 48           # >= 32 chars after encoding
  $tenantId   = [guid]::NewGuid().ToString()
  $storeId    = [guid]::NewGuid().ToString()

  # -- 1. initdb an isolated cluster -----------------------------------------------------------
  if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) {
    Write-Host "initdb -> $dataDir (port $PgPort)"
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
    $pwFile = Join-Path $env:TEMP "corebalt-pgpw-$([guid]::NewGuid().ToString('N')).txt"
    Set-Content -Path $pwFile -Value $dbPassword -NoNewline -Encoding ascii
    try {
      & (Join-Path $pgBin 'initdb.exe') -D $dataDir -U postgres --auth=scram-sha-256 --pwfile=$pwFile --encoding=UTF8
      if ($LASTEXITCODE -ne 0) { throw "initdb failed (exit $LASTEXITCODE)." }
    } finally { Remove-Item $pwFile -Force -ErrorAction SilentlyContinue }

    # Bind to localhost on the dedicated port (last assignment in the file wins).
    $conf = Join-Path $dataDir 'postgresql.conf'
    Add-Content -Path $conf -Value ''
    Add-Content -Path $conf -Value '# Corebalt POS - isolated cluster'
    Add-Content -Path $conf -Value "listen_addresses = 'localhost'"
    Add-Content -Path $conf -Value "port = $PgPort"
    Add-Content -Path $conf -Value 'password_encryption = scram-sha-256'
  } else {
    Write-Host "Existing cluster at $dataDir - leaving it untouched."
  }

  # The PG service account must own the cluster directory.
  & icacls $dataDir /grant "${PgAccount}:(OI)(CI)F" /T /Q | Out-Null

  # -- 2. register + start Postgres as its own service -----------------------------------------
  if (-not (Get-Service -Name $PgService -ErrorAction SilentlyContinue)) {
    Write-Host "Registering Postgres service '$PgService'"
    & (Join-Path $pgBin 'pg_ctl.exe') register -N $PgService -U $PgAccount -D $dataDir -S auto
    if ($LASTEXITCODE -ne 0) { throw "pg_ctl register failed (exit $LASTEXITCODE)." }
  }
  Start-Service -Name $PgService

  # Wait until it accepts connections.
  $ready = $false
  for ($i = 0; $i -lt 60; $i++) {
    & (Join-Path $pgBin 'pg_isready.exe') -h localhost -p $PgPort -q
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 1
  }
  if (-not $ready) { throw "Postgres did not become ready on port $PgPort." }

  # -- 3. create the pos database --------------------------------------------------------------
  $env:PGPASSWORD = $dbPassword
  try {
    $exists = (& (Join-Path $pgBin 'psql.exe') -h localhost -p $PgPort -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='pos'") 2>$null
    if ($exists -ne '1') {
      Write-Host "Creating database 'pos'"
      & (Join-Path $pgBin 'createdb.exe') -h localhost -p $PgPort -U postgres pos
      if ($LASTEXITCODE -ne 0) { throw "createdb failed (exit $LASTEXITCODE)." }
    }
  } finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }

  # -- 4. write + lock the install-level config ------------------------------------------------
  Write-Host "Writing $config"
  $settings = [ordered]@{
    ConnectionStrings = [ordered]@{ Pos = "Host=localhost;Port=$PgPort;Database=pos;Username=postgres;Password=$dbPassword" }
    Urls          = "http://0.0.0.0:$AppPort"
    StoreServer   = [ordered]@{ TenantId = $tenantId; StoreId = $storeId }
    Jwt           = [ordered]@{ Key = $jwtKey }
    Receipt       = [ordered]@{ NumberPrefix = 'MB' }
    Ops           = [ordered]@{
      AutoMigrate            = $true
      PgDumpPath             = (Join-Path $pgBin 'pg_dump.exe')
      BackupDirectory        = $backupDir
      LogDirectory           = $logDir
      DataProtectionKeysPath = $keysDir
    }
  }
  ($settings | ConvertTo-Json -Depth 6) | Set-Content -Path $config -Encoding utf8

  # Secrets in here (DB password, signing key) - restrict to Administrators + SYSTEM.
  & icacls $config /inheritance:r /grant:r "BUILTIN\Administrators:F" "NT AUTHORITY\SYSTEM:F" /Q | Out-Null
  & icacls $keysDir /inheritance:r /grant:r "BUILTIN\Administrators:F" "NT AUTHORITY\SYSTEM:F" /T /Q | Out-Null

  # -- 5. register + start the store-server service (first start auto-migrates) -----------------
  if (-not (Get-Service -Name $AppService -ErrorAction SilentlyContinue)) {
    Write-Host "Registering store-server service '$AppService'"
    & sc.exe create $AppService binPath= "`"$appExe`"" start= auto DisplayName= "Corebalt POS Store Server" | Out-Null
    & sc.exe description $AppService "Corebalt POS store server (API + back-office)." | Out-Null
    & sc.exe failure $AppService reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
  }

  # -- 6. open the inbound LAN firewall port ---------------------------------------------------
  & netsh advfirewall firewall delete rule name="Corebalt POS Store Server" 2>$null | Out-Null
  & netsh advfirewall firewall add rule name="Corebalt POS Store Server" dir=in action=allow protocol=TCP localport=$AppPort | Out-Null

  Start-Service -Name $AppService
  Write-Host "Provisioning complete. Back-office: http://localhost:$AppPort/"
}
catch {
  Write-Error "Provisioning FAILED: $($_.Exception.Message)"
  throw
}
finally {
  Stop-Transcript | Out-Null
}
