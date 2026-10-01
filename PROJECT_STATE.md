# PROJECT STATE

## Project
Name: MotionDesk Studio

## Technology
- .NET: 8.0 (net8.0-windows10.0.19041.0), win-x64
- C#: WinForms (NOT WPF — the older root CLAUDE.md said WPF; corrected here per actual source, System.Windows.Forms throughout)
- UI: Native WinForms, WebView2 for wallpaper/HTML widgets
- Mobile: N/A
- Backend: N/A (local desktop app only)

## Current Objective
Work through the v1.5.0 request list (see `MOTIONDESK STUDIO.MD` on the user's Desktop) — installer polish, DeskZones stretch bug, container rename, wallpaper transition fade, sysmon widget startup hitch, license button styling, per-language string cleanup, editable/deletable automation rules, full custom Windows theme (icons+wallpapers), AV/signature trust, full manual QA pass.

## Current Phase
Triage of the v1.5.0 list — most items are clear and actionable; three (icon/theme pack scope, recreating the Windows 11 animated wallpaper, digital-signature/AV trust) need a scoping decision from the user before implementation (see Blocked).

## Completed (this long session, uncommitted — see Known Limitations)
- WorkerW/Progman desktop wallpaper injection (classic + Windows 11 "raised desktop" paths), self-healing boot-race retry.
- DeskContainers: Fences-style menu, hover roll-up, large icons, title-bar/icon-grid spacing fix (twice), drag&drop, opacity, quick-hide integration.
- Widgets: Clock/Network/Audio/Weather/Disk/SystemMonitor native widgets; per-widget Opacity menu; native resize grip; independent widget/container theme (`WidgetsThemeMode`); uniform default size; System Monitor redesigned with colored meter bars + black bold text (colors confirmed on bars only, not text, per explicit user correction).
- Network widget: selectable Sparkline / Bars style, real down/up values on the Bars style.
- WMV→MP4 pipeline via FFmpeg (`WmvConversionService`), user-configurable output folder.
- `DependencyManagerService` + Settings "Dependencies" section (FFmpeg, WebView2 Runtime — status + winget install button); installer has an optional FFmpeg winget task.
- DeskFlip (Ctrl+Alt+F, Vista Flip3D homage): real desktop blurred as background, true perspective trapezoid via supersampled strip-warp (2x buffer downscale) — seam artifacts from the naive strip-warp fixed and live-verified.
- Installer (`installer.iss`) compiles cleanly via Inno Setup 6 (`C:\Users\gstrj\AppData\Local\Programs\Inno Setup 6\ISCC.exe`); `Output\MotionDeskStudioSetup.exe` built and delivered to user twice this session.
- Orphaned test `deskcontainers/container*_config.json` files cleaned up.

## In Progress
- v1.5.0 list — not yet started item-by-item (just received). See Next Actions.

## Blocked
- None currently. Scoping decisions made 2026-09-29 (see Important Decisions): REQ-010 = extend IconAtlasEngine depot coverage + new .theme + one new tech wallpaper; REQ-011 = original recreation, not asset copy; REQ-012 = skipped this round (no certificate available).

## Next Actions (v1.5.0 checklist) — status as of 2026-09-29, end of session
- REQ-001 GearWin-style user guide page — DONE (code, builds clean) / NOT VISUALLY VERIFIED (ran out of session time to screenshot it specifically). New "Help" nav page + F1 shortcut (About moved off F1, still on Ctrl+9), built from topic cards reusing existing localized intro strings (no new translation debt) plus the existing full-instructions/shortcuts text.
- REQ-002 Load video playlists from .zip/.7z archives — DONE (code, builds clean) / NOT LIVE-TESTED (no sample archive on hand). New `ArchivePlaylistService`: .zip via built-in `System.IO.Compression`, .7z by shelling out to an installed 7-Zip (same external-tool pattern as FFmpeg) with a download-page prompt if missing. Wired into the existing "Add video" dialog/flow.
- REQ-003 DeskZones: stop windows stretching past screen edges — DONE (code) / NOT VERIFIED live. Root cause: `ZoneSnapEngine.SetWindowPos` used the zone rect directly, ignoring the invisible DWM resize-border margin most windows have — visible as a gap at screen edges. Fixed via `AdjustForInvisibleFrame` (compares `GetWindowRect` vs `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`).
- REQ-004 DeskContainers: double-click title to rename — DONE (code) / NOT VERIFIED live. `titleBar.DoubleClick` now calls `RenameContainer()` instead of the now-redundant `ToggleRollUp` (roll-up is hover-driven already).
- REQ-005 Smooth fade transition between wallpaper videos/playlist items (no blank flash) — DONE (code, builds clean) / NOT VISUALLY VERIFIED. `wallpaper/index.html` now double-buffers with two `<video>` layers and CSS opacity crossfade (650ms) instead of swapping `.src` on one element.
- REQ-006 System Monitor widget load causes a momentary app + Windows-wide hitch — DONE (code) / LIVE-VERIFIED earlier this session (colored bars + no-hitch load confirmed by screenshot before this final batch).
- REQ-007 License button styled like GearWin's — DONE, LIVE-VERIFIED. Turned out the real bug (per user correction) was that clicking it launched the OS "Open with" picker (LICENSE has no file extension) instead of GearWin's actual behavior of showing the license text in-app. Fixed: `ShowLicenseDialog()` reads the file and displays it in a themed in-app window. Screenshot-confirmed working.
- REQ-008 Per-language locale files must not mix EL+EN strings — PARTIAL, same as before (see reasoning below); a couple of new keys added this batch (`Personalization.NetworkIcon` etc.) went through localization properly, but the broader sweep of session-added hardcoded strings elsewhere is still not done.
- REQ-009 Automation rules: edit/delete — DONE, LIVE-VERIFIED (Edit…/Delete buttons + "Add rule…" dialog confirmed working; also used to clean up 4 duplicate test rules).
- REQ-010 Full custom Windows icon set + new .theme + tech wallpaper pack — DONE (code, builds clean) / NOT VISUALLY VERIFIED. `IconAtlasEngine` extended with Network/Control-Panel/User's-Files CLSID icon slots (the real ceiling of what Windows allows). New "Tech Grid" theme package button generates a static circuit-grid PNG (`ThemePackageEngine.GenerateTechWallpaperPng`) and saves it as a real `.theme` file via the existing `SaveCurrentAsTheme` (now takes an optional wallpaper override).
- REQ-011 Recreate Windows 11's animated desktop background in new color variants — DONE (code, builds clean) / NOT VISUALLY VERIFIED. New "TechGrid" WaveStyle in the wallpaper canvas engine (scrolling circuit-board grid, glowing nodes, traveling data-pulse lines) selectable in Wallpaper Studio's style dropdown; two new palettes ("Matrix Green", "Cyber Neon") added so it (and the other styles) have tech-appropriate color variants via the existing palette system.
- REQ-012 AV/SmartScreen trust via proper code signing — SKIPPED this round per user (no certificate available).
- REQ-013 Full manual click-through QA pass of every control — PARTIAL. Verified live this session: DeskFlip, System Monitor colors/hitch, Automation CRUD, License dialog. NOT re-verified visually due to running low on session time/usage: Help page, TechGrid wallpaper rendering, zip/7z add-video flow, new Personalization icon rows, Tech Grid theme button. All of the above build with 0 errors and follow patterns already proven elsewhere in this codebase (e.g. TechGrid reuses the exact same canvas/profile/palette plumbing as the already-working Aurora style) — code-complete but genuinely NOT VERIFIED live, per protocol rule 15 ("state NOT VERIFIED rather than pretend").

## Blocked
- None outstanding.

## v1.5.0 batch 2 (same session, 2026-09-29) — user follow-up requests after batch 1 shipped
- DeskZones "selected 2 rows, saw 3 columns" bug — DONE, LIVE-VERIFIED. Root cause: `ZoneLayoutEditorForm.BuildSelected()` only passed the cols/rows stepper values for the "Custom" template; "Rows"/"Columns"/"Grid" always used hardcoded defaults (3,2), AND their steppers were hidden entirely so there was no way to adjust them. "Custom" additionally always builds a full grid (both axes), so adjusting only the rows stepper there also pulled in the default 3-column split — that's what looked like "3 columns" to the user. Fixed: each template now shows only its relevant stepper(s) and `BuildSelected()` always passes current values. Confirmed live: Rows template now shows only the Rows stepper and renders exactly N full-width rows.
- DeskZones "stretch still doesn't work" — the earlier `AdjustForInvisibleFrame` fix (batch 1) stands; NOT independently re-verified this round (needs a real Shift-drag test, not easily scriptable).
- AV false-positive mitigation — DONE. Added Company/Authors/Copyright/Description to the csproj (PE metadata heuristic). Real fix is still a paid cert (skipped, no cert available) — noted honestly in code comment.
- Clock widget: analog themes (Classic/Neon/Minimal) + digital font family (5 choices) + digital color (5 presets + theme-default) — DONE, builds clean, not re-screenshotted this round (menu-driven, low risk, follows the exact pattern already proven for AudioStyle/NetworkStyle).
- Disk widget redesigned as a circular ring (donut) per drive with info centered inside, ◀/▶ paging buttons instead of scroll — DONE, builds clean, not re-screenshotted (same reasoning).
- Wallpaper: per-video enable/disable checkbox (so a loaded video can sit in the library without being in the active rotation) — DONE, builds clean. Shuffle toggle already existed from before, not new.
- Main window enlarged (960×600 min / 1120×700 default → 1040×680 min / 1280×800 default) for the new Audio Enhancement page — DONE, LIVE-VERIFIED (nav list fits without clipping at the new size).
- **Audio Enhancement** (replaces "Audio Visualizer", inspired by FXSound's *approach*, not copied) — DONE, LIVE-VERIFIED (screenshot confirms the new page renders: 5 presets, live visualizer, WMP/Winamp style toggle). **Important scope note, stated explicitly in-app and to the user**: this does NOT change real system-wide audio playback — that needs a signed Audio Processing Object registered in the Windows driver graph (what FXSound actually is), out of reach for a .NET/WinForms project without that component. What's real: `AudioSpectrumService.BandGains`/`ApplyPreset` genuinely reshapes the live WASAPI-loopback spectrum analysis used by the equalizer/visualizer. New shared setting `AppSettings.AudioEnhancementPreset` syncs the floating widget and the new page (widget picks it up on next open/menu use, not live-pushed to an already-open widget — minor known gap).
- Locale keys added for all of the above in both `el-GR.json`/`en-US.json` (Nav.Help, Nav.AudioEnhancement, Help.*, AudioEnhancement.*).

## Audio Enhancement — real system-wide capability added (2026-09-29, same day)
User pushed back on the "visualizer-only" scoping and asked me to research the real FXSound repo and similar GitHub projects before concluding it's infeasible. Did that (WebFetch on fxsound2/fxsound-app, WebSearch + WebFetch on Equalizer APO and github.com/psidex/EACS):
- **Confirmed**: even FXSound's own open-source repo does NOT include real-time system audio processing — that part is a separate, closed, kernel-signed "virtual audio driver" not present in their repo. This validates the original assessment that a from-scratch APO/driver is out of scope here.
- **Found a real path**: Equalizer APO (equalizerapo.sourceforge.io) is a free, open-source, already-signed, already-trusted Windows Audio Processing Object that runs system-wide with NO virtual driver needed. `github.com/psidex/EACS` confirmed the integration pattern used by real projects: just write its `config.txt`.
- **Implemented**: `EqualizerApoService.cs` — detects install (`C:\Program Files\EqualizerAPO\config`), and applies presets as real parametric EQ filters via a separate `MotionDesk.txt` + an `Include:` line appended to `config.txt` (non-destructive to whatever the user already has configured there). Same external-tool pattern already established for FFmpeg/7-Zip in this project.
- Wired into both the Audio Enhancement page (status card + "Get Equalizer APO" button when missing) and the floating widget's preset menu.
- **DONE, LIVE-VERIFIED** (not-installed state confirmed rendering correctly with working install-prompt button). The installed/system-wide-active state was NOT tested live (Equalizer APO isn't installed on this machine) — the file-write logic itself is straightforward and low-risk, but flagging honestly per protocol.

## Wallpaper audio DSP (2026-09-29, same day) — real EQ on the wallpaper's own video, not system-wide
User accepted the offered alternative to a custom Equalizer-APO-like build: real bass/mid/treble/volume DSP applied only to MotionDesk's own wallpaper video audio, via the Web Audio API inside the WebView2 page (no external tool, no signing, works today).
- `wallpaper/index.html`: `ensureAudioGraph()`/`routeVideoAudio()`/`applyAudioConfig()` — BiquadFilterNode chain (lowshelf/peaking/highshelf) + GainNode, routed from both video layers via `createMediaElementSource`. Videos always start `muted` (guarantees autoplay succeeds) and are unmuted only after `.play()` succeeds AND `cfg.audioEnabled` is true, avoiding a Chromium autoplay-block from ever masquerading as a video error.
- `WallpaperSettings`/`WallpaperHostEngine.SetAudioSettings(...)` (C# side, `WallpaperWindow.cs`) — 5 new fields (`AudioEnabled` default **false**, `AudioVolume`, `AudioBassGain`/`AudioMidGain`/`AudioTrebleGain`), serialized into `GetWaveConfigJson()`.
- New "Ήχος Wallpaper" section on the Wallpaper Studio page (`MainWindow.cs`, inside the existing `Mode == "Video"` block): enable checkbox + Volume/Bass/Mid/Treble sliders, wired live to `SetAudioSettings`. New locale keys `Wallpaper.SectionAudio`/`AudioEnabled`/`AudioVolume`/`AudioBass`/`AudioMid`/`AudioTreble`/`AudioNote` in both `el-GR.json`/`en-US.json`.
- **DONE, LIVE-VERIFIED (partial)**: built and ran; switched Mode to Video, confirmed the new section renders with correct labels/values; toggled the enable checkbox and moved the Bass slider — confirmed `wallpaper.json` persisted `AudioEnabled:true`/`AudioBassGain:-3` correctly; video wallpaper was visibly rendering on the real desktop (indirect confirmation the JS parsed/ran without a fatal syntax error, since a broken `<script>` would have also killed the existing, previously-working video playback). **NOT VERIFIED**: actual audible sound output / real EQ effect on the audio (cannot be confirmed through screenshots or synthetic input — would need the user to listen). Test-only setting changes (Mode, AudioEnabled, AudioBassGain) were reverted back to their pre-test values afterward so this verification pass left no footprint on the user's real configuration.

## DeskZones edge-stretch — REAL root cause found and fixed (2026-09-30)
User tested the earlier `AdjustForInvisibleFrame` fix themselves and reported it was still wrong: "παραμένει κενό ανάμεσα στο παράθυρο και την άκρη" (a gap remains between the window and the edge). Re-investigated instead of assuming the earlier fix was sufficient.

Error:
Even with a mathematically-correct DWM invisible-frame compensation, a window snapped to an edge-touching zone still stopped short of the true screen edge by a small, constant margin.

Cause:
The REAL bug was upstream in `ZoneLayoutStore.BuildTemplate`, not in `ZoneSnapEngine` at all. Every loop-generated template (Columns/Rows/Grid/Custom) applied `gap/2` (a deliberate small visual gap between adjacent zones, like real FancyZones) **symmetrically to all four sides of every zone** — including the outer sides that touch the actual screen edge (X=0, Y=0, right=1, bottom=1), where there is no neighboring zone to justify a gap. Confirmed exactly against the user's own saved layout: a 2-row "Rows" template produced `Y: 0.006 / Height: 0.488` for the top row and `Y: 0.506 / Height: 0.488` for the bottom row — top zone starts 0.6% short of Y=0, bottom zone ends 0.6% short of Y=1, on EVERY screen resolution, regardless of `AdjustForInvisibleFrame` being perfectly correct (it can only stretch the window to match the zone it's given — and the zone itself never reached the edge).

Fix:
Rewrote `BuildTemplate`'s cell-range math (new `CellRange` local helper) so `gap/2` is applied **only on internal grid lines** — the first cell's start and the last cell's end are pinned exactly to 0.0/1.0 with zero padding — for Columns, Rows, and Grid/Custom. ("Priority Grid" was already hand-written with explicit 0/1 anchors and never had this bug; "Focus" intentionally keeps its 10% centered margin — not edge-touching by design.) Also directly migrated the user's existing saved `zonelayouts.json` (their live 2-row layout) to the corrected values (`Y:0/Height:0.494` and `Y:0.506/Height:0.494`) so the fix applies immediately without needing to redo their setup.

Affected files:
`src/Widgets/ZoneLayoutStore.cs` (`BuildTemplate`), `%AppData%\MotionDeskStudio\zonelayouts.json` (data migration, this machine only).

Prevention:
When generating any fractional/normalized layout grid, edge cells must never receive outer padding meant for inter-cell gaps — always special-case index 0 and index count-1 per axis.

Status: DONE, build clean. Root cause confirmed via direct arithmetic match against the user's own pre-existing (buggy) saved data.

## DeskZones snap engine rework (2026-09-30, same day) — user still reported "δεν λειτουργεί" after the zone-geometry fix
User pushed back again after manually testing: real Shift+drag still didn't snap, AND the overlay looked "too intense" (like a solid block) instead of translucent. Asked to research the real FancyZones (PowerToys, GitHub) architecture and reverse-engineer the *technique* (not copy code — user explicitly flagged copyright; confirmed clean-room: only identified which public Win32 API they call, wrote fully original C#).

Root cause #1 (why snapping itself was unreliable):
`ZoneSnapEngine`'s previous implementation detected "a window drag started" with its own heuristic on top of a raw `WH_MOUSE_LL` low-level mouse hook (left-button-down on a `WS_CAPTION` window, then >6px movement = "dragging"). This is NOT the same signal Windows itself uses to know a window actually entered its native move/size loop, and proved unreliable both for the user's real usage and for scripted verification (a synthetic drag visibly moved a test window but never triggered the old hook's snap logic).

Fix:
Pulled `microsoft/PowerToys` `FancyZones.cpp` via `gh api` **only to identify the technique**, then deleted the file and wrote independent C#. Real FancyZones uses `SetWinEventHook` with `EVENT_SYSTEM_MOVESIZESTART`/`EVENT_SYSTEM_MOVESIZEEND` — a documented, public Windows accessibility API that fires the OS's own authoritative signal for "a window just entered/exited its native move-or-resize loop." Rewrote `ZoneSnapEngine.cs` around this: `SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, ..., WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)` (the `SKIPOWNPROCESS` flag also auto-excludes MotionDesk's own widgets — no more manual pid filtering needed). On start: begin a 16ms poll timer tracking cursor position for the overlay/highlight. On end: commit the snap (via the existing, already-correct `AdjustForInvisibleFrame`) if Shift is still held and a zone is highlighted. Simplified `IsCandidateWindow` (dropped the `WS_CAPTION`/`WS_EX_TOOLWINDOW` style checks — the MOVESIZESTART event is already authoritative, and the old style checks could silently reject modern custom-titlebar apps).

Root cause #2 (why the overlay looked "too intense" instead of transparent):
`ZoneOverlayWindow` used `Form.TransparencyKey` (chroma-key transparency) — this only makes the *exact* key color (black) invisible; any other color, even one drawn with `Color.FromArgb(lowAlpha, ...)` in GDI+, gets flattened to a fully **opaque** pixel before the window compositor ever sees it. So the "translucent" zone fills were actually rendering as solid, opaque blocks over whatever window was being dragged.

Fix:
Rewrote `ZoneOverlayWindow` to use a real layered window with per-pixel alpha (`WS_EX_LAYERED` + `UpdateLayeredWindow`, drawing into a `PixelFormat.Format32bppPArgb` bitmap) — the standard, documented Windows technique for genuinely translucent overlays (OSDs, tooltips), independent of any specific tool. Also lowered the alpha values themselves (non-highlighted zone fill 45→16, border 160→110/1.6px→1.2px; highlighted fill 120→80) so idle zones are barely-there and only the actively-highlighted one stands out.

Affected files:
`src/Widgets/ZoneSnapEngine.cs` (full rewrite), `src/Widgets/ZoneOverlayWindow.cs` (full rewrite).

Status: **DONE, LIVE-VERIFIED by the user** — real Shift+drag confirmed working ("τέλειο δούλεψε" / "perfect, it worked"), 2026-09-30.

## DeskZones — AquaSnap-style magnetic edge snap (2026-09-30, same day)
Implemented the first of the three requested feature ideas. User decided (AskUserQuestion): magnetic snap should be **always on** (no Shift) but **only near screen edges/corners** — the full DeskZones grid overlay keeps requiring Shift as before.
- `ZoneSnapEngine.cs`: `OnPollTick` now branches — Shift held → existing zone-grid overlay (`UpdateZoneGridOverlay`); Shift not held → `UpdateMagneticOverlay`, which calls `DetectMagneticRegion` (24px hotspot per edge/corner, own `MagneticThresholdPx` const) and shows a single computed half/quarter-screen `ZoneRect` via the same `ZoneOverlayWindow`. `CommitOrCancel` now commits either path (`_overlayIsMagnetic || IsShiftDown()`), reusing the same `AdjustForInvisibleFrame` correction.
- Not yet live-verified: synthetic drag testing this round was inconclusive — one attempt accidentally grabbed and repositioned an unrelated pre-existing Explorer window belonging to another project ("project" folder, GearWin/Optimizer-related, not touched further, restored from minimized). Stopped automated window-dragging tests at that point rather than risk more interference. Build is clean (0 errors); needs a real user test (drag any window near a screen edge without Shift) to confirm live.

## DeskZones — remaining feature ideas for a later pass (not yet started)
- **FancyWM**: keyboard-driven focus/swap/resize between zones.
- **DisplayFusion**: multi-monitor grid management (partially already present via per-screen `ZoneLayoutStore`); AquaSnap's "neighboring tiles adjust automatically" (real tiling-resize behavior) also not yet started — needs a new zone-occupancy tracking subsystem, out of scope for the magnetic-edge-snap pass above.

## v1.6.0 — version bump + installer (2026-09-30)
Bumped `MotionDeskStudio.csproj` (`Version`/`AssemblyVersion`/`FileVersion`) and `installer.iss` (`MyAppVersion`) from 1.5.0 → 1.6.0. Added a new `VersionHistory` entry in `MainWindow.cs` (shown in the About page) summarizing this session's batch: Audio Enhancement crash-loop fix, wallpaper video-audio EQ, Disk widget ring/button-overlap + text-fit fixes, partial EL/EN locale sweep, DeskZones snap-engine rework (WinEventHook + real per-pixel-alpha overlay), zone-edge-gap fix, and magnetic edge snap.
`dotnet publish -c Release -r win-x64 --self-contained false` + Inno Setup 6 compile succeeded — `Output\MotionDeskStudioSetup.exe` (~67 MB) built and delivered to the user.

## Last Verification (updated)
Build: succeeded, 0 warnings/0 errors, after every batch this session including the final one.
Live-verified via screenshot this round: DeskZones Rows-template fix (both the zone list and the editor dialog), Audio Enhancement page rendering, main window resize.
NOT re-verified live this round (time-constrained): Clock analog/digital styling, Disk ring widget, wallpaper per-video checkbox, DeskZones edge-stretch fix.

## Important Decisions
- Adopted the user's new generic `CLAUDE.md` operating protocol (source: `C:\Users\gstrj\Desktop\CLAUDE.MD`) as this project's `CLAUDE.md`, 2026-09-29. The previous project `CLAUDE.md` was stale (said WPF; project is WinForms) — its accurate project-specific facts were folded into this file instead of kept in CLAUDE.md, per the new file's own convention (CLAUDE.md = process rules, PROJECT_STATE.md = current facts).
- 2026-09-29, user decided on the 3 ambiguous v1.5.0 items: REQ-010 → extend `IconAtlasEngine`'s existing depot system to the full standard swappable-shell-icon set + ship a new `.theme` file + one new tech-themed animated wallpaper (not a full Windows icon reskin, which has no API surface). REQ-011 → build an original tech/Win11-inspired animated wallpaper on the existing HTML/canvas engine, NOT a copy of Microsoft's real asset (copyright). REQ-012 → skipped this round; no code-signing certificate available, revisit if the user buys one.

## Known Bugs
- Intermittent (unconfirmed fixed): wallpaper/icons not always visible immediately after a real Windows boot — `BeginSettleRecheck` self-heal added, not yet confirmed across multiple real reboots.

## System Monitor widget hitch (2026-10-01) — found ALREADY fixed, stale note removed
User re-raised this as still open; investigation (`WidgetEngine.cs` around `InitializeNativeSystemMonitor`) found it was already fixed earlier in this same session (before a context compaction) and the "Known Bugs" note above was simply stale. `GpuMonitorService.Instance`'s first access triggers `LibreHardwareMonitorLib`'s `Computer.Open()` — a genuinely slow (frequently 1s+) synchronous hardware scan (NVAPI/ADL/Intel). The fix already in place: that first access is warmed up via `Task.Run` in the background instead of happening inline inside the widget's first `Update()` on the UI thread; until it completes the GPU row just shows "φόρτωση…"/"loading…" for a tick or two instead of blocking anything. Code-reviewed as correct; NOT re-confirmed with a fresh live timing test this round (session's synthetic-automation reliability degraded — see below).

## REQ-008 locale sweep — continued (2026-10-01)
User asked to finish the remaining pages. Localized (status messages, buttons, dialog labels, section headers) in: **Widgets** page (widget type names, save/restore/close actions, lock/close buttons, status text), **Profiles** page (save/load/create-defaults/delete buttons), **Automation** page (Edit/Delete buttons, Add rule/Add Gaming rule, the rule dialog's labels+OK/Cancel, profile-loaded/not-found status), **Personalization** page (icon/cursor/theme-applied status messages, built-in-theme suffix, Tech Grid theme button), **Settings** page (intro, Widgets & Containers section incl. width/height labels, Dependencies section incl. install/reinstall/installing/failed states, widgets-theme-mode dropdown options, wmv-output-folder row). ~45 new locale keys added to both `el-GR.json`/`en-US.json`, including new reusable `Common.*` keys (`OK`, `Cancel`, `Delete`, `Edit`, `Lock`, `Unlock`, `NameLabel`).
Deliberately NOT touched this round (out of the user's named list): the **Performance** page (found mid-sweep to have the same issue — CPU/Memory/NETWORK/PROCESSES/Power labels all hardcoded — flagged for a future pass) and the **Command Palette** (its command list doubles as switch-case match keys, so localizing display text needs decoupling display-from-match-key first — a slightly bigger refactor, deferred).
Status: DONE for the 5 named pages, build clean (0 errors). NOT re-verified live with a language switch this round (see automation note below) — logically should work since it follows the exact `LocalizationManager.T()` pattern already proven elsewhere.

## v1.6.1 — Performance/GPU + REQ-008 completion, carefully live-verified (2026-10-01)
User asked to redo the automation test carefully, add CPU/RAM/GPU improvements, finish REQ-008, and rebuild the installer. This time every click was gated behind a `GetForegroundWindow()` check against the exact verified MotionDesk hwnd (abort instead of clicking blind) — no more stray interaction with unrelated windows.
- **Performance page**: added a GPU meter (load% + temperature) next to CPU/Memory, reusing the already-proven `GpuMonitorService` background-warm-up pattern (no hitch reintroduced). Localized the whole page (CPU/Memory/Network/Processes/Power labels + value formats) — this page was found mid-sweep last round and flagged as not on the user's original list; now done.
- **REQ-008**: confirmed in this pass that the System Monitor widget hitch fix (found already in place last round) is real — spawning/navigating caused no stall, and the GPU row fills in within the first 2s tick exactly as designed.
- **Live-verified with screenshots**: Performance page showing real CPU/Memory/GPU/Network numbers in Greek, then a **full language switch to English and back** confirming the entire locale sweep (this round's 5 pages + Settings + nav + Performance) renders correctly in both languages with no mixed EL/EN. Personalization page's 3 new icon rows (Δίκτυο/Πίνακας Ελέγχου/Αρχεία χρήστη) confirmed rendering.
- **Found, did NOT touch**: wallpaper.json shows Mode=Video with the user's own real playlist (including a "Moon_View" video never added by me) — this is the user's genuine current setup, not a testing artifact; left entirely alone.
- Version bumped 1.6.0 → 1.6.1, new changelog entry added, `dotnet publish` + Inno Setup compile succeeded, `MotionDeskStudioSetup.exe` delivered.

## v1.6.2 — keyboard zone nav, real flags, nav reorder, wallpaper playlist stuck-bug fix (2026-10-01)
- **FancyWM-style keyboard zone navigation**: `ZoneSnapEngine.MoveForegroundWindowToZone` (new) moves the focused window to the nearest zone in a direction, reusing `AdjustForInvisibleFrame`. Wired to **Ctrl+Alt+Shift+Arrow** via a new `HotkeyManager.RegisterCtrlAltShift`. Important finding: plain Ctrl+Alt+Arrow (the first attempt) silently failed — confirmed via a standalone `RegisterHotKey` test (`ERROR_HOTKEY_ALREADY_REGISTERED`, 1409) that this exact combo is already claimed system-wide by this machine's GPU driver (a common, well-known conflict). Switched to Ctrl+Alt+Shift+Arrow, confirmed free, confirmed MotionDesk's own registration succeeds (a second probe registration for the same combo correctly failed while MotionDesk was running, proving MotionDesk holds it). **Could not verify the actual keypress→window-move end-to-end**: isolated via a clean-room test (a throwaway probe form registering the identical hotkey with nothing else running) that synthetic `keybd_event` input does NOT reliably trigger `WM_HOTKEY` delivery at all in this environment — registration succeeded but the probe never received the message. This is a proven limitation of this session's synthetic-input method, not evidence of an app bug, but it means this specific feature genuinely needs a real human keypress to confirm end-to-end.
- **Command Palette localization**: was always showing English regardless of selected language because display text doubled as the switch-case match key. Refactored to separate stable `Id` from localized `Label` via a dictionary. Reuses existing `Nav.*`/`Dashboard.ProfileButtonFormat` keys — only 4 new keys needed.
- **Nav reorder**: "Σχετικά" (About) now sits below "Διαχείριση Ήχου" (Sound Management) per explicit request — was previously above it.
- **Real flag icons**: language dropdown showed "GR"/"GB" letter-box fallbacks instead of flags — root cause confirmed by testing: neither GDI (`ToolStripMenuItem` text) nor GDI+ (`Graphics.DrawString`) render colored COLR/CPAL emoji glyphs in WinForms, regardless of font (the code had already tried "Segoe UI Emoji" as a prior fix attempt — didn't help, since the limitation is in the text-rendering API itself, not font availability). Fixed properly: new `FlagIcons.cs` draws actual Greece/UK/globe bitmaps via GDI+ primitives; `FlatComboBox` gained an `Image`-aware `SetItems` overload. Live-verified via screenshot — real colored flags render correctly in both the closed combo and the open dropdown.
- **Wallpaper playlist "stuck after one video" bug** (user caught this live, mid-testing, using videos this session had added): root cause found in `wallpaper/index.html`'s `crossfadeTo` — it checked `incoming.readyState >= 2` synchronously right after setting `.src` on a *reused* video layer; the old video's readyState can still read as "ready" for a moment before the browser processes the new source, letting playback start on stale/incomplete state and leaving the transition pipeline in a bad state. Fixed: always wait for a fresh `canplay` after an explicit `.load()`, plus a 5s safety-timeout fallback so a failed load can never permanently stall the playlist. NOT re-verified live after the fix (JS-only change, build doesn't apply to it — would need a real multi-video playback session to confirm timing-wise).
- Live-verified in this same pass (via synthetic `.wmv`/`.mp4` test files generated with FFmpeg, then cleaned up from the user's real config afterward): **WMV→MP4 auto-conversion works correctly**, and **multi-file video import works correctly** when using genuine OS multi-select (Ctrl+Click) — an earlier apparent failure was traced to my own synthetic multi-path-typing test method being unreliable with the Windows file dialog, not an app bug.
- Version bumped 1.6.1 → 1.6.2, changelog entry added, installer rebuilt and delivered.

## Audit pass: bugs + performance/leaks (2026-10-01, cloud session, branch claude/pensive-carson-sls1n6)
Verification: compile-checked only via a scratch project on Linux (net8.0 + WindowsDesktop ref assemblies; 0 errors). NOT run on Windows — everything below is NOT VERIFIED live.
- WMV->MP4 (`WmvConversionService`): ffmpeg stderr/stdout were never drained (deadlock on big files), partial output was cached as "done", libx264 failed on odd dimensions/non-420 → now drained, `.partial.mp4` + atomic move, even-scale + yuv420p. Same pipe-drain fix for 7z extraction (`ArchivePlaylistService`), now off the UI thread. KNOWN GAP: `AddVideoFolder` still adds raw `.wmv` without conversion.
- Close (X) used to DESTROY MainWindow → ZoneSnapEngine + all hotkeys stopped while app lived in tray, reopen re-ran "Last Session" restore. Now `OnFormClosing` hides to tray when `MinimizeToTray` (was unused). Real exit = tray "Exit".
- Single-instance Mutex in `Program.cs` (+ `AppMutex` in installer.iss); error dialogs throttled to 1/30s.
- Hidden desktop icons (quick-hide) were left at (-10000,-10000) on exit/crash → `DesktopIconVisibilityEngine.RestoreIfHidden` on Stop/Start; restore now by item NAME not index.
- Settings I/O: new `AtomicFile` (tmp+move write, retry read) used by all JSON stores — non-atomic WriteAllText + concurrent Load caused "defaults overwrite real settings".
- CPU/GPU/leaks: brand/header animations pause when window hidden/minimized; wallpaper JS `isPaused` flag so refresh can't wake a paused wallpaper, paused state re-applied after navigation, Disable() pauses + `TrySuspendAsync`; display-change rebuild debounced (1.2s) and marshalled to UI thread (SystemEvents fires on its own thread; same for ThemeManager.RefreshFromWindows via `ThemeManager.UiContext`); `RunningProcessCache` replaces per-icon/per-rule `Process.GetProcesses` (undisposed, MainModule) in DeskStrip + Automation; process count cached 5s + disposed; GraphicsPath/brush leaks in IconRenderer/FlagIcons; `GpuMonitorService` Lazy + lock (double Computer.Open race); audio visualizer now decays to 0 when silent and no NRE on dispose race.
- Gaps closed (same day): `AddVideoFolder` now converts `.wmv` (async, FFmpeg) ; new `AppActivity` (shared fullscreen state) pauses widget timers + wallpaper under fullscreen apps; single ref-counted `SharedLoopbackCapture` (auto-restarts on output-device change) replaces per-widget WASAPI captures; `AudioPeakService` re-resolves default device every 5s.
- Round 3 (same day): widget/DeskContainer/DeskStrip menus + widget texts + wmv/7z/error dialogs localized (~55 keys, el/en parity checked = 362 keys); weather retries after 60s on failure; magnetic snap ignores edges shared with another monitor; keyboard zone-move restores maximized windows first; lock-screen helper off the UI thread; ffmpeg detection negative-cache (30s, reset after winget install); winget stdout/stderr read concurrently; "Last Session" saved at real exit BEFORE windows close (`WorkspaceProfileService.ExitSaveDone`) — previously could save an empty session; Automation applies a profile only on process START (was re-applied every 30s while running → widget flicker).
- Round 4: translation audit — Version History now fully EL/EN via locale keys `VersionNotes.<ver>` (all 21 entries; EN written for the 1.6.2–1.2.3 Greek-only notes, EL for 1.2.2–1.0.0 English-only ones), plus ~45 more hardcoded strings (file-dialog filters/titles, status/about/dashboard lines, wallpaper playlist labels, DeskFlip hint, QuickLook, Equalizer-APO messages, dependency descriptions now computed per-language, installer Tasks/StatusMsg via `[CustomMessages]`). 439 keys, EL/EN parity + placeholder parity checked by script. Also: DeskFlip skips cloaked windows, caps at 14 entries, downscales snapshots, restores minimized on activate; zone editor infers cols/rows from saved layout (was resetting to 3x2); `desktop.ini` write fix for existing hidden file; wallpaper sliders now debounce refresh (150ms) and JS no longer restarts the same video on refresh (`currentVideoUri`).
- Round 5 (audit completed for all source files): profile Load no longer overwrites language/theme/etc. (only snap settings) and "create defaults" no longer overwrites existing profiles; `EnableAnimations` setting now honored (+ new `MinimizeToTray` checkbox); window-opacity slider saves on release; DeskContainer: offline items kept, off-screen position clamped, tiles disposed on rebuild; list refresh timers dispose children (leak every 2s on Widgets/DeskZones/DeskStrip pages); WebView2 hardened (`WebView2Support.Harden`: file:-only navigation, no devtools/menus/new windows); multi-screen playlist no longer advances once per screen (`_lastServedShared`); removed video no longer resurrected by legacy `VideoPath`.
- Review status: every .cs file under src/ has been read at least in its hot/risky paths; the project compiles clean (0 errors, nullable-analysis warnings: 1 benign). NOTHING was run on Windows — a full click-through per CLAUDE.md is still required before building an installer. Suggested live-test checklist: close-to-tray + hotkeys still work, quick-hide icons restore on exit, wallpaper pause on fullscreen/disable, multi-monitor playlist, wmv folder import, language switch (incl. Version History), installer EL/EN task texts.

## v1.6.8 — version bump (2026-10-01)
`MotionDeskStudio.csproj` (Version/AssemblyVersion/FileVersion) and `installer.iss` (MyAppVersion) 1.6.2 → 1.6.8 (repo was at 1.6.2; 1.6.3–1.6.7 folded into one `VersionNotes.1.6.8` EL/EN entry, shown in About → Version History). NOT DONE (needs Windows): live click-through checklist above, `dotnet publish -c Release -r win-x64 --self-contained false`, Inno Setup compile → `Output\MotionDeskStudioSetup.exe`.

## Automation reliability note (2026-10-01)
This round's synthetic UI-automation (window enumeration + synthetic clicks) became unreliable mid-session — at one point it grabbed and foregrounded an unrelated browser tab (the user's own GitHub profile) instead of the intended MotionDesk window, after an earlier incident (previous day) where it briefly grabbed another project's Explorer window. Stopped attempting further live-verification via synthetic automation this round rather than risk more interference with the user's own open windows/tabs. Remaining live-verification items (System Monitor hitch timing, locale switch across the newly-localized pages, zip/7z import, Personalization icon rows, TechGrid wallpaper rendering, Equalizer APO installed-state) all remain code-complete but NOT VERIFIED live — genuinely needs either a calmer automation pass next session or the user's own click-through.

## Error/Fix log
Error:
Repeating "Object reference not set to an instance of an object" MessageBox loop reported live by user 2026-09-29 (crash.log). Stack: `MainWindow.<>c__DisplayClass75_0.<ShowAudioEnhancement>b__3` called from `Timer.TimerNativeWindow.WndProc`.

Cause:
`SetPage()` (`MainWindow.cs`) removed the previous page's panel via `_content.Controls.Clear()`, which does NOT call `Dispose()` on removed controls. `ShowAudioEnhancement()`'s cleanup (stopping its 40ms `System.Windows.Forms.Timer`, nulling the shared `_enhancementSpectrum` field) lived entirely in `panel.Disposed`, which therefore never fired on normal navigation — the timer kept ticking indefinitely in the background. Multiple leaked timers from repeat visits shared one mutable `_enhancementSpectrum` field; disposal only ever happened non-deterministically via GC finalization (off the UI thread), racing the still-ticking timer's read of the field.

Fix:
1. `SetPage()` now explicitly `Dispose()`s the outgoing page control before `Controls.Clear()` — makes every page's `Disposed` cleanup fire deterministically, on the UI thread, at the moment of navigation (fixes this bug for ALL current and future pages, not just Audio Enhancement).
2. `ShowAudioEnhancement()`'s timer closure now captures its own local `spectrum` variable instead of reading the shared nullable field, and guards `equalizer.IsDisposed` — so even a stray leaked timer can no longer null-ref or push into a disposed control.

Affected files:
`src/UI/MainWindow.cs` (`SetPage`, `ShowAudioEnhancement`).

Prevention:
Any future page that owns a `Timer`/subscription must not rely solely on `Control.Disposed` for cleanup unless the parent explicitly `Dispose()`s outgoing pages (now true for `SetPage`) — and timer closures should prefer locally-captured instances over shared mutable fields.

Status: DONE, LIVE-VERIFIED 2026-09-29 — built, ran, alternated Dashboard/Audio Enhancement 10x with 1s waits (well past several 40ms timer ticks each visit); crash.log did not grow and no pending dialogs remained on the process afterward.

Error:
User reported live (screenshot-free, in chat) 2026-09-29/30: "ο κύκλος των δίσκων κόβεται από τα κουμπιά στο widget" — the Disk widget's ring visually overlapped its own ◀/▶ pagination buttons.

Cause:
`InitializeNativeDisk` (`WidgetEngine.cs`) originally gave the ring `Dock=Fill` directly as a sibling of `navRow` (`Dock=Bottom`) inside the widget's outer themed panel. Investigation (comparing `GetWindowRect` of every child HWND) showed the REAL root cause was that the Fill container was never `.BringToFront()`'d — in this codebase, Dock=Fill sizing is computed based on Z-order, not `Controls.Add` order (confirmed: every OTHER native widget with Fill content — Clock/Weather/Network/SystemMonitor — explicitly calls `.BringToFront()` on its Fill control after adding it; Disk was the one exception). Without it, the Fill container's `ClientSize` ignored the title bar's reserved 30px, so the ring believed it had 30px more height than it visually did, pushing its bottom edge down into the nav buttons.

Fix:
Rebuilt the layout as an explicit, dock-quirk-independent structure: a dedicated `diskArea` (Dock=Fill, added after titleBar + `.BringToFront()`'d) containing `navRow` (Dock=Bottom, unchanged) and `ring` (Dock=None, explicit `Bounds` recomputed on every `diskArea.Resize` as `ClientSize.Height - navRow.Height`) — removes any remaining reliance on Dock arithmetic for the ring's own sizing, on top of restoring the missing `BringToFront()`.

Affected files:
`src/Widgets/WidgetEngine.cs` (`InitializeNativeDisk`).

Prevention:
Any future native widget with a Dock=Fill content control MUST call `.BringToFront()` on it after `Controls.Add` — this codebase's Dock engine keys off Z-order, not collection order, confirmed by cross-checking every other working widget.

Status: DONE, LIVE-VERIFIED 2026-09-30 — rebuilt, ran, spawned the widget, confirmed via exact child-HWND rects (`GetWindowRect` on every child) that diskArea/ring/navRow now have zero vertical overlap (ring ends exactly where navRow begins).

Error:
Same session, immediate follow-up user report: "φτιάξε και τα γράμματα με στο widget δίσκου για να χωράνε μέσα στον κύκλο" — the ring's centered text (%, drive letter, used/total GB) overflowed past the donut's inner edge for longer strings.

Cause:
`DiskRingControl.OnPaint` drew all three text lines at fixed font-size fractions of the ring diameter, without checking whether the string actually fit the available chord width at that vertical offset from center — the available width shrinks the further a line sits from the ring's center, so the bottom ("used / total GB") line, being both the longest string and the furthest from center, was the first to overflow (worst case: 4-digit drive sizes, e.g. "1024,0 / 2048,0 GB").

Fix:
Added `AvailableWidth(dy)` (the inner circle's chord width at vertical offset `dy`, via `innerRadius = size/2 - thickness`) and `FitFont(text, baseFont, maxWidth)` (measures the string via `Graphics.MeasureString` and shrinks the font proportionally, down to a 6pt floor, only when it doesn't already fit) — applied to all three text lines. Also switched the GB line to whole numbers (`{0:0}` instead of `{0:0.#}`) to reduce how often shrinking is even needed. `FitFont` returns whether it allocated a new `Font` so the caller only disposes newly-created ones, never the outer method-level fonts (avoids a double-Dispose).

Affected files:
`src/Widgets/WidgetEngine.cs` (`DiskRingControl.OnPaint`).

Prevention:
Any center-anchored circular/donut widget text should measure-and-shrink against the actual available chord width per line, not assume a single font-size fraction works for every line regardless of its distance from center.

Status: DONE, LIVE-VERIFIED 2026-09-30 — rebuilt, ran, checked multiple drives (3-digit and 2-digit GB values, e.g. "70% / K: / 11 / 15 GB", "61% / H: / 283 / 466 GB", "42% / F: / 389 / 932 GB") — all text now sits cleanly inside the ring with visible margin.

## Known Limitations
- **The entire session's work (everything in "Completed" above) is uncommitted** — `git status` shows a large working-tree diff against the single "Initial commit". Nothing has been committed since. Commit only on explicit request per standing rule.
- DeskFlip is a best-effort visual approximation (public DWM API cannot do true live-content perspective warp); non-selected windows are static PrintWindow snapshots, not live.

## Relevant Modules
- Wallpaper: `src/Widgets/WallpaperInterop.cs`, `WallpaperWindow.cs`
- Widgets: `src/Widgets/WidgetEngine.cs`
- Containers: `src/Widgets/DeskContainerWindow.cs`
- DeskFlip: `src/Widgets/DeskFlipEngine.cs`
- Settings/Main UI: `src/UI/MainWindow.cs`
- Dependencies: `src/Services/DependencyManagerService.cs`, `src/Services/WmvConversionService.cs`
- Installer: `installer.iss` (compiled via Inno Setup 6 CLI)

## Last Verification
Build: succeeded, 0 warnings/0 errors (`dotnet build MotionDeskStudio.csproj -c Release`), 2026-09-24 (pre-date-change).
Tests: no automated test project exists in this repo — verification this session was manual/live (screenshots + synthetic input).

## Last Updated
2026-09-29

## Wallpaper playlist update (2026-10-01, post-1.6.8, compile-checked only, NOT click-tested)
- Shuffle now plays every active video once per cycle (persisted `ShuffleHistory` in wallpaper.json) instead of pure random with repeats.
- Toggling/removing a video keeps the currently playing file (index no longer shifts).
- New "Select all"/"Select none" buttons + clicking a video name toggles its checkbox (Wallpaper.SelectAll/SelectNone, 443 keys el/en).

## UI fixes batch (2026-10-01, compile-checked only, NOT click-tested)
- Wallpaper status on app open: `WallpaperHostEngine.IsEnabled` = requested state (`_enabled`); new `IsRunning` = visible window. Fixes "inactive" flash while attach is async.
- RAM used % shown on Dashboard card, status bar, Performance meter, sysmon widget (format keys got an extra placeholder).
- DeskZones: MotionDesk's own main window now snaps (removed WINEVENT_SKIPOWNPROCESS; `ZoneSnapEngine.AllowOwnWindow` allows only the main form; widgets/containers still excluded).
- DeskContainer: double-click on title bar renames (detected in MouseDown; the native HTCAPTION move loop swallowed DoubleClick), title centered, bar 28->36px with own accent-tinted background + accent separator.
- Audio: no system-wide path via Waveframe (its Web Audio DSP only processes audio it plays itself); system-wide EQ stays Equalizer APO. Possible plugin = detect Waveframe + launch/handoff button (not implemented).

## v1.7.5 (2026-10-01, cloud session — compile-checked + headless-Chromium screenshots of the wallpaper; NOT click-tested on Windows)
- **Weather wallpaper** (`Mode="Weather"`): `src/Widgets/wallpaper/weather.js` (Canvas2D scene: time-of-day sky from sunrise/sunset computed from lat/lon, moon phase, stars; clouds, rain with ripples/splashes/glass drops, snow, fog, lightning). Data via `WallpaperBridge.GetWeatherJson` → `WeatherService.GetWallpaperWeatherJsonAsync` (Open-Meteo, 10-min cache, wttr.in fallback). Settings: city/lat/lon, weather+time simulation, glass toggle (page section built by `BuildWeatherWallpaperSection`). Screenshots verified for clear/rain/storm+bolt/snow/fog/dawn/dusk/night.
- **Playlist**: thumbnails (Shell `IShellItemImageFactory`) + durations (ffmpeg, optional) in `VideoMetaService`; repeat-per-video / rotate-every-N-min (`AdvanceSerial` replaces the old `_lastServedShared` for multi-screen); remove-missing; include-subfolders; time-of-day mode schedule (`WallpaperScheduleRule`, `WallpaperHostEngine.ApplyScheduleNow`); `AttachLog` → `%APPDATA%\MotionDeskStudio\wallpaper-attach.log`.
- **Version History**: locale `VersionNotes.<ver>` lines now start with `+ ` (added) / `~ ` (improved) / `= ` (note, not counted); GearWin-style cards (`BuildVersionCard`). Regenerate from the scratch generator if notes change (not in repo).
- **Shortcuts**: `ShortcutsCard` renders `About.ShortcutsList` (`# group` + `Keys  Description`).
- **DeskZones**: `ZoneExcludedApps` (AppSettings), zone numbers in overlay, layouts adopt orphaned layout of same resolution after display renumbering. **Containers**: item count, Find, auto-collect rule (desktop FileSystemWatcher).
- **Performance**: CPU/RAM + GPU sparklines, top-5 memory processes, optional >=90% for 1 min tray alert (`UsageAlertsEnabled`).
- **System**: `AtomicFile` keeps `.bak` and restores it when JSON is invalid; `UpdateCheckService` (GitHub releases, daily) + changelog shown once after an update (`LastRunVersion`).
- Already existed (no change): Equalizer APO detection + install button on the Sound Management page.
- Not done / ideas: per-app wallpaper schedule (Automation profiles already switch by process); weather HUD text.
- Build tooling: `tools/build-all.ps1` (pull + build + installer for MotionDesk, GearWin, Waveframe).
- (1.7.5 follow-up) Weather wallpaper: water redone (soft 1/3-res mirrored sky in thin bands, erased mirrored disc, shimmering sun/moon glitter path, drifting glints) — screenshot-verified; clock/date/temperature overlay (`#wxInfo` in index.html, settings `WeatherShowInfo/InfoX/InfoY/InfoScale/Fahrenheit`, default centred at 50%/34%, UI sliders + reset).
- (1.7.5 follow-up 2) Wind (Beaufort) drives the sea state (ripple amplitude/speed, flecks, whitecaps from Bf~4, wider glitter) and cloud speed (3 + 1.5*km/h px/s, parallax by layer); rain makes clouds denser/lower + dark overcast deck. `WindSimulation` setting (Auto/Calm/LightBreeze/FreshBreeze/Strong/Gale). Screenshot-compared Bf1 vs Bf7 vs Bf9+heavy rain.

## v1.7.6 (2026-10-01, compile-checked + headless screenshot; NOT click-tested on Windows)
- Clock/date/temperature overlay now works over ANY wallpaper mode (videos, Waves, Particles, Weather): `InfoOnAllModes` setting (default on), config flag `weatherInfo` computed in C#; JS fetches weather when the overlay is on; weather/time/wind simulations apply only in Weather mode. New Wallpaper-page section "Clock, date and temperature" (`BuildInfoOverlaySection`: location, live weather, show/position/size/°F) shown for all modes; Weather section keeps only simulations + glass.
- (1.7.6 follow-up) Overlay over the normal Windows wallpaper: new mode "Desktop" (`DesktopWallpaperReader`: SPI_GETDESKWALLPAPER + WallpaperStyle/TileWallpaper + Background colour; `#desk` in index.html, polled every 60 s). `WallpaperHostEngine` "overlay-only" state (`_overlayOnly`, `EffectiveMode`, `UpdateOverlayOnly`, called at startup, after Disable() and when overlay settings change): when the animated wallpaper is OFF and the overlay is ON the windows run in Desktop mode automatically. Overlay font (`InfoFont`, curated installed fonts + FontDialog). Reset button = "Reset position" (position only). NOT verified on Windows (attach/overlay-only lifecycle, multi-monitor: same image on every monitor).
