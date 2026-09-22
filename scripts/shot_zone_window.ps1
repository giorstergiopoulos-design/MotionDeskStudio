param(
    [string]$OutPath = "C:\MotionDeskStudio\assets\shot_zone.png"
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinZone {
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
$procs = Get-Process -Name MotionDesk -ErrorAction SilentlyContinue
if (-not $procs) { Write-Host "No MotionDesk process"; exit 1 }
$pids = $procs | ForEach-Object { $_.Id }

$found = [IntPtr]::Zero
[WinZone]::EnumWindows({
    param($hwnd, $lparam)
    if ([WinZone]::IsWindowVisible($hwnd)) {
        $sb = New-Object System.Text.StringBuilder 256
        [WinZone]::GetWindowText($hwnd, $sb, 256) | Out-Null
        $title = $sb.ToString()
        $procId = 0
        [WinZone]::GetWindowThreadProcessId($hwnd, [ref]$procId) | Out-Null
        if (($pids -contains $procId) -and $title -ne "MotionDesk Studio" -and $title -ne "") {
            $script:found = $hwnd
            return $false
        }
        if (($pids -contains $procId) -and $title -eq "") {
            $rect = New-Object WinZone+RECT
            [WinZone]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
            $w = $rect.Right - $rect.Left
            $h = $rect.Bottom - $rect.Top
            if ($w -gt 100 -and $h -gt 100) { $script:found = $hwnd; return $false }
        }
    }
    return $true
}, [IntPtr]::Zero)

if ($found -eq [IntPtr]::Zero) { Write-Host "Zone window not found"; exit 1 }

$rect = New-Object WinZone+RECT
[WinZone]::GetWindowRect($found, [ref]$rect) | Out-Null
Add-Type -AssemblyName System.Drawing
$w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WinZone]::PrintWindow($found, $hdc, 2)
$g.ReleaseHdc($hdc)
if (-not $ok) { Write-Host "PrintWindow failed"; exit 1 }
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Saved $OutPath ($w x $h)"
