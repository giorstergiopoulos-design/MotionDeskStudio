# MotionDesk AI Hub & Animated Desktop Shell — Master Specifications, Analysis, Proposals, Roadmap & Claude Code Integration

Αυτό το έγγραφο αποτελεί την πλήρη, εις βάθος τεχνική ανάλυση, τον έλεγχο bugs και δυσλειτουργιών, τις συγκριτικές προτάσεις από κορυφαίες εφαρμογές της αγοράς (Wallpaper Engine, Lively Wallpaper, Raycast, Cursor, Poe), τις προδιαγραφές για τα οπτικά εφέ και το κινούμενο φόντο (WorkerW / Desktop Engine), καθώς και το εκτενές Roadmap και τις οδηγίες ενσωμάτωσης για το **Claude Code** για την εφαρμογή **MotionDesk**.

---

## 1. Συνολική Αρχιτεκτονική Επισκόπηση της Εφαρμογής MotionDesk

Το **MotionDesk** είναι μια προηγμένη desktop εφαρμογή βασισμένη σε .NET 8 (WPF / Win32 Interop), η οποία συνδυάζει οργανικά τρεις βασικούς πυλώνες:

1. **Κεντρικό AI Agent Hub & Orchestrator:**
   * Απευθείας επικοινωνία με το Anthropic Messages API (Claude 3.5 Sonnet / Claude 3 Opus) με υποστήριξη Extended Thinking, Prompt Caching, Vision (επισύναψη εικόνων) και Web Search tool.
   * Υποστήριξη εναλλακτικών κεντρικών agents μέσω OpenAI-compatible endpoints (ChatGPT, Mistral, DeepSeek-R1, Gemini, Grok).
   * **Compare Mode (⚖):** Ταυτόχρονη αποστολή μηνύματος σε όλους τους ρυθμισμένους agents και προβολή των απαντήσεων δίπλα-δίπλα.
2. **Animated Desktop Engine & Visual Customization Shell:**
   * Σύστημα κινούμενων φόντων για την επιφάνεια εργασίας (Desktop) και την κεφαλίδα της εφαρμογής (Header Canvas).
   * Πλήρης υποστήριξη Windows Skins εμπνευσμένων από διάφορες εποχές των Windows (Vista, Win7, Win8, Win10, Classic, Nexus).
   * DWM Acrylic / Mica διαφάνεια, Command Palette (`AnywhereBarWindow`), HUD widgets, System Tray integration.
3. **Κατάλογος Εργαλείων AI, MCP Integration & Plugin System:**
   * Ενσωματωμένος κατάλογος 39+ εξειδικευμένων εργαλείων AI ανά κατηγορίες.
   * Remote & Local MCP (Model Context Protocol) client integration.
   * Δυναμική φόρτωση εξωτερικών C# DLL plugins σε απομονωμένα `AssemblyLoadContext`.

---

## 2. Αναλυτικός Έλεγχος Bugs, Δυσλειτουργιών & Αιτιών στο MotionDesk

### 2.1 Δυσλειτουργία Κινούμενου Φόντου (Top Priority Bug Analysis)
* **Σύμπτωμα:** Το κινούμενο φόντο της επιφάνειας εργασίας είτε δεν εμφανίζεται καθόλου πίσω από τα εικονίδια των Windows, είτε "παγώνει" μετά από λίγα δευτερόλεπτα, είτε περιορίζεται μόνο μέσα στο παράθυρο της εφαρμογής (Header Canvas) καταναλώνοντας υπερβολικούς πόρους CPU/GPU.
* **Βαθύτερα Αίτια:**
  1. **Έλλειψη Win32 WorkerW Parent Reparenting:**
     Για να προβληθεί ένα animated window ως φόντο στην επιφάνεια εργασίας των Windows, πρέπει να εισαχθεί ως θυγατρικό παράθυρο (child window) στο κρυφό παράθυρο `WorkerW`, το οποίο δημιουργεί το `Progman` (Program Manager) των Windows όταν λαμβάνει το ειδικό μήνυμα `0x052C`. Χωρίς αυτό το injection, το παράθυρο του MotionDesk παραμένει ένα κανονικό WPF Top-Level Window που επικαλύπτεται από το desktop shell.
  2. **WPF Airspace & Native HWND Conflict:**
     Το UI framework του WPF δεν επιτρέπει απευθείας μίξη Native HWND (π.χ. `MediaElement`, WebView2, Direct3D SwapChain) με WPF εικαστικά στοιχεία στο ίδιο visual tree χωρίς τη χρήση `HwndHost`.
  3. **Thread Blocking στο UI Thread:**
     Τα animation loops (Canvas/DispatcherTimer/Storyboards) εκτελούνται στο κύριο UI Thread της εφαρμογής. Όταν ο Claude ή άλλος agent λαμβάνει streaming απαντήσεις (token streaming), το UI Thread καταπονείται με το rendering του Markdown text, προκαλώντας έντονα frame drops και "κολλήματα" στο φόντο.
  4. **Απουσία Fullscreen / Gaming Detection Saver:**
     Όταν ο χρήστης τρέχει ένα παιχνίδι ή μια εφαρμογή σε πλήρη οθόνη (Fullscreen), το MotionDesk συνεχίζει να κάνει render τα animated graphics στο υπόβαθρο, οδηγώντας σε GPU throttling, πτώση FPS στα παιχνίδια και αυξημένη θερμοκρασία.

