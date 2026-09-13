# update-engine.ps1 - deliberately change the pinned DSH engine version.
#
#   .\update-engine.ps1                 show the current pin + the newest version on npm
#   .\update-engine.ps1 -Version 0.1.5-rc.1
#   .\update-engine.ps1 -Latest         pin the newest published version
#
# Rolling back is just as easy: pin the older version again - npx keeps every
# version it has downloaded in its cache, so no reinstall is needed.
# This file is intentionally pure ASCII so any PowerShell reads it correctly.

param(
    [string]$Version,
    [switch]$Latest
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
$pinFile = Join-Path $root 'dsh-engine.txt'

$current = ''
if (Test-Path -LiteralPath $pinFile) {
    $current = ([System.IO.File]::ReadAllText($pinFile)).Trim()
}

Write-Host ('pinned now    : ' + $(if ($current) { $current } else { '(none - follows npm latest)' }))

if (-not $Version -and -not $Latest) {
    Write-Host 'querying npm  : ...'
    $newest = (& npm view '@deepseek-ai/dsh' version 2>$null | Select-Object -Last 1)
    if ($newest) { Write-Host ('newest on npm : ' + $newest.Trim()) }
    Write-Host ''
    Write-Host 'pin a version : .\update-engine.ps1 -Version <x.y.z>'
    Write-Host 'follow newest : .\update-engine.ps1 -Latest'
    exit 0
}

if ($Latest) {
    Write-Host 'querying npm  : ...'
    $newest = (& npm view '@deepseek-ai/dsh' version 2>$null | Select-Object -Last 1)
    if (-not $newest) { throw 'could not read the newest version from npm' }
    $Version = $newest.Trim()
}

$Version = $Version.Trim()
[System.IO.File]::WriteAllText($pinFile, $Version + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('pinned engine : ' + $Version)
Write-Host ''
Write-Host 'Now stop DSH (desktop icon "DSH 停止") and start it again from the "DSH" icon.'
