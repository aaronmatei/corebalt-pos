<#
  Downloads the PORTABLE PostgreSQL Windows binaries (EDB) and the MSVC runtime, ready to be bundled
  into the store-server installer. Run once on the build machine before deploy/build-installers.ps1.

  Output:
    deploy/installer/pgsql/...           portable Postgres (bin, lib, share)
    deploy/installer/redist/vc_redist.x64.exe

  Usage:  powershell -ExecutionPolicy Bypass -File deploy/fetch-postgres.ps1 [-PgVersion 17.5-1] [-Prune]
#>
[CmdletBinding()]
param(
  [string] $PgVersion = "17.5-1",
  [switch] $Prune = $true
)
$ErrorActionPreference = 'Stop'
$installerDir = Join-Path $PSScriptRoot 'installer'
$pgsqlDir = Join-Path $installerDir 'pgsql'
$redistDir = Join-Path $installerDir 'redist'
New-Item -ItemType Directory -Force -Path $installerDir, $redistDir | Out-Null

# -- Portable Postgres -------------------------------------------------------------------------
$zipUrl = "https://get.enterprisedb.com/postgresql/postgresql-$PgVersion-windows-x64-binaries.zip"
$zip = Join-Path $env:TEMP "postgresql-$PgVersion-x64.zip"
if (-not (Test-Path (Join-Path $pgsqlDir 'bin\initdb.exe'))) {
  Write-Host "Downloading $zipUrl"
  Invoke-WebRequest -Uri $zipUrl -OutFile $zip
  Write-Host "Extracting -> $installerDir\pgsql"
  if (Test-Path $pgsqlDir) { Remove-Item $pgsqlDir -Recurse -Force }
  # The zip contains a top-level "pgsql/" folder.
  Expand-Archive -Path $zip -DestinationPath $installerDir -Force
  Remove-Item $zip -Force -ErrorAction SilentlyContinue
} else {
  Write-Host "Portable Postgres already present at $pgsqlDir"
}

if ($Prune) {
  # Drop what the runtime cluster never needs - shrinks the installer substantially.
  foreach ($d in 'doc','include','pgAdmin 4','StackBuilder','symbols') {
    $p = Join-Path $pgsqlDir $d
    if (Test-Path $p) { Remove-Item $p -Recurse -Force; Write-Host "Pruned $d" }
  }
}

# -- MSVC runtime (Postgres needs it; harmless if the target already has it) ---------------------
$vc = Join-Path $redistDir 'vc_redist.x64.exe'
if (-not (Test-Path $vc)) {
  Write-Host "Downloading vc_redist.x64.exe"
  Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" -OutFile $vc
}

Write-Host "Done. pgsql + redist ready under $installerDir" -ForegroundColor Green
