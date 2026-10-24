Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outIcon = Join-Path $root 'app.ico'
$preview = Join-Path $PSScriptRoot 'icon-preview.png'

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bmp.SetResolution(96, 96)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(0.0, $size * 0.015)
    $radius = [Math]::Max(2.0, $size * 0.215)
    $path = New-RoundedPath $pad $pad ($size - 2 * $pad) ($size - 2 * $pad) $radius

    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 34, 133, 224), [System.Drawing.Color]::FromArgb(255, 10, 86, 176), 90.0)
    $g.FillPath($brush, $path)
    $brush.Dispose()

    # subtle glossy highlight along the top half
    $glossRect = New-Object System.Drawing.RectangleF(0, 0, $size, $size * 0.58)
    $gloss = New-Object System.Drawing.Drawing2D.LinearGradientBrush($glossRect, [System.Drawing.Color]::FromArgb(46, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255), 90.0)
    $g.SetClip($path)
    $g.FillRectangle($gloss, $glossRect)
    $g.ResetClip()
    $gloss.Dispose()

    # power symbol
    $penWidth = [Math]::Max(1.45, $size * 0.095)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $cx = $size / 2.0
    $cy = $size / 2.0
    $diameter = $size * 0.50
    $arcRect = New-Object System.Drawing.RectangleF(($cx - $diameter / 2), ($cy - $diameter / 2 + $size * 0.015), $diameter, $diameter)
    $g.DrawArc($pen, $arcRect, -58, 296)

    $stemTop = $cy - $size * 0.255
    $stemBottom = $cy - $size * 0.015
    $g.DrawLine($pen, $cx, $stemTop, $cx, $stemBottom)

    $pen.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$images = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,@($s, $ms.ToArray())
    if ($s -eq 256) {
        $bmp.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
    $ms.Dispose()
}

$fs = [System.IO.File]::Create($outIcon)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = [int]$img[0]
    $bytes = [byte[]]$img[1]
    $bw.Write([byte]$(if ($s -eq 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -eq 256) { 0 } else { $s }))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($img in $images) {
    $bw.Write([byte[]]$img[1])
}
$bw.Flush()
$bw.Close()
$fs.Close()

Write-Output "Wrote $outIcon ($((Get-Item $outIcon).Length) bytes)"