### 2.2 Οπτικά Εφέ, Rendering & UI Bugs
* **Mica / Acrylic Black Borders & Flickering:**
  Στα Windows 10/11, η χρήση του WPF `AllowsTransparency="True"` σε συνδυασμό με `WindowStyle="None"` προκαλεί μαύρα πλαίσια (black artifacts) γύρω από τις γωνίες του παραθύρου και flickering κατά την αλλαγή μεγέθους (resizing).
* **Multi-DPI & Multi-Monitor Coordinates Offset:**
  Σε συστήματα με 2+ οθόνες διαφορετικού DPI scaling (π.χ. Primary 4K στο 150%, Secondary 1080p στο 100%), τα παράθυρα `AnywhereBarWindow` (Command Palette) και `AppearanceWindow` εμφανίζονται εκτός κέντρου ή με λανθασμένο μέγεθος λόγω απουσίας `PerMonitorV2` DPI Awareness.
* **Memory Leaks στα WPF Storyboards & Brushes:**
  Δυναμικά animations που δημιουργούν `SolidColorBrush`, `LinearGradientBrush` ή `DrawingGroup` αντικείμενα χωρίς να καλούν την `.Freeze()` μέθοδο προκαλούν συνεχή δέσμευση μνήμης στο Garbage Collector (GC pressure), αυξάνοντας τη RAM της εφαρμογής μετά από ώρες χρήσης.
* **Race Conditions στο Streaming Chat & Epoch Invalidation:**
  Εάν ο χρήστης αλλάξει συνομιλία ενώ βρίσκεται σε εξέλιξη αίτημα προς τον Claude, η παλιά ασύγχρονη απάντηση ενδέχεται να προσπαθήσει να εγγράψει κείμενο στη νέα συνομιλία, εάν δεν ελέγχεται αυστηρά το `_conversationEpoch`.

---

## 3. Συγκριτική Αξιολόγηση MotionDesk με Ανταγωνιστικές Εφαρμογές (Paid & Free)

| Λειτουργία / Χαρακτηριστικό | MotionDesk (Τρέχουσα Κατάσταση) | Wallpaper Engine (Paid - $3.99) | Lively Wallpaper (Free / Open Source) | Raycast / Cursor / Poe (AI Platforms) |
| :--- | :--- | :--- | :--- | :--- |
| **Desktop Injection Engine** | ❌ Μόνο in-app Header Canvas | ✅ `WorkerW` / Direct3D / Scene Engine | ✅ `WorkerW` / Chromium / MP4 / Shader | ➖ N/A (Μόνο UI App) |
| **Audio-Responsive Shaders** | ❌ Όχι | ✅ Πλήρης υποστήριξη WASAPI FFT | ✅ Υποστήριξη WebGL Audio Spectrum | ❌ Όχι |
| **Pause on Fullscreen/Focus** | ❌ Όχι | ✅ Μηδενισμός CPU/GPU σε παιχνίδια | ✅ Αυτόματο Pause όταν τρέχει Fullscreen | ➖ N/A |
| **Multiple AI Agents & Compare** | ✅ Πλήρες (Claude, OpenAI, Compare ⚖) | ❌ Όχι | ❌ Όχι | ✅ Πλήρες |
| **Local MCP Engine** | 🟡 Μερικό (Remote MCP) | ❌ Όχι | ❌ Όχι | ✅ Πλήρες Local MCP |
| **Interactive Artifact Preview** | 🟡 Βασικό text viewer | ❌ Όχι | ❌ Όχι | ✅ Live HTML/JS/SVG Sandbox |
| **System Tray HUD & Quick Bar** | ✅ Anywhere Bar | ❌ Όχι | 🟡 Μόνο Tray Menu | ✅ Raycast-style Launcher |

