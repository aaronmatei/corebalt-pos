<#
  Publish the Corebalt POS store server (API + Blazor back-office) as a SELF-CONTAINED win-x64 build
  (bundles the .NET runtime — the client machine has no developer tools). Single-folder output, ready
  to copy to the store-server box and run as a Windows Service.

  Usage:   pwsh deploy/publish-server.ps1 [-Output dist/store-server]
#>
param(
  [string]$Output = "dist/store-server",
  [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "Publishing store server (self-contained win-x64) -> $Output" -ForegroundColor Cyan
dotnet publish "$root/src/Pos.Api/Pos.Api.csproj" `
  -c $Configuration -r win-x64 --self-contained true `
  -p:PublishSingleFile=false `
  -o "$root/$Output"

# Ship the install-config template (the installer fills it in as appsettings.Production.json).
Copy-Item "$root/src/Pos.Api/appsettings.Production.json.template" "$root/$Output/" -Force

Write-Host "Done. Folder: $root/$Output" -ForegroundColor Green
Write-Host "Next: copy the folder to the store-server box, fill appsettings.Production.json, then install the service:" -ForegroundColor Yellow
Write-Host '  sc.exe create "CorebaltPOS" binPath= "C:\Corebalt\store-server\Pos.Api.exe" start= auto' -ForegroundColor Yellow
