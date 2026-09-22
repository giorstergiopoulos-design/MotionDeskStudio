param([int]$X, [int]$Y)
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinPop {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
$found = [IntPtr]::Zero
[WinPop]::EnumWindows({
    param($hwnd, $lparam)
    if ([WinPop]::IsWindowVisible($hwnd)) {
        $sb = New-Object System.Text.StringBuilder 256
        [WinPop]::GetWindowText($hwnd, $sb, 256) | Out-Null
        if ($sb.ToString() -eq "MotionDesk Studio") { $script:found = $hwnd; return $false }
    }
    return $true
}, [IntPtr]::Zero)
if ($found -eq [IntPtr]::Zero) { Write-Host "Main window not found"; exit 1 }

[WinPop]::SetForegroundWindow($found) | Out-Null
Start-Sleep -Milliseconds 200
$rect = New-Object WinPop+RECT
[WinPop]::GetWindowRect($found, [ref]$rect) | Out-Null

$absX = $rect.Left + $X
$absY = $rect.Top + $Y
[WinPop]::SetCursorPos($absX, $absY) | Out-Null
Start-Sleep -Milliseconds 80
[WinPop]::mouse_event(0x0002, 0, 0, 0, 0)
Start-Sleep -Milliseconds 50
[WinPop]::mouse_event(0x0004, 0, 0, 0, 0)
Start-Sleep -Milliseconds 400

$pids = @()
Add-Type -AssemblyName System | Out-Null
$procId = 0
[WinPop]::GetWindowThreadProcessId($found, [ref]$procId) | Out-Null

[WinPop]::EnumWindows({
    param($hwnd, $lparam)
    if ([WinPop]::IsWindowVisible($hwnd) -and $hwnd -ne $found) {
        $pid2 = 0
        [WinPop]::GetWindowThreadProcessId($hwnd, [ref]$pid2) | Out-Null
        if ($pid2 -eq $procId) {
            $sb2 = New-Object System.Text.StringBuilder 256
            [WinPop]::GetClassName($hwnd, $sb2, 256) | Out-Null
            $r2 = New-Object WinPop+RECT
            [WinPop]::GetWindowRect($hwnd, [ref]$r2) | Out-Null
            $w2 = $r2.Right - $r2.Left; $h2 = $r2.Bottom - $r2.Top
            if ($w2 -gt 0 -and $h2 -gt 0) {
                Write-Host "class=$($sb2.ToString()) rect=($($r2.Left),$($r2.Top))-($($r2.Right),$($r2.Bottom)) size=${w2}x${h2}"
            }
        }
    }
    return $true
}, [IntPtr]::Zero)
