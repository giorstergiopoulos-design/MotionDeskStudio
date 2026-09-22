# Βασικό αυτοματοποιημένο smoke test: εκκινεί την εφαρμογή, κάνει κλικ σε κάθε βασική σελίδα/
# ενέργεια, και ελέγχει το crash.log στο τέλος. Δεν αντικαθιστά πραγματικά unit/integration
# tests, αλλά τυποποιεί (και κάνει επαναλήψιμο) το χειροκίνητο click-testing που γινόταν μέχρι
# τώρα σε κάθε αλλαγή.
param(
    [string]$ExePath = "C:\MotionDeskStudio\bin\Release\net8.0-windows10.0.19041.0\win-x64\MotionDesk.exe",
    [string]$CrashLogPath = "$env:APPDATA\MotionDeskStudio\crash.log"
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SmokeWin {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

function Send-Key($vk) {
    [SmokeWin]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    [SmokeWin]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
}

# Πιο αξιόπιστο από χειροποίητο EnumWindows: το ίδιο το .NET Process object εκθέτει το
# MainWindowHandle μόλις δημιουργηθεί το πρώτο ορατό παράθυρο της διεργασίας.
function Find-MainWindow($proc) {
    for ($i = 0; $i -lt 20; $i++) {
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero -and $proc.MainWindowTitle -eq "MotionDesk Studio") {
            return $proc.MainWindowHandle
        }
        Start-Sleep -Milliseconds 300
    }
    return [IntPtr]::Zero
}

function Click-Relative($hwnd, $x, $y) {
    $rect = New-Object SmokeWin+RECT
    [SmokeWin]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    [SmokeWin]::SetCursorPos($rect.Left + $x, $rect.Top + $y) | Out-Null
    Start-Sleep -Milliseconds 80
    [SmokeWin]::mouse_event(0x0002, 0, 0, 0, 0)
    Start-Sleep -Milliseconds 50
    [SmokeWin]::mouse_event(0x0004, 0, 0, 0, 0)
}

$results = @()
function Step($name, $action) {
    try {
        & $action
        Start-Sleep -Milliseconds 400
        $script:results += [PSCustomObject]@{ Step = $name; Status = "OK" }
    } catch {
        $script:results += [PSCustomObject]@{ Step = $name; Status = "EXCEPTION: $($_.Exception.Message)" }
    }
}

if (Test-Path $CrashLogPath) { Remove-Item $CrashLogPath -Force }

if (-not (Test-Path $ExePath)) { Write-Host "EXE not found: $ExePath"; exit 1 }
$proc = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Milliseconds 1500

$hwnd = Find-MainWindow $proc
if ($hwnd -eq [IntPtr]::Zero) {
    Write-Host "FAIL: main window not found after launch"
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    exit 1
}
[SmokeWin]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 300

# Πλοήγηση σε κάθε σελίδα του πλευρικού μενού (θέσεις βασισμένες στο 1120x700 default layout).
Step "Nav: Widgets"     { Click-Relative $hwnd 100 220 }
Step "Nav: DeskZones"   { Click-Relative $hwnd 100 266 }
# DeskZones is now FancyZones-style (data-driven layouts, no persistent zone windows) — exercise
# the layout editor dialog via its global shortcut instead of clicking page-specific coordinates.
Step "DeskZones: open+cancel layout editor" {
    [SmokeWin]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)   # Ctrl down
    [SmokeWin]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero)   # Shift down
    [SmokeWin]::keybd_event(0x4E, 0, 0, [UIntPtr]::Zero)   # N down
    [SmokeWin]::keybd_event(0x4E, 0, 2, [UIntPtr]::Zero)   # N up
    [SmokeWin]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero)   # Shift up
    [SmokeWin]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)   # Ctrl up
    Start-Sleep -Milliseconds 500
    Send-Key 0x1B   # Escape — closes the modal editor dialog
}
Step "Nav: Wallpaper"   { Click-Relative $hwnd 100 312 }
Step "Nav: Performance" { Click-Relative $hwnd 100 358 }
Step "Nav: Settings"    { Click-Relative $hwnd 100 404 }
Step "Nav: Profiles"    { Click-Relative $hwnd 100 450 }
Step "Nav: Automation"  { Click-Relative $hwnd 100 496 }
Step "Nav: Personalization" { Click-Relative $hwnd 100 542 }
Step "Nav: About"       { Click-Relative $hwnd 100 588 }
Step "Nav: Dashboard"   { Click-Relative $hwnd 100 174 }
Step "Sidebar: collapse" { Click-Relative $hwnd 124 639 }
Step "Sidebar: expand"   { Click-Relative $hwnd 40 639 }

Start-Sleep -Milliseconds 500

if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }

# Καθαρισμός των test artifacts που δημιουργεί το ίδιο το script (το "DeskZones: create 2x2
# layout" βήμα) — ΠΡΑΓΜΑΤΙΚΟ bug προκλήθηκε επειδή αυτά έμεναν πίσω: η "Last Session" ανοίγει
# αυτόματα στην επόμενη εκκίνηση της εφαρμογής (αν RestoreLastSession=true), οπότε αν ο χρήστης
# την είχε ήδη αποθηκεύσει μετά από ένα smoke test, θα έβλεπε τα ΔΟΚΙΜΑΣΤΙΚΑ zones "να ανοίγουν
# μόνα τους". Το Stop-Process -Force παρακάμπτει το FormClosed (άρα δεν ξαναγράφεται συνήθως η
# Last Session μέσα από αυτό το script) αλλά τα raw zone config αρχεία μένουν ούτως ή άλλως.
$zoneDir = "$env:APPDATA\MotionDeskStudio\deskzones"
if (Test-Path $zoneDir) {
    Get-ChildItem $zoneDir -Filter "zone-*_config.json" | Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "=== Smoke test steps ==="
$results | Format-Table -AutoSize

if (Test-Path $CrashLogPath) {
    Write-Host ""
    Write-Host "=== CRASH LOG FOUND ==="
    Get-Content $CrashLogPath
    Write-Host ""
    Write-Host "RESULT: FAIL (crash detected)"
    exit 1
} else {
    Write-Host ""
    Write-Host "RESULT: PASS (no crash logged)"
    exit 0
}
