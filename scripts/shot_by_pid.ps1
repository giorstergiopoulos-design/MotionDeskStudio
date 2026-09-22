param([int]$TargetPid, [string]$OutPath = "C:\MotionDeskStudio\assets\shot.png", [int]$WaitMs = 1000)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinPid {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
Start-Sleep -Milliseconds $WaitMs
$found = [IntPtr]::Zero
[WinPid]::EnumWindows({
    param($hwnd, $lparam)
    if ([WinPid]::IsWindowVisible($hwnd)) {
        $procId = 0
        [WinPid]::GetWindowThreadProcessId($hwnd, [ref]$procId) | Out-Null
        if ($procId -eq $TargetPid) {
            $sb = New-Object System.Text.StringBuilder 256
            [WinPid]::GetWindowText($hwnd, $sb, 256) | Out-Null
            if ($sb.ToString() -eq "MotionDesk Studio") { $script:found = $hwnd; return $false }
        }
    }
    return $true
}, [IntPtr]::Zero)
if ($found -eq [IntPtr]::Zero) { Write-Host "Window not found for PID $TargetPid"; exit 1 }

$rect = New-Object WinPid+RECT
[WinPid]::GetWindowRect($found, [ref]$rect) | Out-Null
Add-Type -AssemblyName System.Drawing
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WinPid]::PrintWindow($found, $hdc, 2)
$g.ReleaseHdc($hdc)
if (-not $ok) { Write-Host "PrintWindow failed"; exit 1 }
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Saved $OutPath ($w x $h)"
