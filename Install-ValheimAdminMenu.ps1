param([string]$GamePath = 'D:\SteamLibrary\steamapps\common\Valheim')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$game = [IO.Path]::GetFullPath($GamePath)
$loader = Join-Path $game 'BepInEx/core/BepInEx.dll'
if (!(Test-Path -LiteralPath $loader)) { throw "BepInEx is missing at $game" }
if (!(Test-Path -LiteralPath (Join-Path $game 'valheim_Data/Managed/assembly_valheim.dll')) -and !(Test-Path -LiteralPath (Join-Path $game 'valheim_server_Data/Managed/assembly_valheim.dll'))) {
    throw 'The selected directory does not contain a Valheim managed installation.'
}
$source = Join-Path $PSScriptRoot 'ValheimAdminMenu.dll'
if (!(Test-Path -LiteralPath $source)) { throw "Release DLL missing: $source" }
$destination = Join-Path $game 'BepInEx/plugins/ValheimAdminMenu'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$target = Join-Path $destination 'ValheimAdminMenu.dll'
if (Test-Path -LiteralPath $target) {
    $backup = Join-Path $PSScriptRoot ('backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Copy-Item -LiteralPath $target -Destination (Join-Path $backup 'ValheimAdminMenu.dll')
}
Copy-Item -LiteralPath $source -Destination $target -Force
$expected = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw 'Installed DLL hash does not match the release.' }
Write-Host "Installed: $target"
Write-Host "SHA256: $actual"
Write-Host 'Restart Valheim, enter a test world, and press F8. Existing game assemblies and BepInEx core files were not replaced.'
