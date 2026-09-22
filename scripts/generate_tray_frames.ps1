# Δημιουργεί 6 καρέ (16x16) για ζωντανό (animated) tray icon: το ίδιο λογότυπο MotionDesk,
# με μια απαλή "αναπνέουσα" λάμψη (glow pulse) γύρω από τις κυματιστές γραμμές κίνησης.
Add-Type -AssemblyName System.Drawing

$size = 32  # tray icons render sharper at 32 downscaled by the shell than drawn natively at 16
$frameCount = 6
$outDir = Join-Path $PSScriptRoot "..\assets\tray"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

for ($f = 0; $f -lt $frameCount; $f++) {
    $glowT = [Math]::Sin(($f / $frameCount) * [Math]::PI * 2) * 0.5 + 0.5  # 0..1..0

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

    # Pulsing glow halo behind the wave lines
    $glowAlpha = [int](40 + $glowT * 130)
    $glowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($glowAlpha, 255, 255, 255))
    $glowSize = $size * (0.55 + $glowT * 0.15)
    $gx = ($size - $glowSize) / 2
    $gy = ($size - $glowSize) / 2
    $g.FillEllipse($glowBrush, $gx, $gy, $glowSize, $glowSize)

    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.0, $size * 0.09))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $w = $size * 0.62
    $x0 = ($size - $w) / 2
    $yMid = $size * 0.58
    $wavePath1 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wavePath1.AddBezier([float]$x0, [float]$yMid, [float]($x0 + $w * 0.25), [float]($yMid - $size * 0.22), [float]($x0 + $w * 0.75), [float]($yMid + $size * 0.22), [float]($x0 + $w), [float]$yMid)
    $g.DrawPath($pen, $wavePath1)

    $pen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(210, 255, 255, 255), [Math]::Max(1.0, $size * 0.06))
    $pen2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen2.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $yMid2 = $size * 0.38
    $wavePath2 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wavePath2.AddBezier([float]$x0, [float]$yMid2, [float]($x0 + $w * 0.25), [float]($yMid2 - $size * 0.16), [float]($x0 + $w * 0.75), [float]($yMid2 + $size * 0.16), [float]($x0 + $w), [float]$yMid2)
    $g.DrawPath($pen2, $wavePath2)

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
