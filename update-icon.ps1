# Converts an image into dsh.ico and rebuilds DSHLauncher.exe with the new icon.
# Usage:  .\update-icon.ps1 -Source "C:\path\to\image.png"
param(
    [Parameter(Mandatory = $true)]
    [string]$Source
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$icoOut = Join-Path $root 'dsh.ico'
$exeOut = Join-Path $root 'DSHLauncher.exe'
$csPath = Join-Path $root 'DSHLauncher.cs'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }

if (-not (Test-Path $Source)) { throw "Source image not found: $Source" }
if (-not (Test-Path $csPath)) { throw "DSHLauncher.cs not found next to this script: $csPath" }

Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($Source)
$side = [Math]::Min($img.Width, $img.Height)
$srcRect = New-Object System.Drawing.Rectangle ([int](($img.Width - $side) / 2)), ([int](($img.Height - $side) / 2)), $side, $side

# Build a multi-size ICO (16/32/48/256) with PNG-compressed entries.
$sizes = @(16, 32, 48, 256)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$entries = @()
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $s, $s), $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $data = $pngMs.ToArray()
    $pngMs.Dispose(); $g.Dispose(); $bmp.Dispose()
    $entries += [pscustomobject]@{ Size = $s; Data = $data; Offset = $offset }
    $offset += $data.Length
}
foreach ($e in $entries) {
    $w = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $bw.Write([byte]$w); $bw.Write([byte]$w)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Data.Length); $bw.Write([uint32]$e.Offset)
}
foreach ($e in $entries) { $bw.Write($e.Data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoOut, $ms.ToArray())
$bw.Dispose(); $ms.Dispose(); $img.Dispose()
Write-Host "dsh.ico updated: $icoOut"

# Rebuild the exe with the new icon embedded.
& $csc -nologo -target:winexe -optimize+ "-win32icon:$icoOut" "-out:$exeOut" `
    -r:System.Management.dll -r:System.Windows.Forms.dll $csPath
if ($LASTEXITCODE -ne 0) { throw "Compile failed with exit code $LASTEXITCODE" }
Write-Host "DSHLauncher.exe rebuilt with the new icon."
Write-Host "Desktop shortcut picks the icon up from the exe automatically."
