# Mirror release/valheaven-full-credit onto release/forum-credit, then apply forum-only credits.
# Does not push. Commit is optional (-Commit) so authorship stays on your machine.
param(
    [string]$SourceBranch = 'release/valheaven-full-credit',
    [string]$ForumBranch = 'release/forum-credit',
    [string]$ForumCredit = 'Loathecsgo',
    [switch]$Commit,
    [switch]$Push,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
Set-Location -LiteralPath $root

function Invoke-Git([string[]]$Args) {
    & git @Args
    if ($LASTEXITCODE -ne 0) { throw "git $($Args -join ' ') failed: $LASTEXITCODE" }
}

$status = @(git status --porcelain)
if ($status.Count -gt 0 -and -not $Force) {
    throw "Working tree is dirty. Commit or stash on $SourceBranch first, or pass -Force (discards local changes when resetting forum)."
}

$current = (git rev-parse --abbrev-ref HEAD).Trim()
Write-Host "Current branch: $current"
Write-Host "Mirroring $SourceBranch -> $ForumBranch (credit: $ForumCredit)"

Invoke-Git @('fetch', 'origin', $SourceBranch, $ForumBranch)
Invoke-Git @('checkout', $ForumBranch)
# Histories may be unrelated; hard reset is the intended mirror.
Invoke-Git @('reset', '--hard', $SourceBranch)

$localeDir = Join-Path $root 'ValheimAdminMenu\Locales'
$keys = @('chrome.credits_short', 'settings.credits.note', 'search.credits.kw')
$changed = @()
Get-ChildItem -LiteralPath $localeDir -Filter '*.json' | ForEach-Object {
    $path = $_.FullName
    $text = [IO.File]::ReadAllText($path)
    $orig = $text
    foreach ($key in $keys) {
        $pattern = '"(' + [regex]::Escape($key) + ')"\s*:\s*"[^"]*"'
        $replacement = '"$1": "' + $ForumCredit.Replace('\', '\\').Replace('"', '\"') + '"'
        $text = [regex]::Replace($text, $pattern, $replacement)
    }
    if ($text -ne $orig) {
        [IO.File]::WriteAllText($path, $text)
        $changed += $_.Name
    }
}

if ($changed.Count -eq 0) {
    Write-Warning "No locale credit keys were rewritten. Check Locales JSON keys."
} else {
    Write-Host ("Patched credits in: " + ($changed -join ', '))
}

Invoke-Git @('add', '--', 'ValheimAdminMenu/Locales')
Write-Host ''
Write-Host 'Forum mirror ready (locales staged).'

if ($Commit) {
    $msg = "Forum credit: $ForumCredit only (mirror of $SourceBranch)."
    Invoke-Git @('commit', '-m', $msg)
    Write-Host "Committed as: $(git log -1 --format='%an <%ae>')"
} else {
    Write-Host 'Commit yourself (keeps authorship on your account):'
    Write-Host "  git commit -m `"Forum credit: $ForumCredit only (mirror of $SourceBranch).`""
}

if ($Push) {
    if (-not $Commit) { throw '-Push requires -Commit (or commit manually first).' }
    Invoke-Git @('push', '--force-with-lease', 'origin', $ForumBranch)
    Write-Host "Pushed origin/$ForumBranch"
} else {
    Write-Host 'Push when ready:'
    Write-Host "  git push --force-with-lease origin $ForumBranch"
    Write-Host ''
    Write-Host "Return to full-credit:"
    Write-Host "  git checkout $SourceBranch"
}
