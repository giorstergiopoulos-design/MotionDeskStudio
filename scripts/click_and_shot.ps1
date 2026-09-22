param(
    [int]$X,
    [int]$Y,
    [string]$OutPath = "C:\MotionDeskStudio\assets\shot.png",
    [int]$WaitMsAfterClick = 1200
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinClick {
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
[WinClick]::EnumWindows({
    param($hwnd, $lparam)
    if ([WinClick]::IsWindowVisible($hwnd)) {
        $sb = New-Object System.Text.StringBuilder 256
        [WinClick]::GetWindowText($hwnd, $sb, 256) | Out-Null
        if ($sb.ToString() -eq "MotionDesk Studio") { $script:found = $hwnd; return $false }
    }
    return $true
}, [IntPtr]::Zero)

if ($found -eq [IntPtr]::Zero) { Write-Host "Window not found"; exit 1 }

[WinClick]::SetForegroundWindow($found) | Out-Null
Start-Sleep -Milliseconds 200

$rect = New-Object WinClick+RECT
[WinClick]::GetWindowRect($found, [ref]$rect) | Out-Null
$absX = $rect.Left + $X
$absY = $rect.Top + $Y

[WinClick]::SetCursorPos($absX, $absY) | Out-Null
Start-Sleep -Milliseconds 100
[WinClick]::mouse_event(0x0002, 0, 0, 0, 0)  # MOUSEEVENTF_LEFTDOWN
Start-Sleep -Milliseconds 60
[WinClick]::mouse_event(0x0004, 0, 0, 0, 0)  # MOUSEEVENTF_LEFTUP

Start-Sleep -Milliseconds $WaitMsAfterClick

Add-Type -AssemblyName System.Drawing
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WinClick]::PrintWindow($found, $hdc, 2)
$g.ReleaseHdc($hdc)
if (-not $ok) { Write-Host "PrintWindow failed"; exit 1 }
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Clicked ($absX,$absY) -- Saved $OutPath ($w x $h)"
