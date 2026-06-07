<#
  Writes the till's appsettings.json from the installer's answers: the store-server LAN address and the
  lane number. A stable RegisterId GUID identifies this lane to the server - it is PRESERVED across
  re-installs (re-running the installer keeps the same lane identity) and generated only on first install.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $InstallDir,
  [Parameter(Mandatory = $true)] [string] $ServerUrl,   # "192.168.1.10:5080" or a full http URL
  [int] $Lane = 1,
  [string] $Currency = 'KES'
)

$ErrorActionPreference = 'Stop'
$config = Join-Path $InstallDir 'appsettings.json'

# Normalize the server address to a URL.
$url = $ServerUrl.Trim()
if ($url -notmatch '^https?://') { $url = "http://$url" }

# Preserve an existing RegisterId so a re-install keeps the same lane identity on the server.
$registerId = [guid]::NewGuid().ToString()
if (Test-Path $config) {
  try {
    $existing = Get-Content $config -Raw | ConvertFrom-Json
    if ($existing.Till -and $existing.Till.RegisterId) { $registerId = [string]$existing.Till.RegisterId }
  } catch { } # unreadable/garbage config - fall back to a new id
}

$settings = [ordered]@{
  Till = [ordered]@{
    BaseUrl    = $url
    RegisterId = $registerId
    LaneNumber = $Lane      # operator-facing label; the server still auto-numbers "Lane N" on first sale
    Currency   = $Currency
  }
}
($settings | ConvertTo-Json -Depth 5) | Set-Content -Path $config -Encoding utf8
Write-Host "Till configured: server $url, lane $Lane, register $registerId"
