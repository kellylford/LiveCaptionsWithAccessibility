# Builds and locally registers the Windows AI flavor of Accessible Live Captions:
# the MSIX-packaged build whose extra "Windows on-device" engine uses the OS speech
# recognizer (NPU-accelerated on Copilot+ PCs) via Microsoft.Windows.AI.Speech.
#
# Prerequisites on the target machine:
#   * Windows 11 24H2 (build 26100) or later.
#   * Developer Mode ON (Settings > System > For developers) - required to register
#     an unsigned loose-file package.
#
# Usage:  powershell -ExecutionPolicy Bypass -File packaging\build-windows-ai.ps1
# Re-run after code changes; it re-registers the updated layout in place.

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $repo "bin\windows-ai\publish"

Write-Host "Publishing the Windows AI flavor..." -ForegroundColor Cyan
dotnet publish (Join-Path $repo "AccessibleLiveCaptions.csproj") `
    -c Release -r win-arm64 --self-contained false `
    -p:WindowsAI=true -p:WindowsAppSDKSelfContained=true `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# The manifest must sit at the layout root as AppxManifest.xml.
Copy-Item (Join-Path $PSScriptRoot "Package.appxmanifest") (Join-Path $publish "AppxManifest.xml") -Force

Copy-Item (Join-Path $PSScriptRoot "Assets") $publish -Recurse -Force

Write-Host "Registering the package (requires Developer Mode)..." -ForegroundColor Cyan
Add-AppxPackage -Register (Join-Path $publish "AppxManifest.xml") -ForceUpdateFromAnyVersion

Write-Host "Done. Launch 'Accessible Live Captions' from the Start menu." -ForegroundColor Green
Write-Host "The Audio > Engine menu now offers 'Windows on-device - NPU'."
