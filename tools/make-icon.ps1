# Gera src/Qhours/assets/qhours.ico (PNG em vários tamanhos) e site/assets/qhours.png
# Uso: pwsh tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Quadrado arredondado com gradiente âmbar
    $r = [single](56 * $s); $pad = [single](8 * $s); $w = [single]($size - 2 * $pad)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, $r, $r, 180, 90)
    $path.AddArc($pad + $w - $r, $pad, $r, $r, 270, 90)
    $path.AddArc($pad + $w - $r, $pad + $w - $r, $r, $r, 0, 90)
    $path.AddArc($pad, $pad + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, $w, $w
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(246, 190, 92)), ([System.Drawing.Color]::FromArgb(214, 122, 38)), 90
    $g.FillPath($grad, $path)

    # Relógio
    $ink = [System.Drawing.Color]::FromArgb(28, 20, 10)
    $pen = New-Object System.Drawing.Pen $ink, ([single](20 * $s))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $c = [single](128 * $s); $cr = [single](70 * $s)
    $g.DrawEllipse($pen, $c - $cr, $c - $cr, 2 * $cr, 2 * $cr)
    $g.DrawLine($pen, $c, $c, $c, $c - [single](42 * $s))
    $g.DrawLine($pen, $c, $c, $c + [single](32 * $s), $c + [single](18 * $s))
    $dot = New-Object System.Drawing.SolidBrush $ink
    $d = [single](12 * $s)
    $g.FillEllipse($dot, $c - $d, $c - $d, 2 * $d, 2 * $d)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $b = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    , $ms.ToArray()
}

$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ico
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $root 'src/Qhours/assets/qhours.ico'), $ico.ToArray())

New-Item -ItemType Directory -Force (Join-Path $root 'site/assets') | Out-Null
$big = New-IconBitmap 512
$big.Save((Join-Path $root 'site/assets/qhours.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
Write-Output 'ok'
