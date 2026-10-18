# Stage menu + Harmony binaries for version.dll RCDATA embedding.
param(
    [string]$PayloadDir = '',
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($PayloadDir)) {
    $PayloadDir = Join-Path $RepoRoot 'ValheimAdminMenu/bin/Release/net472/payload'
}
$menu = Join-Path $PayloadDir 'ValheavenModMenu.dll'
$harmony = Join-Path $PayloadDir '0Harmony.dll'
if (!(Test-Path -LiteralPath $menu)) { throw "Menu DLL not found: $menu. Build ValheimAdminMenu (Release) first." }
if (!(Test-Path -LiteralPath $harmony)) { throw "Harmony DLL not found: $harmony. Build ValheimAdminMenu (Release) first." }

$native = $PSScriptRoot
Copy-Item -LiteralPath $menu -Destination (Join-Path $native 'menu.bin') -Force
Copy-Item -LiteralPath $harmony -Destination (Join-Path $native 'harmony.bin') -Force
Write-Host "Wrote menu.bin ($((Get-Item (Join-Path $native 'menu.bin')).Length) bytes) and harmony.bin ($((Get-Item (Join-Path $native 'harmony.bin')).Length) bytes)"
Write-Host 'Rebuild Native/VersionProxy so payload.rc embeds both into version.dll.'