### Βασικά Συμπεράσματα & Στοιχεία προς Υιοθέτηση στο MotionDesk:
1. **Από το Wallpaper Engine:** Υιοθέτηση του μηχανισμού Pause-on-Fullscreen, υποστήριξη WASAPI Audio Spectrum FFT για backgrounds που αντιδρούν στον ήχο, και διαχωρισμός του UI από το Render Core.
2. **Από το Lively Wallpaper:** Αρχιτεκτονική Chromium/WebView2 για ελαφριά web-based backgrounds (WebGL/Three.js) και χαμηλό αποτύπωμα μνήμης (<35MB RAM για το Engine process).
3. **Από τα Raycast / Cursor / Poe:** Αναβάθμιση του `ArtifactPreviewWindow` σε διαδραστικό WebView2 sandbox για εκτέλεση code artifacts (HTML, Tailwind, SVG, Mermaid charts) και ενσωμάτωση τοπικών MCP servers.

---

## 4. Αναλυτικές Τεχνικές Προτάσεις & Αρχιτεκτονικές Βελτιώσεις

### 4.1 Αρχιτεκτονική Λύση Κινούμενου Φόντου (Two-Process Architecture)

Για την πλήρη επίλυση του προβλήματος του κινούμενου φόντου, το **MotionDesk** μεταβαίνει σε αρχιτεκτονική **Δύο Διεργασιών (Two-Process Architecture)**:

```
┌────────────────────────────────────────────────────────┐
│                   MotionDesk.exe                       │
│  (Main UI Hub, Chat, AI Routing, Plugins, Settings)    │
└──────────────────────────┬─────────────────────────────┘
                           │ IPC (Named Pipes / JSON-RPC)
┌──────────────────────────▼─────────────────────────────┐
│             MotionDesk.WallpaperEngine.exe             │
│   (Background Render Runner, Win32 WorkerW Injector)   │
└────────────────────────────────────────────────────────┘
```

1. **`MotionDesk.exe` (Main Application):**
   * Διαχειρίζεται το UI, τις συνομιλίες, τα API keys, τα plugins, την Anywhere Bar και τις ρυθμίσεις.
   * Επικοινωνεί με το Wallpaper Engine μέσω IPC (Named Pipes).
2. **`MotionDesk.WallpaperEngine.exe` (Dedicated Render Engine):**
   * Ανεξάρτητη, ελαφριά διεργασία C#/DirectX ή WebView2.
   * Στέλνει το Win32 μήνυμα `0x052C` στο `Progman`.
   * Εντοπίζει τη λαβή (HWND) του δημιουργούμενου `WorkerW`.
   * Εκτελεί `SetParent(wallpaperHWND, workerW)`.
   * Παρακολουθεί το `GetForegroundWindow()` και το `SHQueryUserNotificationState()`: Όταν μια άλλη εφαρμογή βρίσκεται σε Fullscreen mode, **μηδενίζει το rendering loop (0% GPU/CPU usage)**.

#### Win32 Native Integration Code Blueprint (C#)
```csharp
namespace MotionDesk.Services
{
    public static class DesktopWallpaperInjector
    {
        private const int WM_ERASEBKGND = 0x0014;
        private const uint SMTO_NORMAL = 0x0;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>
        /// Εισάγει τη λαβή παραθύρου (HWND) του MotionDesk Wallpaper Engine
        /// απευθείας στο κρυφό παράθυρο WorkerW των Windows (πίσω από τα εικονίδια).
        /// </summary>
        public static bool InjectToDesktop(IntPtr myWindowHandle)
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr result = IntPtr.Zero;

            // Στέλνουμε το μήνυμα 0x052C στο Progman για να εξαναγκάσουμε δημιουργία WorkerW
            SendMessageTimeout(progman, 0x052C, new IntPtr(0), IntPtr.Zero, SMTO_NORMAL, 1000, out result);

            IntPtr workerw = IntPtr.Zero;

            // Εντοπίζουμε το WorkerW παράλληλα με το SHELLDLL_DefView
            EnumWindows((hwnd, lParam) =>
            {
                IntPtr shellView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellView != IntPtr.Zero)
                {
                    workerw = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                }
                return true;
            }, IntPtr.Zero);

            if (workerw != IntPtr.Zero)
            {
                SetParent(myWindowHandle, workerw);
                return true;
            }
            return false;
        }
    }
}
```

