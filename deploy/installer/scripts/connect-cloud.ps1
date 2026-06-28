<#
  Connect an installed Corebalt POS STORE SERVER to the cloud (HQ) tier. Adds/updates the HqSync block
  in the install's appsettings.Production.json and restarts the service so the store starts pushing its
  sales/stock/returns/cash-ups up. Run in an ELEVATED (Administrator) PowerShell on the store machine.

  Example:
    powershell -ExecutionPolicy Bypass -File connect-cloud.ps1 -TenantSlug acme -SyncToken hqs_abc123...
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $TenantSlug,   # the tenant's slug in the cloud (e.g. acme)
  [Parameter(Mandatory = $true)] [string] $SyncToken,    # the syncToken returned at cloud onboarding
  [string] $CloudBaseUrl   = 'https://pos.corebalt.co.ke',
  [string] $InstallDir     = (Join-Path ${env:ProgramFiles} 'Corebalt POS\Store Server'),
  [int]    $IntervalSeconds = 15,
  [int]    $BatchSize       = 200,
  [string] $Service         = 'CorebaltPOS'
)
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw "Run this in an elevated (Administrator) PowerShell — it writes Program Files config and restarts a service."
}

$config = Join-Path $InstallDir 'app\appsettings.Production.json'
if (-not (Test-Path $config)) {
  throw "Config not found at '$config'. Is the store server installed? Pass -InstallDir if it lives elsewhere."
}

Write-Host "Updating $config"
$json = Get-Content -Raw -Path $config | ConvertFrom-Json

$hq = [ordered]@{
  Enabled         = $true
  CloudBaseUrl    = $CloudBaseUrl
  TenantSlug      = $TenantSlug
  SyncToken       = $SyncToken
  IntervalSeconds = $IntervalSeconds
  BatchSize       = $BatchSize
}
if ($json.PSObject.Properties.Name -contains 'HqSync') { $json.HqSync = $hq }
else { $json | Add-Member -NotePropertyName HqSync -NotePropertyValue $hq }

($json | ConvertTo-Json -Depth 8) | Set-Content -Path $config -Encoding utf8

# Re-lock — the file holds the DB password, signing key and now the sync token.
& icacls $config /inheritance:r /grant:r "BUILTIN\Administrators:F" "NT AUTHORITY\SYSTEM:F" /Q | Out-Null

Write-Host "Restarting '$Service'…"
Restart-Service -Name $Service -Force

Write-Host ""
Write-Host "Done — this store now pushes to $CloudBaseUrl as tenant '$TenantSlug'." -ForegroundColor Green
Write-Host "Verify on the cloud's Sync status page, or tail the store log under the install's 'logs' folder."
Write-Host "To disconnect later: set HqSync.Enabled=false in the config and restart '$Service'."
