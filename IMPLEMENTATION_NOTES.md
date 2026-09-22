# MotionDesk Studio - Feature/UI pass

This pass fixes the application shell so feature pages contain usable controls instead of overlapping/placeholder controls.

## Visible features
- Widget Gallery: System Monitor, Clock, Network, Audio Visualizer, Weather, close-all.
- DeskZones: 2/3-column and 2x2/3x2 presets, custom zone, close-all.
- Wallpaper Studio: enable/disable, video selection, Aurora fallback, performance modes.
- System Monitor: live CPU/RAM/network/process/power metrics.
- Workspace Profiles: save/load and create Work/Gaming/Focus presets.
- Performance Center and Automation pages.
- Command Palette with keyboard execution.
- Global hotkeys for Gaming and showing the app.

## Lightweight design
- Native WinForms widgets are used for clock/network/audio/weather/system monitor.
- WebView2 remains lazy-loaded only for rich HTML widgets.
- Weather is refreshed every 15 minutes.
- Network/performance metrics are cached.
- Native widgets stop their timers when closed.
