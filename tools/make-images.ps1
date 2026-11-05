# Generates logo.png (512x512) and modes.png (1280x720) for the repo README.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outDir = Split-Path $PSScriptRoot -Parent

function New-Color([string]$hex) {
    $hex = $hex.TrimStart('#')
    [System.Drawing.Color]::FromArgb(
        [convert]::ToByte($hex.Substring(0,2), 16),
        [convert]::ToByte($hex.Substring(2,2), 16),
        [convert]::ToByte($hex.Substring(4,2), 16))
}

function New-RoundRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90)
    $p.AddArc($x + $w - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
    $p.AddArc($x + $w - 2 * $r, $y + $h - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $p.AddArc($x, $y + $h - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Brush([string]$hex) { New-Object System.Drawing.SolidBrush((New-Color $hex)) }
function New-Pen([string]$hex, [float]$width) {
    $p = New-Object System.Drawing.Pen((New-Color $hex), $width)
    $p.StartCap = 'Round'; $p.EndCap = 'Round'
    return $p
}

function Draw-CenterText($g, [string]$text, [string]$face, [float]$size, [string]$colorHex, $rect) {
    $f = New-Object System.Drawing.Font($face, $size, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'
    $sf.LineAlignment = 'Center'
    $g.DrawString($text, $f, (New-Brush $colorHex), $rect, $sf)
    $f.Dispose()
}

function Draw-Arrow($g, [string]$colorHex, [float]$x1, [float]$y1, [float]$x2, [float]$y2, [float]$thick, [float]$head) {
    $pen = New-Pen $colorHex $thick
    $g.DrawLine($pen, $x1, $y1, $x2, $y2)
    $ang = [Math]::Atan2($y2 - $y1, $x2 - $x1)
    $pts = @(
        (New-Object System.Drawing.PointF($x2, $y2)),
        (New-Object System.Drawing.PointF(($x2 - $head * [Math]::Cos($ang - 0.45)), ($y2 - $head * [Math]::Sin($ang - 0.45)))),
        (New-Object System.Drawing.PointF(($x2 - $head * [Math]::Cos($ang + 0.45)), ($y2 - $head * [Math]::Sin($ang + 0.45))))
    )
    $g.FillPolygon((New-Brush $colorHex), $pts)
    $pen.Dispose()
}

# ===========================================================================
# LOGO 512x512 : dark rounded square, top monitor (blue, work) +
#                bottom ultrawide monitor (green, personal), swap arrows
# ===========================================================================
$bmp = New-Object System.Drawing.Bitmap 512, 512
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'

$bg = New-RoundRectPath 12 12 488 488 92
$g.FillPath((New-Brush '#0F172A'), $bg)

# top monitor (work / blue)
$penBlue = New-Pen '#38BDF8' 16
$mon1 = New-RoundRectPath 116 84 280 148 18
$g.DrawPath($penBlue, $mon1)
$g.FillRectangle((New-Brush '#38BDF8'), 240, 232, 32, 18)
$base1 = New-RoundRectPath 200 250 112 16 8
$g.FillPath((New-Brush '#38BDF8'), $base1)

# bottom ultrawide monitor (personal / green)
$penGreen = New-Pen '#4ADE80' 16
$mon2 = New-RoundRectPath 76 312 360 128 18
$g.DrawPath($penGreen, $mon2)
$g.FillRectangle((New-Brush '#4ADE80'), 240, 440, 32, 18)
$base2 = New-RoundRectPath 190 458 132 16 8
$g.FillPath((New-Brush '#4ADE80'), $base2)

# swap arrows between monitors
Draw-Arrow $g '#94A3B8' 190 286 322 286 10 18
Draw-Arrow $g '#94A3B8' 322 306 190 306 10 18

$g.Dispose(); $bmp.Save((Join-Path $outDir 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Output "logo.png written"

# ===========================================================================
# MODES DIAGRAM 1280x720 : two panels, work (left) / personal (right)
# ===========================================================================
$bmp = New-Object System.Drawing.Bitmap 1280, 720
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = ([System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit)
$g.Clear([System.Drawing.Color]::White)

# title
Draw-CenterText $g 'Two modes, one click' 'Segoe UI Semibold' 44 '#0F172A' (New-Object System.Drawing.RectangleF(0, 24, 1280, 60))
Draw-CenterText $g 'Windows 11 display switching, driven by your display profile' 'Segoe UI' 22 '#64748B' (New-Object System.Drawing.RectangleF(0, 84, 1280, 36))

# panel borders
$g.DrawPath((New-Pen '#BFDBFE' 3), (New-RoundRectPath 60 140 540 520 24))
$g.DrawPath((New-Pen '#BBF7D0' 3), (New-RoundRectPath 680 140 540 520 24))

# ---- WORK panel ----
Draw-CenterText $g 'WORK MODE' 'Segoe UI Semibold' 34 '#2563EB' (New-Object System.Drawing.RectangleF(60, 168, 540, 48))
Draw-CenterText $g 'Acer is main  -  MSI disconnected' 'Segoe UI' 21 '#64748B' (New-Object System.Drawing.RectangleF(60, 216, 540, 34))

# top monitor (ACER) - ON, main (blue fill)
$g.FillPath((New-Brush '#DBEAFE'), (New-RoundRectPath 190 292 280 118 12))
$g.DrawPath((New-Pen '#2563EB' 5), (New-RoundRectPath 190 292 280 118 12))
Draw-CenterText $g 'ACER' 'Segoe UI Semibold' 24 '#1D4ED8' (New-Object System.Drawing.RectangleF(190, 316, 280, 32))
Draw-CenterText $g 'MAIN' 'Segoe UI' 19 '#2563EB' (New-Object System.Drawing.RectangleF(190, 352, 280, 28))
$g.FillRectangle((New-Brush '#2563EB'), 314, 410, 32, 14)
$g.FillPath((New-Brush '#2563EB'), (New-RoundRectPath 280 424 100 12 6))

# bottom monitor (MSI) - OFF (grey, dashed, power symbol)
$g.FillPath((New-Brush '#F1F5F9'), (New-RoundRectPath 140 508 380 100 12))
$penO = New-Pen '#94A3B8' 4
$penO.DashStyle = 'Dash'
$g.DrawPath($penO, (New-RoundRectPath 140 508 380 100 12))
$penP = New-Pen '#94A3B8' 7
$g.DrawArc($penP, 330, 540, 40, 40, 310, 280)
$g.DrawLine($penP, 350, 534, 350, 556)
Draw-CenterText $g 'MSI - OFF' 'Segoe UI' 20 '#94A3B8' (New-Object System.Drawing.RectangleF(140, 614, 380, 30))

# ---- PERSONAL panel ----
Draw-CenterText $g 'PERSONAL MODE' 'Segoe UI Semibold' 34 '#16A34A' (New-Object System.Drawing.RectangleF(680, 168, 540, 48))
Draw-CenterText $g 'MSI is main  -  Acer extended' 'Segoe UI' 21 '#64748B' (New-Object System.Drawing.RectangleF(680, 216, 540, 34))

# top monitor (ACER) - ON, extended (green, thinner border)
$g.FillPath((New-Brush '#DCFCE7'), (New-RoundRectPath 810 292 280 118 12))
$g.DrawPath((New-Pen '#16A34A' 3), (New-RoundRectPath 810 292 280 118 12))
Draw-CenterText $g 'ACER' 'Segoe UI Semibold' 24 '#15803D' (New-Object System.Drawing.RectangleF(810, 316, 280, 32))
Draw-CenterText $g 'EXTENDED' 'Segoe UI' 19 '#16A34A' (New-Object System.Drawing.RectangleF(810, 352, 280, 28))
$g.FillRectangle((New-Brush '#16A34A'), 934, 410, 32, 14)
$g.FillPath((New-Brush '#16A34A'), (New-RoundRectPath 900 424 100 12 6))

# bottom monitor (MSI) - ON, main (green, thick border)
$g.FillPath((New-Brush '#DCFCE7'), (New-RoundRectPath 760 508 380 100 12))
$g.DrawPath((New-Pen '#16A34A' 6), (New-RoundRectPath 760 508 380 100 12))
Draw-CenterText $g 'MSI' 'Segoe UI Semibold' 24 '#15803D' (New-Object System.Drawing.RectangleF(760, 528, 380, 32))
Draw-CenterText $g 'MAIN' 'Segoe UI' 19 '#16A34A' (New-Object System.Drawing.RectangleF(760, 562, 380, 28))

# center swap arrows between panels
Draw-Arrow $g '#334155' 612 380 668 380 8 16
Draw-Arrow $g '#334155' 668 420 612 420 8 16
Draw-CenterText $g 'one click' 'Segoe UI' 16 '#334155' (New-Object System.Drawing.RectangleF(592, 436, 96, 24))

$g.Dispose(); $bmp.Save((Join-Path $outDir 'modes.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Write-Output "modes.png written"
