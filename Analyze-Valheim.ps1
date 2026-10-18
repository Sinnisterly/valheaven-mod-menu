param(
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Valheim',
    [string]$Workspace = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$managed = Join-Path $GamePath 'valheim_Data/Managed'
$tools = Join-Path $Workspace 'work/tools'
$reference = Join-Path $Workspace 'work/reference'
$decompiled = Join-Path $Workspace 'work/decompiled'
$publicized = Join-Path $Workspace 'work/publicized'
foreach ($folder in @($tools, $reference, $decompiled, $publicized)) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed with exit code $LASTEXITCODE" }
}
if (!(Test-Path -LiteralPath (Join-Path $tools 'ilspycmd.exe'))) {
    Invoke-Checked dotnet @('tool','install','ilspycmd','--version','11.0.0.9375','--tool-path',$tools,'--source','https://api.nuget.org/v3/index.json')
}
if (!(Test-Path -LiteralPath (Join-Path $tools 'assembly-publicizer.exe'))) {
    Invoke-Checked dotnet @('tool','install','BepInEx.AssemblyPublicizer.Cli','--version','0.4.3','--tool-path',$tools,'--source','https://api.nuget.org/v3/index.json')
}
$sources = @(Get-ChildItem -LiteralPath $managed -Filter '*.dll')
if ($sources.Count -eq 0) { throw "No managed assemblies found at $managed" }
$before = @{}
foreach ($file in $sources) {
    $before[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $file.FullName -Destination $reference -Force
}
$targets = [ordered]@{
    'assembly_valheim.dll' = 'valheim'
    'Assembly-CSharp.dll' = 'csharp'
    'assembly_utils.dll' = 'utils'
}
foreach ($name in $targets.Keys) {
    $copy = Join-Path $reference $name
    Invoke-Checked (Join-Path $tools 'ilspycmd.exe') @('--disable-updatecheck','-p','-o',(Join-Path $decompiled $targets[$name]),'-r',$reference,$copy)
    Invoke-Checked (Join-Path $tools 'assembly-publicizer.exe') @($copy,'-o',$publicized,'--overwrite')
}
$manifest = foreach ($file in $sources) {
    $after = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $copyHash = (Get-FileHash -LiteralPath (Join-Path $reference $file.Name) -Algorithm SHA256).Hash
    if ($after -ne $before[$file.Name] -or $copyHash -ne $after) {
        throw "Assembly changed during analysis: $($file.Name)"
    }
    [pscustomobject]@{ Name=$file.Name; Bytes=$file.Length; SHA256=$after; SourceUnchanged=$true; CopyMatches=$true }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'assembly-manifest.json') -Encoding utf8
Write-Host "Analysis complete. Publicized copies are development references only: $publicized"
