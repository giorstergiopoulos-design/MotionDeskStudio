Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KeySim {
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
"@
$VK_CONTROL = 0x11
$VK_MENU = 0x12  # Alt
$VK_F = 0x46
$KEYEVENTF_KEYUP = 0x0002

[KeySim]::keybd_event($VK_CONTROL, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 50
[KeySim]::keybd_event($VK_MENU, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 50
[KeySim]::keybd_event($VK_F, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[KeySim]::keybd_event($VK_F, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)
[KeySim]::keybd_event($VK_MENU, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)
[KeySim]::keybd_event($VK_CONTROL, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)
Write-Host "Sent Ctrl+Alt+F via keybd_event"
