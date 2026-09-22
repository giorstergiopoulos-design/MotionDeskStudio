MotionDesk Studio — Complete Feature Build

Included in this source package:
- Real WinForms application shell with Dashboard, Widgets, DeskZones, Wallpaper,
  System Monitor, Settings, Profiles, Performance, Automation and About pages.
- Native lightweight widgets: System Monitor, Clock, Network, Audio Visualizer, Weather.
- Widget drag support, lock/unlock context menu, close action, persistent position/size.
- Snap-to-screen, snap-to-widget, optional grid snapping and snap guides.
- Multi-monitor position recovery.
- DeskZone layouts: 2-column, 3-column, 2x2, 3x2 and custom zones.
- DeskZone persistent position/size/title plus rename/close context menu.
- Live wallpaper with video selection, Aurora fallback and performance modes:
  High, Balanced, Low Power and Battery.
- Battery-aware automatic wallpaper performance selection.
- Optional live wallpaper startup.
- Shared/lazy WebView2 environment; WebView2 is not used for native widgets.
- Shared native system metrics via Win32 instead of a PerformanceCounter per widget.
- AudioPeakService disposal and lazy activation.
- Workspace Profiles with AppSettings, wallpaper state, DeskZones and active widget layout.
- Last Session automatic save/restore.
- Work/Gaming/Focus profile quick actions.
- Process-triggered automation rules.
- Command Palette.
- Global hotkeys: Ctrl+Alt+G (Gaming/Work), Ctrl+Alt+M (show manager),
  Ctrl+Alt+F (Flip 3D).
- Tray integration, startup toggle and wallpaper controls.
- Lightweight .NET 8 / WinForms / win-x64 / framework-dependent publish configuration.
- WPF disabled; stale core sources excluded from compilation.
- Existing localization assets retained.

Build on Windows:
  dotnet restore
  dotnet clean
  dotnet build -c Release
  dotnet publish -c Release -r win-x64 --self-contained false

The current environment does not have the .NET SDK installed, so this delivery was
assembled and statically reviewed here but not compiled in this environment.
