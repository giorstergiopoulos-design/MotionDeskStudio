param(
    [int]$X1, [int]$Y1,
    [int]$X2, [int]$Y2,
    [string]$OutPath = "C:\MotionDeskStudio\assets\shot.png",
    [int]$WaitBetween = 400,
    [int]$WaitAfter = 800
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinDC {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
$found = [IntPtr]::Zero
[WinDC]::EnumWindows({
    param($hwnd, $lparam)
    if ([WinDC]::IsWindowVisible($hwnd)) {
        $sb = New-Object System.Text.StringBuilder 256
        [WinDC]::GetWindowText($hwnd, $sb, 256) | Out-Null
        if ($sb.ToString() -eq "MotionDesk Studio") { $script:found = $hwnd; return $false }
    }
    return $true
}, [IntPtr]::Zero)

if ($found -eq [IntPtr]::Zero) { Write-Host "Window not found"; exit 1 }

[WinDC]::SetForegroundWindow($found) | Out-Null
Start-Sleep -Milliseconds 200

$rect = New-Object WinDC+RECT
[WinDC]::GetWindowRect($found, [ref]$rect) | Out-Null

function Click($x, $y) {
    $absX = $rect.Left + $x
    $absY = $rect.Top + $y
    [WinDC]::SetCursorPos($absX, $absY) | Out-Null
    Start-Sleep -Milliseconds 80
    [WinDC]::mouse_event(0x0002, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 50
    [WinDC]::mouse_event(0x0004, 0, 0, 0, 0)
}

Click $X1 $Y1
Start-Sleep -Milliseconds $WaitBetween
Click $X2 $Y2
Start-Sleep -Milliseconds $WaitAfter

Add-Type -AssemblyName System.Drawing
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WinDC]::PrintWindow($found, $hdc, 2)
$g.ReleaseHdc($hdc)
if (-not $ok) { Write-Host "PrintWindow failed"; exit 1 }
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Saved $OutPath ($w x $h)"
