Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

# Background circular gradient
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0,0), (New-Object System.Drawing.Point 256,256), ([System.Drawing.Color]::FromArgb(255, 15, 23, 42)), ([System.Drawing.Color]::FromArgb(255, 14, 116, 144))
$g.FillEllipse($brush, 8, 8, 240, 240)

# Outer glow
$penGlow = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(180, 56, 189, 248)), 5
$g.DrawEllipse($penGlow, 8, 8, 240, 240)

# Monitor border
$penMonitor = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 6
$g.DrawRectangle($penMonitor, 46, 52, 164, 110)

# Screen inside
$screenBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 34, 53))
$g.FillRectangle($screenBrush, 52, 58, 152, 98)

# Draw cast waves inside screen
$penWave1 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 56, 189, 248)), 6
$penWave2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 125, 211, 252)), 5
$penWave3 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 224, 242, 254)), 4

# Center dot
$cyanBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 56, 189, 248))
$g.FillEllipse($cyanBrush, 122, 115, 12, 12)

# Expanding arcs
$g.DrawArc($penWave1, 108, 95, 40, 40, 200, 140)
$g.DrawArc($penWave2, 94, 80, 68, 68, 200, 140)
$g.DrawArc($penWave3, 80, 65, 96, 96, 200, 140)

# Stand & base
$g.FillRectangle([System.Drawing.Brushes]::White, 118, 162, 20, 24)
$g.FillRectangle([System.Drawing.Brushes]::White, 88, 186, 80, 10)

$g.Dispose()

# Save PNG bytes
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $ms.ToArray()
$ms.Dispose()
$bmp.Dispose()

# Build standard ICO with embedded PNG
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $scriptDir) { $scriptDir = $PSScriptRoot }
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }
$outIcon = Join-Path $scriptDir "Assets\app.ico"
$fs = [System.IO.File]::Create($outIcon)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0) # Reserved
$bw.Write([uint16]1) # Type (1=Icon)
$bw.Write([uint16]1) # Count (1 image)

# Directory Entry (16 bytes)
$bw.Write([byte]0) # 256 width
$bw.Write([byte]0) # 256 height
$bw.Write([byte]0) # Color palette
$bw.Write([byte]0) # Reserved
$bw.Write([uint16]1) # Color planes
$bw.Write([uint16]32) # Bits per pixel
$bw.Write([uint32]$pngBytes.Length) # Image data size
$bw.Write([uint32]22) # Offset of data (6 + 16 = 22)

$bw.Write($pngBytes)
$bw.Flush()
$fs.Close()
Write-Host "app.ico generated successfully!"
