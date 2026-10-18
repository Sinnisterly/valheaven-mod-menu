param([string]$GamePath = 'D:\SteamLibrary\steamapps\common\Valheim')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Invoke-Checked([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with code $LASTEXITCODE" }
}
Invoke-Checked @('build', (Join-Path $PSScriptRoot 'ValheimAdminMenu/ValheimAdminMenu.csproj'), '-c', 'Release', '--nologo', ('-p:GamePath=' + $GamePath))
Invoke-Checked @('build', (Join-Path $PSScriptRoot 'Validation/ValheimAdminMenu.Validation.csproj'), '-c', 'Release', '--nologo', ('-p:GamePath=' + $GamePath))
Invoke-Checked @((Join-Path $PSScriptRoot 'Validation/bin/Release/net8.0/ValheimAdminMenu.Validation.dll'), $GamePath)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ValheimAdminMenu/bin/Release/net472/ValheavenModMenu.dll') -Destination (Join-Path $PSScriptRoot 'ValheavenModMenu.dll') -Force
$payloadDir = Join-Path $PSScriptRoot 'ValheimAdminMenu/bin/Release/net472/payload'
if (Test-Path -LiteralPath $payloadDir) {
    Copy-Item -LiteralPath (Join-Path $payloadDir 'ValheavenModMenu.dll') -Destination (Join-Path $PSScriptRoot 'ValheavenPayload-Menu.dll') -Force
    Copy-Item -LiteralPath (Join-Path $payloadDir '0Harmony.dll') -Destination (Join-Path $PSScriptRoot 'ValheavenPayload-Harmony.dll') -Force
}
Write-Host 'Managed build and offline checks passed. Build Native/VersionProxy for version.dll, then Install-Valheaven.ps1.'
