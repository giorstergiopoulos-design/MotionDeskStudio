# Δημιουργεί ένα πολυ-ανάλυσης .ico (16/32/48/64/128/256) για το MotionDesk Studio,
# με PNG-encoded frames (υποστηρίζεται από Explorer/Taskbar από Vista και μετά).
Add-Type -AssemblyName System.Drawing

$sizes = 16, 32, 48, 64, 128, 256
$outIco = Join-Path $PSScriptRoot "..\assets\MotionDesk.ico"
$pngBlobs = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Στρογγυλεμένο τετράγωνο φόντο με διαγώνιο gradient (cyan -> indigo), όπως το accent χρώμα
    # που ήδη χρησιμοποιείται στα widgets (#00d2ff -> #3a7bd5).
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

    # Λευκό "motion wave" glyph (δύο overlapping τόξα) στο κέντρο, συμβολίζοντας κίνηση/desktop motion.
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.0, $size * 0.09))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $wavePath1 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = $size * 0.62
    $x0 = ($size - $w) / 2
    $yMid = $size * 0.58
    $wavePath1.AddBezier(
        [float]$x0, [float]$yMid,
        [float]($x0 + $w * 0.25), [float]($yMid - $size * 0.22),
        [float]($x0 + $w * 0.75), [float]($yMid + $size * 0.22),
        [float]($x0 + $w), [float]$yMid)
    $g.DrawPath($pen, $wavePath1)

    $pen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(210, 255, 255, 255), [Math]::Max(1.0, $size * 0.06))
    $pen2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen2.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $wavePath2 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $yMid2 = $size * 0.38
    $wavePath2.AddBezier(
        [float]$x0, [float]$yMid2,
        [float]($x0 + $w * 0.25), [float]($yMid2 - $size * 0.16),
        [float]($x0 + $w * 0.75), [float]($yMid2 + $size * 0.16),
        [float]($x0 + $w), [float]$yMid2)
    $g.DrawPath($pen2, $wavePath2)

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
