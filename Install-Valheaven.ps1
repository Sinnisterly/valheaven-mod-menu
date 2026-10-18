param([string]$GamePath = 'D:\SteamLibrary\steamapps\common\Valheim')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$game = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
if (!(Test-Path -LiteralPath (Join-Path $game 'BepInEx/core/BepInEx.dll'))) { throw 'BepInEx is missing in the selected game directory.' }
$source = Join-Path $PSScriptRoot 'ValheavenModMenu.dll'
if (!(Test-Path -LiteralPath $source)) { $source = Join-Path $PSScriptRoot 'BepInEx/plugins/ValheavenModMenu/ValheavenModMenu.dll' }
if (!(Test-Path -LiteralPath $source)) { throw 'Release DLL not found beside installer or in the release package.' }
$destination = [IO.Path]::GetFullPath((Join-Path $game 'BepInEx/plugins/ValheavenModMenu/ValheavenModMenu.dll'))
$legacy = [IO.Path]::GetFullPath((Join-Path $game 'BepInEx/plugins/ValheimAdminMenu/ValheimAdminMenu.dll'))
foreach ($target in @($destination,$legacy)) {
    if (!$target.StartsWith($game + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Plugin path escaped the specified game directory.' }
}
$backup = Join-Path $PSScriptRoot ('backups/Valheaven-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $backup,(Split-Path -Parent $destination) | Out-Null
if (Test-Path -LiteralPath $legacy) { Copy-Item -LiteralPath $legacy -Destination (Join-Path $backup 'ValheimAdminMenu.dll') }
if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination (Join-Path $backup 'ValheavenModMenu.dll') }
if ([IO.Path]::GetFullPath($source) -ne $destination) { Copy-Item -LiteralPath $source -Destination $destination -Force }
$expected=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) { throw 'Installed hash does not match release.' }
if (Test-Path -LiteralPath $legacy) { Remove-Item -LiteralPath $legacy }
Write-Host "Installed Valheaven Mod Menu 2.1.0: $destination"
Write-Host "Previous DLL backed up to: $backup"
Write-Host "SHA256: $expected"
Write-Host 'Restart Valheim. F8 opens the menu unless you previously saved another key. Keybind and persistence controls are on the Settings page.'
