# Generates dsh.ico and creates a desktop shortcut "DSH" that launches start-dsh.ps1.
# Re-run this script any time you move the workspace folder.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$icoPath = Join-Path $root 'dsh.ico'
$desktop = [Environment]::GetFolderPath('Desktop')
$lnkPath = Join-Path $desktop 'DSH.lnk'

# --- 1. Generate a simple DeepSeek-blue icon with a white "D" ---
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

# Rounded-rectangle background
$r = New-Object System.Drawing.Rectangle 8, 8, 240, 240
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$d = 48
$path.AddArc($r.X, $r.Y, $d, $d, 180, 90)
$path.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
$path.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
$path.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
$path.CloseFigure()

$blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 77, 107, 254))
$g.FillPath($blue, $path)

# White "D" letter
$font = New-Object System.Drawing.Font('Segoe UI', 150, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$rectF = New-Object System.Drawing.RectangleF 0, 0, 256, 256
$g.DrawString('D', $font, [System.Drawing.Brushes]::White, $rectF, $sf)

$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create($icoPath)
$icon.Save($fs)
$fs.Close()

$g.Dispose(); $bmp.Dispose(); $blue.Dispose(); $font.Dispose(); $sf.Dispose(); $icon.Dispose()
Write-Host "Icon saved: $icoPath"

# --- 2. Create the desktop shortcut (prefer DSHLauncher.exe when available) ---
$exePath = Join-Path $root 'DSHLauncher.exe'
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut($lnkPath)
if (Test-Path $exePath) {
    $lnk.TargetPath = $exePath
    $lnk.Arguments = ''
    $lnk.IconLocation = $exePath + ',0'
    $lnk.WindowStyle = 1
} else {
    $lnk.TargetPath = 'powershell.exe'
    $lnk.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $root + '\start-dsh.ps1"'
    $lnk.IconLocation = $icoPath
    $lnk.WindowStyle = 7
}
$lnk.WorkingDirectory = $root
$lnk.Description = 'DeepSeek Harness Web - double-click to start'
$lnk.Save()

Write-Host "Shortcut created: $lnkPath"