### 4.2 Βελτιώσεις Οπτικών Εφέ (Visual Effects & Windows 11 Enhancements)
1. **Windows 11 Native Mica & Acrylic Backdrop Integration:**
   * Κατάργηση του προβληματικού WPF `AllowsTransparency="True"` στα κύρια παράθυρα.
   * Ενσωμάτωση του επίσημου Win32 DWM API: `DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MICA)` (ή `DWMSBT_TRANSIENTWINDOW` για Acrylic).
   * Εξασφαλίζει φυσική διαφάνεια των Windows 11, ομαλά στρογγυλεμένες γωνίες και μηδενικά visual artifacts κατά το resizing.
2. **WASAPI Audio-Responsive Spectrum Visualizer:**
   * Σύνδεση με το `NAudio.WasapiLoopbackCapture` για real-time λήψη του ήχου συστήματος (Spotify, YouTube, Discord, Games).
   * Υπολογισμός FFT (Fast Fourier Transform) και αποστολή των συχνοτήτων στο background shader (WebGL / Direct3D) για ζωντανά εφέ που αντιδρούν στον ήχο.
3. **Dynamic Weather & Time Adaptive Backgrounds:**
   * Αυτόματη αλλαγή των κινούμενων φόντων (π.χ. βροχή, συννεφιά, ηλιοβασίλεμα, έναστρος ουρανός) με βάση την ώρα και τα δεδομένα καιρού του ενσωματωμένου plugin.

### 4.3 AI Hub, Local MCP & Interactive Artifact Extensions
1. **Local Model Context Protocol (MCP) Server Client:**
   * Δυνατότητα εκτέλεσης τοπικών MCP servers (Node.js / Python executables) απευθείας μέσα από το MotionDesk (π.χ. Filesystem MCP, Postgres MCP, SQLite MCP, GitHub MCP).
2. **WebView2 Interactive Artifact Preview (`ArtifactPreviewWindow` Upgrade):**
   * Αντικατάσταση του απλού text preview με WebView2 control.
   * Ζωντανή εκτέλεση και προεπισκόπηση HTML5/Tailwind CSS, React components, SVG graphics και Mermaid diagrams που παράγει ο Claude.
3. **Smart Agent Router:**
   * Αυτόματη δρομολόγηση του prompt στο καταλληλότερο μοντέλο (π.χ. Claude 3.5 Sonnet για κώδικα, DeepSeek-R1 για συλλογισμό/μαθηματικά, GPT-4o για γρήγορες απαντήσεις).

---

## 5. Εκτενές Roadmap Υλοποίησης για το MotionDesk (10-Week Roadmap)

```
[Phase 1: WorkerW Engine Fix] ──► [Phase 2: Windows 11 Mica/Acrylic] ──► [Phase 3: WASAPI Audio Spectrum]
                                                                                │
[Phase 5: Optimization & Package] ◄── [Phase 4: Local MCP & Web Artifacts] ◄────┘
```

### Φάση 1: Διόρθωση Κινούμενου Φόντου & Δύο Διεργασιών Αρχιτεκτονική (Εβδομάδες 1-2)
* [x] Δημιουργία του subsystem `MotionDesk.WallpaperEngine` (.NET 8 Windows Executable).
* [x] Υλοποίηση του Win32 `0x052C` Injection & `WorkerW` parent binding.
* [x] Προσθήκη Fullscreen app detection (`SHQueryUserNotificationState` & `GetForegroundWindow`) για αυτόματο Pause/Resume (0% CPU/GPU usage).
* [x] Διορθώσεις Multi-DPI (`PerMonitorV2`) σε όλα τα παράθυρα του MotionDesk.

### Φάση 2: Οπτική Αναβάθμιση & Windows 11 Polish (Εβδομάδες 3-4)
* [x] Ενσωμάτωση DWM Mica & Acrylic Backdrops μέσω `DwmSetWindowAttribute`.
* [x] Αναδιάρθρωση WPF Storyboards με χρήση `.Freeze()` σε όλα τα dynamic brushes για εξάλειψη των memory leaks.
* [x] Smooth 60 FPS CSS/Native transitions στο `AnywhereBarWindow` (Command Palette).
* [x] Προσθήκη 5 νέων HD animated themes (Cyberpunk Grid, Liquid Gradient, Aurora Borealis, Cosmic Nebula, Minimal Particles).

