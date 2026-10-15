# Δημιουργεί ένα πολυ-ανάλυσης .ico (16/32/48/64/128/256) για το Opsis — "Trace" concept:
# το ίδιο κύμα-λογότυπο, με έναν φωτεινό κόμβο πάνω στη γραμμή (στατικό hero frame της
# ζωντανής εκδοχής στο tray). PNG-encoded frames (υποστηρίζεται από Explorer/Taskbar από Vista και μετά).
Add-Type -AssemblyName System.Drawing

function BezierPoint([double]$t, [double[]]$p0, [double[]]$p1, [double[]]$p2, [double[]]$p3) {
    $mt = 1 - $t
    $x = $mt*$mt*$mt*$p0[0] + 3*$mt*$mt*$t*$p1[0] + 3*$mt*$t*$t*$p2[0] + $t*$t*$t*$p3[0]
    $y = $mt*$mt*$mt*$p0[1] + 3*$mt*$mt*$t*$p1[1] + 3*$mt*$t*$t*$p2[1] + $t*$t*$t*$p3[1]
    return @($x, $y)
}

$sizes = 16, 32, 48, 64, 128, 256
$outIco = Join-Path $PSScriptRoot "..\assets\Opsis.ico"
$pngBlobs = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Στρογγυλεμένο τετράγωνο φόντο με διαγώνιο gradient (cyan -> indigo) — αμετάβλητο, ίδιο brand mark.
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.ColorTranslator]::FromHtml("#00d2ff"),
        [System.Drawing.ColorTranslator]::FromHtml("#233a8f"),
        45)

    $radius = [Math]::Max(2, [int]($size * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)

    $w = $size * 0.62
    $x0 = ($size - $w) / 2
    $yMid = $size * 0.58
    $p0 = @($x0, $yMid)
    $p1 = @(($x0 + $w * 0.25), ($yMid - $size * 0.22))
    $p2 = @(($x0 + $w * 0.75), ($yMid + $size * 0.22))
    $p3 = @(($x0 + $w), $yMid)

    # "Trace": η ίδια γραμμή κύματος, πιο αχνή σαν ίχνος/track ...
    $trackPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(130, 255, 255, 255), [Math]::Max(1.0, $size * 0.07))
    $trackPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $trackPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $wavePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wavePath.AddBezier([float]$p0[0], [float]$p0[1], [float]$p1[0], [float]$p1[1], [float]$p2[0], [float]$p2[1], [float]$p3[0], [float]$p3[1])
    $g.DrawPath($trackPen, $wavePath)

    # ... + ένας φωτεινός κόμβος στο μέσο της διαδρομής (στατικό "hero" στιγμιότυπο της κίνησης στο tray).
    $node = BezierPoint 0.5 $p0 $p1 $p2 $p3
    $glowSize = $size * 0.30
    $glowBrush = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowBrush.AddEllipse($node[0] - $glowSize/2, $node[1] - $glowSize/2, $glowSize, $glowSize)
    $glowPathBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowBrush)
    $glowPathBrush.CenterColor = [System.Drawing.Color]::FromArgb(160, 255, 255, 255)
    $glowPathBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($glowPathBrush, $glowBrush)

    $dotR = [Math]::Max(1.2, $size * 0.075)
    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillEllipse($dotBrush, $node[0] - $dotR, $node[1] - $dotR, $dotR * 2, $dotR * 2)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs += ,($size, $ms.ToArray())

    $g.Dispose()
    $bmp.Dispose()
}

# --- Εγγραφή .ico (ICONDIR + ICONDIRENTRY[] + PNG blobs) ---
$fs = New-Object System.IO.FileStream $outIco, ([System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter $fs

$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type = icon
$bw.Write([UInt16]$pngBlobs.Count)

$offset = 6 + (16 * $pngBlobs.Count)
foreach ($entry in $pngBlobs) {
    $size = $entry[0]
    $bytes = $entry[1]
    $wByte = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([Byte]$wByte)       # width
    $bw.Write([Byte]$wByte)       # height
    $bw.Write([Byte]0)            # color count
    $bw.Write([Byte]0)            # reserved
    $bw.Write([UInt16]1)          # planes
    $bw.Write([UInt16]32)         # bit count
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($entry in $pngBlobs) {
    $bw.Write($entry[1])
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host "Icon written to $outIco"
