param(
    [string]$OutPath = "C:\MotionDeskStudio\assets\shot.png",
    [int]$WaitMs = 3000
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinShot {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
Start-Sleep -Milliseconds $WaitMs
$found = [IntPtr]::Zero
[WinShot]::EnumWindows({
    param($hwnd, $lparam)
    if ([WinShot]::IsWindowVisible($hwnd)) {
        $sb = New-Object System.Text.StringBuilder 256
        [WinShot]::GetWindowText($hwnd, $sb, 256) | Out-Null
        if ($sb.ToString() -eq "MotionDesk Studio") { $script:found = $hwnd; return $false }
    }
    return $true
}, [IntPtr]::Zero)
if ($found -ne [IntPtr]::Zero) {
    $rect = New-Object WinShot+RECT
    [WinShot]::GetWindowRect($found, [ref]$rect) | Out-Null
    Add-Type -AssemblyName System.Drawing
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    # PW_RENDERFULLCONTENT (0x2) captures the window's own content directly via its HWND,
    # regardless of Z-order/focus — avoids accidentally grabbing whatever else is on screen.
    $ok = [WinShot]::PrintWindow($found, $hdc, 2)
    $g.ReleaseHdc($hdc)
    if (-not $ok) { Write-Host "PrintWindow failed"; exit 1 }
    $bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Saved $OutPath ($w x $h)"
} else { Write-Host "MotionDesk Studio window not found" }
