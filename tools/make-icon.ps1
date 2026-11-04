# Erzeugt assets\app.ico aus assets\icon-source.png (transparent, quadratisch, 16-256 px).
# Aufruf: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$Source = "")
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
if ($Source -eq "") { $Source = Join-Path $root 'assets\icon-source.png' }
$src = [System.Drawing.Image]::FromFile($Source)
$sizes = 256, 64, 48, 40, 32, 24, 20, 16
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    # ganzes Bild mittig einpassen, Seitenverhältnis erhalten
    $scale = [Math]::Min($s / $src.Width, $s / $src.Height)
    $dw = $src.Width * $scale; $dh = $src.Height * $scale
    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # keine hellen Ränder beim Skalieren
    $dest = New-Object System.Drawing.Rectangle ([int](($s - $dw) / 2)), ([int](($s - $dh) / 2)), ([int]$dw), ([int]$dh)
    $g.DrawImage($src, $dest, 0, 0, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $pngs += , $ms.ToArray()
}
$src.Dispose()
$out = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
[IO.File]::WriteAllBytes((Join-Path $root 'assets\app.ico'), $out.ToArray())
Write-Host "assets\app.ico erzeugt"
