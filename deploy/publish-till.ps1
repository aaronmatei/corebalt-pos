<#
  Publish the Corebalt POS till (Avalonia) as a SELF-CONTAINED win-x64 build (bundles the .NET runtime).
  Single-folder output, ready for a per-lane install on each till PC.

  Usage:   pwsh deploy/publish-till.ps1 [-Output dist/till]
#>
param(
  [string]$Output = "dist/till",
  [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "Publishing till (self-contained win-x64) -> $Output" -ForegroundColor Cyan
dotnet publish "$root/src/Pos.Till/Pos.Till.csproj" `
  -c $Configuration -r win-x64 --self-contained true `
  -p:PublishSingleFile=false `
  -o "$root/$Output"

Write-Host "Done. Folder: $root/$Output" -ForegroundColor Green
Write-Host "Next: copy to each till PC, set Till:BaseUrl (the store server's LAN URL) + Till:RegisterId in appsettings.json, run Pos.Till.exe." -ForegroundColor Yellow
