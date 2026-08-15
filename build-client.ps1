# ============================================================================
# Build DSH Client (DSHClient.exe) - WebView2 desktop shell
# ----------------------------------------------------------------------------
# Uses the .NET Framework 4.8 csc.exe, no SDK installation needed.
# On first run, copies the WebView2 SDK trio next to the exe (from a local
# Microsoft OfficePLUS / JASM install, version 1.0.2535.41).
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-client.ps1
# ============================================================================
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# --- 1. Ensure the WebView2 SDK files exist (copy local copies if missing) ---
$need = @(
    @{ Name = 'Microsoft.Web.WebView2.Core.dll';     From = 'C:\Program Files\Microsoft OfficePLUS\3.16.0.46159\addin\Microsoft.Web.WebView2.Core.dll' },
    @{ Name = 'Microsoft.Web.WebView2.WinForms.dll'; From = 'C:\Program Files\Microsoft OfficePLUS\3.16.0.46159\addin\Microsoft.Web.WebView2.WinForms.dll' },
    @{ Name = 'WebView2Loader.dll';                  From = 'C:\Program Files\Microsoft OfficePLUS\3.16.0.46159\addin\runtimes\win-x64\native\WebView2Loader.dll' }
)
foreach ($item in $need) {
    $dst = Join-Path $root $item.Name
    if (-not (Test-Path $dst)) {
        if (-not (Test-Path $item.From)) {
            throw "Missing $($item.Name) and no local source at: $($item.From)`nDownload the Microsoft.Web.WebView2 NuGet package and place the file manually."
        }
        Copy-Item -LiteralPath $item.From -Destination $dst
        Write-Host "Copied $($item.Name)"
    } else {
        Write-Host "OK $($item.Name)"
    }
}

# --- 2. Compile ---
$src   = Join-Path $root 'DSHClient.cs'
$out   = Join-Path $root 'DSHClient.exe'
$ico   = Join-Path $root 'dsh.ico'
$core  = Join-Path $root 'Microsoft.Web.WebView2.Core.dll'
$win   = Join-Path $root 'Microsoft.Web.WebView2.WinForms.dll'

if (-not (Test-Path $src)) { throw "Missing source: $src" }
if (-not (Test-Path $ico)) { Write-Warning "Missing dsh.ico; building without an icon" }

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    ('/out:' + $out),
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll',
    '/r:System.Management.dll',
    ('/r:' + $core),
    ('/r:' + $win)
)
if (Test-Path $ico) { $cscArgs += ('/win32icon:' + $ico) }
$cscArgs += $src

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Compile failed (exit code $LASTEXITCODE)" }

Write-Host ""
Write-Host "Build OK: $out"
Write-Host "Run DSHClient.exe, or re-run setup-desktop.ps1 to point the desktop icon at it."
