# Scaffold build for Valheaven version.dll (x64). Requires CMake + MSVC.
param(
    [string]$Config = 'RelWithDebInfo',
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-CMake {
    $cmd = Get-Command cmake -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $vs = & $vswhere -latest -products * -property installationPath
        if ($vs) {
            $candidate = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }
    foreach ($p in @(
        'C:\Program Files\CMake\bin\cmake.exe',
        'C:\Program Files (x86)\CMake\bin\cmake.exe'
    )) {
        if (Test-Path -LiteralPath $p) { return $p }
    }
    throw 'cmake.exe not found. Install Visual Studio CMake tools or add cmake to PATH.'
}

$cmake = Resolve-CMake
& (Join-Path $PSScriptRoot 'Embed-Payload.ps1') -RepoRoot $RepoRoot

$build = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null
Push-Location $build
try {
    & $cmake -G 'Visual Studio 17 2022' -A x64 ..
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed: $LASTEXITCODE" }
    & $cmake --build . --config $Config
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed: $LASTEXITCODE" }
}
finally { Pop-Location }

$out = Join-Path $build "$Config\version.dll"
$pdb = Join-Path $build "$Config\version.pdb"
if (!(Test-Path -LiteralPath $out)) { 
    $out = Join-Path $build 'version.dll' 
    $pdb = Join-Path $build 'version.pdb'
}
if (Test-Path -LiteralPath $out) {
    Copy-Item -LiteralPath $out -Destination (Join-Path $RepoRoot 'version.dll') -Force
    Write-Host "Ship artifact: $(Join-Path $RepoRoot 'version.dll')"
    if (Test-Path -LiteralPath $pdb) {
        Copy-Item -LiteralPath $pdb -Destination (Join-Path $RepoRoot 'version.pdb') -Force
        Write-Host "Ship artifact: $(Join-Path $RepoRoot 'version.pdb')"
    }
} else {
    Write-Warning 'version.dll not found after build; check CMake output paths.'
}
