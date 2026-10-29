#Requires -Version 5.1
<#
.SYNOPSIS
    生成应用图标 app.ico：深色圆角底 + 白色 USB 三叉戟，和界面侧边栏 logo 一致。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy/make-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$Output = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root 'src\UsbConfigEditor\app.ico'
}

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()

foreach ($size in $sizes) {
    $scale = $size / 256.0
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圆角深色底
    $radius = [single](56 * $scale)
    $rect = New-Object System.Drawing.RectangleF ([single]0), ([single]0), ([single]$size), ([single]$size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 23, 23, 23))
    $g.FillPath($bg, $path)

    # 白色 USB 三叉戟
    $white = [System.Drawing.Color]::FromArgb(255, 250, 250, 250)
    $penWidth = [single](16 * $scale)
    $pen = New-Object System.Drawing.Pen $white, $penWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $brush = New-Object System.Drawing.SolidBrush $white

    # 主杆
    $g.DrawLine($pen, [single](128 * $scale), [single](210 * $scale), [single](128 * $scale), [single](84 * $scale))

    # 顶部箭头
    $arrow = @(
        (New-Object System.Drawing.PointF ([single](128 * $scale)), ([single](50 * $scale))),
        (New-Object System.Drawing.PointF ([single](103 * $scale)), ([single](90 * $scale))),
        (New-Object System.Drawing.PointF ([single](153 * $scale)), ([single](90 * $scale)))
    )
    $g.FillPolygon($brush, [System.Drawing.PointF[]]$arrow)

    # 左支：圆点
    $g.DrawLine($pen, [single](128 * $scale), [single](176 * $scale), [single](80 * $scale), [single](146 * $scale))
    $g.DrawLine($pen, [single](80 * $scale), [single](146 * $scale), [single](80 * $scale), [single](128 * $scale))
    $r = [single](15 * $scale)
    $g.FillEllipse($brush, [single](80 * $scale) - $r, [single](110 * $scale) - $r, $r * 2, $r * 2)

    # 右支：方块
    $g.DrawLine($pen, [single](128 * $scale), [single](152 * $scale), [single](178 * $scale), [single](122 * $scale))
    $g.DrawLine($pen, [single](178 * $scale), [single](122 * $scale), [single](178 * $scale), [single](102 * $scale))
    $sq = [single](34 * $scale)
    $sqR = [single](7 * $scale)
    $sqPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $sqPath.AddArc([single](162 * $scale), [single](64 * $scale), $sqR * 2, $sqR * 2, 180, 90)
    $sqPath.AddArc([single](162 * $scale) + $sq - $sqR * 2, [single](64 * $scale), $sqR * 2, $sqR * 2, 270, 90)
    $sqPath.AddArc([single](162 * $scale) + $sq - $sqR * 2, [single](64 * $scale) + $sq - $sqR * 2, $sqR * 2, $sqR * 2, 0, 90)
    $sqPath.AddArc([single](162 * $scale), [single](64 * $scale) + $sq - $sqR * 2, $sqR * 2, $sqR * 2, 90, 90)
    $sqPath.CloseFigure()
    $g.FillPath($brush, $sqPath)

    $g.Dispose()

    # 取像素，拼 32bpp DIB（不用 PNG 帧，WinForms 兼容性更好）
    $rectInt = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bmp.LockBits($rectInt, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    $maskStride = [math]::Floor(($size + 31) / 32) * 4
    $image = New-Object System.IO.MemoryStream
    $iw = New-Object System.IO.BinaryWriter $image

    $iw.Write([uint32]40)
    $iw.Write([int32]$size)
    $iw.Write([int32]($size * 2))
    $iw.Write([uint16]1)
    $iw.Write([uint16]32)
    $iw.Write([uint32]0)
    $iw.Write([uint32]($size * $size * 4))
    $iw.Write([int32]0)
    $iw.Write([int32]0)
    $iw.Write([uint32]0)
    $iw.Write([uint32]0)

    for ($y = $size - 1; $y -ge 0; $y--) {
        $iw.Write($pixels, $y * $stride, $size * 4)
    }

    $iw.Write((New-Object byte[] ($maskStride * $size)))
    $iw.Flush()
    $bytes = $image.ToArray()
    $iw.Dispose()
    $image.Dispose()

    $frames += , @{ Size = $size; Bytes = $bytes }
}

# 组装 ICO（32bpp DIB 帧）
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $dim = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}

foreach ($frame in $frames) {
    $writer.Write($frame.Bytes)
}

$writer.Flush()
$bytes = $file.ToArray()
$writer.Dispose()
$file.Dispose()

[System.IO.File]::WriteAllBytes($Output, $bytes)
Write-Host "已生成 $Output（$($frames.Count) 个尺寸，$([math]::Round($bytes.Length / 1KB, 1)) KB）"
