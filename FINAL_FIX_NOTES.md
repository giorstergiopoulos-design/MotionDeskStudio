# Final build-fix pass

- Fixed DeskZoneState persistence to include Width/Height.
- Fixed EventHandler invocation to pass sender and EventArgs.
- Replaced reflection-style Marshal.PtrToStructure cast with generic overload.
- Tightened nullable annotations for StartupManager, TrayApplicationContext and WallpaperInterop.
- Kept WinForms-only WebView2 references; WPF build assets remain excluded.

Web research checked Microsoft guidance for EventHandler signatures and nullable reference types.
