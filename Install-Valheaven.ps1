param([string]$GamePath = 'D:\SteamLibrary\steamapps\common\Valheim')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$game = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
if (!(Test-Path -LiteralPath (Join-Path $game 'valheim.exe'))) { throw 'valheim.exe not found in the selected game directory.' }

$source = Join-Path $PSScriptRoot 'version.dll'
if (!(Test-Path -LiteralPath $source)) { throw 'version.dll not found beside installer. Build Native/VersionProxy first.' }

$destination = [IO.Path]::GetFullPath((Join-Path $game 'version.dll'))
if (!$destination.StartsWith($game + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Install path escaped the specified game directory.' }

$pluginDir = Join-Path $game 'BepInEx/plugins/ValheavenModMenu'
$legacyDir = Join-Path $game 'BepInEx/plugins/ValheimAdminMenu'
$pluginDll = Join-Path $pluginDir 'ValheavenModMenu.dll'
$legacyDll = Join-Path $legacyDir 'ValheimAdminMenu.dll'

$backup = Join-Path $PSScriptRoot ('backups/Valheaven-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $backup | Out-Null
if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination (Join-Path $backup 'version.dll') }
if (Test-Path -LiteralPath $pluginDll) { Copy-Item -LiteralPath $pluginDll -Destination (Join-Path $backup 'ValheavenModMenu.dll') }
if (Test-Path -LiteralPath $legacyDll) { Copy-Item -LiteralPath $legacyDll -Destination (Join-Path $backup 'ValheimAdminMenu.dll') }

Copy-Item -LiteralPath $source -Destination $destination -Force
$expected = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) { throw 'Installed hash does not match release.' }

# Avoid two menus: remove old BepInEx Valheaven plugins (do not touch other BepInEx mods / Doorstop).
foreach ($path in @($pluginDll, $legacyDll)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}

Write-Host "Installed Valheaven single-file injector: $destination"
Write-Host "Previous files backed up to: $backup"
Write-Host "SHA256: $expected"
Write-Host 'Sidecars (created on first run): Valheaven.settings.json, Valheaven.bookmarks.json, Valheaven.blueprints.json, Valheaven.log next to valheim.exe (or %AppData%\Valheaven\).'
Write-Host 'Restart Valheim. F8 opens the menu unless you previously saved another key. Other BepInEx mods are left alone.'
