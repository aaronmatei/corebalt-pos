<#
  One-shot build of both Corebalt POS installers on a build machine:
    1. publish the store server + till (self-contained win-x64, part 1),
    2. ensure the portable Postgres + MSVC runtime are present (deploy/fetch-postgres.ps1),
    3. compile both Inno Setup scripts -> dist/installers.

  Prereqs: .NET 10 SDK, Inno Setup 6 (ISCC.exe), and an internet connection the first time (for the
  portable Postgres download). Usage:  powershell -ExecutionPolicy Bypass -File deploy/build-installers.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
  [string] $Version = "1.0.0"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$installerDir = Join-Path $PSScriptRoot 'installer'

function Find-ISCC {
  $candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"  # winget user-scope install
  )
  foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
  $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  throw "Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php"
}

Write-Host "== 1/3 publish self-contained apps ==" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'publish-server.ps1')
& (Join-Path $PSScriptRoot 'publish-till.ps1')

Write-Host "== 2/3 portable Postgres + MSVC runtime ==" -ForegroundColor Cyan
if (-not (Test-Path (Join-Path $installerDir 'pgsql\bin\initdb.exe'))) {
  & (Join-Path $PSScriptRoot 'fetch-postgres.ps1')
} else {
  Write-Host "pgsql already present."
}

Write-Host "== 3/3 compile installers ==" -ForegroundColor Cyan
$iscc = Find-ISCC
New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist\installers') | Out-Null
# ISCC is a native exe — a failed compile returns non-zero but does NOT stop the script, so check it
# explicitly or a broken .iss ships silently (and only one of the two installers would be produced).
& $iscc "/dMyAppVersion=$Version" (Join-Path $installerDir 'store-server.iss')
if ($LASTEXITCODE -ne 0) { throw "store-server.iss failed to compile (ISCC exit $LASTEXITCODE)." }
& $iscc "/dMyAppVersion=$Version" (Join-Path $installerDir 'till.iss')
if ($LASTEXITCODE -ne 0) { throw "till.iss failed to compile (ISCC exit $LASTEXITCODE)." }

Write-Host "Done. Installers in: $root\dist\installers" -ForegroundColor Green
Get-ChildItem (Join-Path $root 'dist\installers') -Filter *.exe | ForEach-Object { Write-Host "  $($_.Name)" }
