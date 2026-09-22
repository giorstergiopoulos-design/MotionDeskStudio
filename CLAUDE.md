\# CLAUDE.md — MotionDesk Architecture \& Development Guidelines



\## Project Context

\- \*\*Application Name:\*\* MotionDesk

\- \*\*Target Platform:\*\* Windows 10 / Windows 11

\- \*\*Framework:\*\* .NET 8.0 (WPF / Win32 Interop)



\## Primary Priority Task: MotionDesk Desktop Engine (WorkerW Injection)

Fix and decouple the animated wallpaper renderer so that live backgrounds run smoothly behind Windows desktop icons.



\### Execution Steps

1\. \*\*Desktop Injection:\*\*

&#x20;  - Post message `0x052C` to `Progman` to spawn the hidden `WorkerW` handle.

&#x20;  - Attach the `MotionDesk` background rendering HWND to `WorkerW` using `SetParent(hWnd, workerW)`.

2\. \*\*Performance \& Fullscreen Saver:\*\*

&#x20;  - Detect active full-screen windows (e.g., games, videos) using `GetForegroundWindow()` and `SHQueryUserNotificationState()`.

&#x20;  - Automatically pause the rendering engine (0% GPU/CPU usage) while full-screen applications are running.

3\. \*\*Windows 11 Visual Effects:\*\*

&#x20;  - Replace legacy WPF `AllowsTransparency` with native DWM Mica / Acrylic backdrops using `DwmSetWindowAttribute`.

4\. \*\*Multi-DPI Handling:\*\*

&#x20;  - Set DPI Awareness to `PerMonitorV2` in the application manifest.



\## Build Commands

\- \*\*Build:\*\* `dotnet build MotionDesk.csproj -c Release`

\- \*\*Run:\*\* `dotnet run --project MotionDesk.csproj`

