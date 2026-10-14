# Δημιουργεί 6 καρέ (32x32) για ζωντανό (animated) tray icon: "Trace" concept — ένας φωτεινός
# κόμβος ξαναδιατρέχει το κύμα-λογότυπο σε βρόχο, σαν υπογραφή που ξαναγράφεται.
Add-Type -AssemblyName System.Drawing

function BezierPoint([double]$t, [double[]]$p0, [double[]]$p1, [double[]]$p2, [double[]]$p3) {
    $mt = 1 - $t
    $x = $mt*$mt*$mt*$p0[0] + 3*$mt*$mt*$t*$p1[0] + 3*$mt*$t*$t*$p2[0] + $t*$t*$t*$p3[0]
    $y = $mt*$mt*$mt*$p0[1] + 3*$mt*$mt*$t*$p1[1] + 3*$mt*$t*$t*$p2[1] + $t*$t*$t*$p3[1]
    return @($x, $y)
}

$size = 32  # tray icons render sharper at 32 downscaled by the shell than drawn natively at 16
$frameCount = 6
$outDir = Join-Path $PSScriptRoot "..\assets\tray"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

for ($f = 0; $f -lt $frameCount; $f++) {
    $t = $f / $frameCount

    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.ColorTranslator]::FromHtml("#00d2ff"),
        [System.Drawing.ColorTranslator]::FromHtml("#233a8f"),
        45)
    $radius = [int]($size * 0.22)
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

    # Αχνό ίχνος (track) — η ίδια καμπύλη του brand mark, σταθερή σε όλα τα frames.
    $trackPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(110, 255, 255, 255), [Math]::Max(1.0, $size * 0.07))
    $trackPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $trackPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $wavePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wavePath.AddBezier([float]$p0[0], [float]$p0[1], [float]$p1[0], [float]$p1[1], [float]$p2[0], [float]$p2[1], [float]$p3[0], [float]$p3[1])
    $g.DrawPath($trackPen, $wavePath)

    # Φωτεινός κόμβος στη θέση t = f/6 πάνω στην ΙΔΙΑ καμπύλη — κάθε frame τον δείχνει λίγο
    # πιο πέρα, οπότε ο κύκλος 6 frames δείχνει τον κόμβο να διατρέχει ολόκληρο το κύμα σε βρόχο.
    $node = BezierPoint $t $p0 $p1 $p2 $p3
    $glowSize = $size * 0.34
    $glowEllipse = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowEllipse.AddEllipse($node[0] - $glowSize/2, $node[1] - $glowSize/2, $glowSize, $glowSize)
    $glowPathBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowEllipse)
    $glowPathBrush.CenterColor = [System.Drawing.Color]::FromArgb(190, 255, 255, 255)
    $glowPathBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($glowPathBrush, $glowEllipse)

    $dotR = [Math]::Max(1.4, $size * 0.085)
    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillEllipse($dotBrush, $node[0] - $dotR, $node[1] - $dotR, $dotR * 2, $dotR * 2)

    # Write single-size ICO (PNG-compressed) for this frame
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $ms.ToArray()

    $icoPath = Join-Path $outDir "frame$f.ico"
    $fs = New-Object System.IO.FileStream $icoPath, ([System.IO.FileMode]::Create)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
    $bw.Write([Byte]$size); $bw.Write([Byte]$size); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngBytes.Length); $bw.Write([UInt32]22)
    $bw.Write($pngBytes)
    $bw.Flush(); $bw.Close(); $fs.Close()

    $g.Dispose(); $bmp.Dispose()
    Write-Host "Frame $f written -> $icoPath"
}
Write-Host "Done: $frameCount tray icon frames in $outDir"
