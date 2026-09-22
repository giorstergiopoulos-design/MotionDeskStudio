param([int]$X, [int]$Y)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinAbs {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
}
"@
[WinAbs]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 80
[WinAbs]::mouse_event(0x0002, 0, 0, 0, 0)
Start-Sleep -Milliseconds 50
[WinAbs]::mouse_event(0x0004, 0, 0, 0, 0)
Write-Host "Clicked at ($X, $Y)"
