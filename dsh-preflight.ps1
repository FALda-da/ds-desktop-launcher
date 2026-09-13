# dsh-preflight.ps1
# Best-effort self-heal for the DSH web profile, run before every server start.
#
#   * rotates dsh-server.log when it grows past 1 MB
#   * makes sure ~/.dsh/profiles/web/package.json has non-empty "name" and "version"
#     (a missing "version" makes every model request fail with
#      "DeepSeek request extension preparation failed" - seen 3 times already)
#   * repairs the known mojibake path "DS" + U+705C U+6FCA U+762F -> "DS" + U+5C1D U+8BD5
#     (skin/market tooling rewrote a UTF-8 path as GBK, breaking the link: dependency)
#
# Every change is validated and a timestamped backup is kept next to the original.
# Safe to run at any time; does nothing when the config is already healthy.
# This file is intentionally pure ASCII (no BOM) so any PowerShell reads it correctly.

param(
    [string]$PackagePath,
    [string]$LogPath
)

$ErrorActionPreference = 'Continue'

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $LogPath) { $LogPath = Join-Path $root 'dsh-server.log' }
if (-not $PackagePath) { $PackagePath = Join-Path $env:USERPROFILE '.dsh\profiles\web\package.json' }
$repairLog = Join-Path $root 'dsh-repair.log'

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Repair([string]$msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy/MM/dd HH:mm:ss'), $msg
    try { [System.IO.File]::AppendAllText($repairLog, $line + "`r`n", $utf8NoBom) } catch { }
    Write-Host $msg
}

# --- 1. rotate the server log at 1 MB -------------------------------------
try {
    if (Test-Path -LiteralPath $LogPath) {
        if ((Get-Item -LiteralPath $LogPath).Length -gt 1MB) {
            Move-Item -LiteralPath $LogPath -Destination ($LogPath + '.old') -Force
            Write-Repair 'rotated dsh-server.log -> dsh-server.log.old'
        }
    }
} catch { }

# --- 2. self-heal the profile package.json --------------------------------
try {
    if (-not (Test-Path -LiteralPath $PackagePath)) { exit 0 }

    $raw = [System.IO.File]::ReadAllText($PackagePath, $utf8NoBom)
    $obj = $null
    try { $obj = $raw | ConvertFrom-Json } catch {
        Write-Repair 'WARN package.json is not valid JSON - left untouched'
        exit 0
    }

    $text  = $raw
    $notes = New-Object System.Collections.ArrayList

    if (-not $obj.PSObject.Properties['name'] -or [string]::IsNullOrWhiteSpace([string]$obj.name)) {
        $i = $text.IndexOf('{')
        if ($i -ge 0) {
            $text = $text.Substring(0, $i + 1) + "`r`n  `"name`": `"dsh-profile-web`"," + $text.Substring($i + 1)
            [void]$notes.Add('added missing "name"')
        }
    }

    if (-not $obj.PSObject.Properties['version'] -or [string]::IsNullOrWhiteSpace([string]$obj.version)) {
        $i = $text.IndexOf('{')
        if ($i -ge 0) {
            $text = $text.Substring(0, $i + 1) + "`r`n  `"version`": `"0.0.0`"," + $text.Substring($i + 1)
            [void]$notes.Add('added missing "version" (0.0.0)')
        }
    }

    # known mojibake: "DS" + 3 CJK chars (UTF-8 bytes of the real path read as GBK)
    $badPath  = 'DS' + [string][char]0x705C + [string][char]0x6FCA + [string][char]0x762F
    $goodPath = 'DS' + [string][char]0x5C1D + [string][char]0x8BD5
    if ($text.Contains($badPath)) {
        $fixed = $text.Replace($badPath, $goodPath)
        $ok = $true
        try {
            $fo = $fixed | ConvertFrom-Json
            foreach ($p in $fo.dependencies.PSObject.Properties) {
                $v = [string]$p.Value
                if ($v -match '^(link|file):(.+)$') {
                    if (-not (Test-Path -LiteralPath $Matches[2])) { $ok = $false; break }
                }
            }
        } catch { $ok = $false }

        if ($ok) {
            $text = $fixed
            [void]$notes.Add('repaired mojibake path to ' + $goodPath)
        } else {
            Write-Repair 'WARN mojibake path found, but the repaired path does not exist - left untouched'
        }
    }

    if ($notes.Count -gt 0) {
        $stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
        $backup = Join-Path (Split-Path -Parent $PackagePath) ('package.json.bak-' + $stamp)
        Copy-Item -LiteralPath $PackagePath -Destination $backup -Force
        [System.IO.File]::WriteAllText($PackagePath, $text, $utf8NoBom)
        try {
            $null = [System.IO.File]::ReadAllText($PackagePath, $utf8NoBom) | ConvertFrom-Json
            $valid = 'JSON OK'
        } catch { $valid = 'JSON INVALID' }
        Write-Repair ('repaired {0}: {1} [{2}] backup -> {3}' -f $PackagePath, ($notes -join '; '), $valid, (Split-Path -Leaf $backup))
    }
} catch {
    Write-Repair ('WARN preflight error: ' + $_.Exception.Message)
}
