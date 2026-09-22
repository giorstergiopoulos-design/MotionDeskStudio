param([string]$OutPath = "C:\MotionDeskStudio\assets\flip3d.png")
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinFlip {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
$pids = (Get-Process -Name MotionDesk -ErrorAction SilentlyContinue) | ForEach-Object { $_.Id }
$found = [IntPtr]::Zero
[WinFlip]::EnumWindows({
    param($hwnd, $lparam)
    if ([WinFlip]::IsWindowVisible($hwnd)) {
        $procId = 0
        [WinFlip]::GetWindowThreadProcessId($hwnd, [ref]$procId) | Out-Null
        if ($pids -contains $procId) {
            $r = New-Object WinFlip+RECT
            [WinFlip]::GetWindowRect($hwnd, [ref]$r) | Out-Null
            $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
            if ($w -gt 1500 -and $h -gt 900) { $script:found = $hwnd; return $false }
        }
    }
    return $true
}, [IntPtr]::Zero)
if ($found -eq [IntPtr]::Zero) { Write-Host "Flip3D window not found"; exit 1 }

$rect = New-Object WinFlip+RECT
[WinFlip]::GetWindowRect($found, [ref]$rect) | Out-Null
Add-Type -AssemblyName System.Drawing
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WinFlip]::PrintWindow($found, $hdc, 2)
$g.ReleaseHdc($hdc)
if (-not $ok) { Write-Host "PrintWindow failed"; exit 1 }
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Saved $OutPath ($w x $h)"