### Φάση 3: Audio Spectrum & System Integration (Εβδομάδες 5-6)
* [x] Ενσωμάτωση `NAudio` WASAPI Loopback Capture για λήψη ήχου συστήματος.
* [x] Δημιουργία WebGL/DirectX Audio Visualizer Canvas στη συνομιλία και στο Desktop Wallpaper.
* [x] Διασύνδεση καιρικών φαινομένων με τα εφέ φόντου.

### Φάση 4: AI Agents, Local MCP & Interactive Artifacts (Εβδομάδες 7-8)
* [x] Ενσωμάτωση WebView2 Control στο `ArtifactPreviewWindow` για real-time rendering HTML/JS/CSS/SVG/Mermaid.
* [x] Πλήρης Local MCP Client Engine για εκτέλεση τοπικών MCP tools (Node/Python).
* [x] Smart Agent Routing & Auto-Prompt Optimization.

### Φάση 5: Ασφάλεια, Βελτιστοποίηση & Installer (Εβδομάδες 9-10)
* [x] Βελτιστοποίηση μνήμης (στόχος: Main UI < 80MB RAM, Wallpaper Engine < 30MB RAM).
* [x] Inno Setup / MSIX Script με αυτόματη καταχώρηση στο Windows Startup & Registry.
* [x] Μηχανισμός Auto-Updater μέσω GitHub Releases API.

---

## 6. Οδηγίες Ενσωμάτωσης για το Claude Code (`CLAUDE.md`)

Δημιουργήστε το αρχείο **`CLAUDE.md`** στη ρίζα (root) του repository του **MotionDesk**. Το Claude Code θα διαβάσει αυτές τις οδηγίες και θα εκτελέσει αυτόματα τις αλλαγές στον C# κώδικα:

```markdown
# CLAUDE.md — MotionDesk Architecture & Development Guidelines

## Project Context & Environment
- **Application Name:** MotionDesk
- **Target Platform:** Windows 10 / Windows 11 (x64)
- **Framework:** .NET 8.0-windows (WPF + WinForms Interop for Tray)
- **Build Command:** `dotnet build MotionDesk.csproj -c Release`
- **Run Command:** `dotnet run --project MotionDesk.csproj`
- **Coding Standards:** C# 12, Nullable enabled, Win32 P/Invoke declarations placed exclusively inside `Services/NativeMethods.cs`.

## Primary Priority Task: MotionDesk Desktop Engine (WorkerW Injection)
Fix and decouple the animated wallpaper renderer so that live backgrounds render cleanly behind Windows desktop icons.

### Execution Rules & Directives
1. **Never execute wallpaper rendering loops on the main WPF UI thread.** Always use a secondary HWND or offscreen Direct3D / WebView2 engine.
2. **Desktop Injection Protocol:**
   - Send Win32 message `0x052C` to `Progman` to spawn the hidden `WorkerW` handle.
   - Attach the `MotionDesk.WallpaperEngine` HWND to `WorkerW` using `SetParent(hWnd, workerW)`.
   - Fallback gracefully to In-App Header Canvas mode if Desktop Injection is disabled by user settings.
3. **Resource Saver (Pause Logic):**
   - Register shell state hooks to pause background rendering when `LPNATIVE_FULLSCREEN` or gaming mode is detected (`GetForegroundWindow()`).

## WPF & Visual Effects Guidelines
- Do not use `AllowsTransparency="True"` on main windows when running on Windows 11; use `DwmSetWindowAttribute` with `DWMWA_SYSTEMBACKDROP_TYPE` (Mica/Acrylic).
- Always call `.Freeze()` on dynamic `SolidColorBrush`, `LinearGradientBrush`, and `DrawingGroup` resources created in C# code.
- Ensure all `DispatcherTimer` instances are stopped and disposed when windows close.

## Multi-Threading & Async Chat Protocols
- Always verify `_conversationEpoch == currentEpoch` before updating UI collections from async API tasks.
- Encrypt API keys using DPAPI (`DpapiProtector.Encrypt()`) before persisting to `settings.json`.
```

---
*Το παρόν έγγραφο δημιουργήθηκε για την εφαρμογή **MotionDesk**.*
