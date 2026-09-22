using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    // Πραγματική συμπεριφορά FancyZones (PowerToys), όχι απλό "άσε το οπουδήποτε μέσα σε ζώνη":
    // κρατάς Shift ΕΝΩ σέρνεις ένα κανονικό παράθυρο -> εμφανίζεται ημιδιάφανο overlay με τις
    // ζώνες της οθόνης κάτω από τον δείκτη, η ζώνη κάτω από τον δείκτη τονίζεται, και το άφημα
    // (mouse up) ΜΕΣΑ σε highlighted ζώνη ενώ κρατάς ΑΚΟΜΑ Shift την "κουμπώνει". Χωρίς Shift, το
    // σύρσιμο είναι απολύτως κανονικό — καμία παρέμβαση, κανένα overlay. Αυτό ήταν ρητό αίτημα
    // μετά από screenshots του πραγματικού FancyZones Editor· η προηγούμενη εκδοχή "κούμπωνε" σε
    // ΚΑΘΕ drop μέσα σε ορατό, μόνιμο ζώνη-παράθυρο (πιο κοντά σε Fences παρά σε FancyZones).
    public static class ZoneSnapEngine
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int GA_ROOT = 2;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int VK_SHIFT = 0x10;

        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelMouseProc? _proc;
        private static IntPtr _dragCandidate = IntPtr.Zero;
        private static bool _isDragging;
        private static Point _dragStart;
        private static ZoneOverlayWindow? _overlay;
        private static Screen? _overlayScreen;

        public static void Start()
        {
            if (_hookId != IntPtr.Zero) return;
            _proc = HookCallback;
            using var proc = Process.GetCurrentProcess();
            using var module = proc.MainModule!;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        }

        public static void Stop()
        {
            if (_hookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _proc = null;
            _overlay?.Dispose();
            _overlay = null;
        }

        private static bool IsShiftDown() => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int msg = wParam.ToInt32();
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var pt = new Point(data.pt.x, data.pt.y);

                    if (msg == WM_LBUTTONDOWN)
                    {
                        IntPtr hwnd = GetAncestor(WindowFromPoint(pt), GA_ROOT);
                        _dragCandidate = IsCandidateWindow(hwnd) ? hwnd : IntPtr.Zero;
                        _isDragging = false;
                        _dragStart = pt;
                    }
                    else if (msg == WM_MOUSEMOVE && _dragCandidate != IntPtr.Zero)
                    {
                        if (!_isDragging && (Math.Abs(pt.X - _dragStart.X) > 6 || Math.Abs(pt.Y - _dragStart.Y) > 6))
                            _isDragging = true;

                        if (_isDragging && IsShiftDown()) UpdateOverlay(pt);
                        else HideOverlay();
                    }
                    else if (msg == WM_LBUTTONUP)
                    {
                        if (_dragCandidate != IntPtr.Zero && _isDragging && IsShiftDown() && _overlay != null)
                        {
                            var zoneBounds = _overlay.GetHighlightedZoneBounds();
                            if (zoneBounds.HasValue)
                            {
                                var r = zoneBounds.Value;
                                SetWindowPos(_dragCandidate, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
                            }
                        }
                        HideOverlay();
                        _dragCandidate = IntPtr.Zero;
                        _isDragging = false;
                    }
                }
                catch { /* ένα hook callback δεν πρέπει ΠΟΤΕ να πετάξει εξαίρεση προς τα έξω */ }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static void UpdateOverlay(Point pt)
        {
            var screen = Screen.FromPoint(pt);
            var layout = ZoneLayoutStore.GetLayout(screen.DeviceName);
            if (layout.Zones.Count == 0) { HideOverlay(); return; }

            if (_overlay == null || _overlay.IsDisposed) _overlay = new ZoneOverlayWindow();
            if (_overlayScreen?.DeviceName != screen.DeviceName)
            {
                _overlay.ShowForScreen(screen, layout);
                _overlayScreen = screen;
            }
            _overlay.UpdateHighlight(_overlay.FindZoneIndexAt(pt));
        }

        private static void HideOverlay()
        {
            _overlay?.HideOverlay();
            _overlayScreen = null;
        }

        // Αγνοεί: δικά μας παράθυρα (MotionDesk), τον Explorer/desktop/taskbar, και οτιδήποτε δεν
        // είναι ένα κανονικό, ορατό, μεγιστοποιήσιμο/μετακινήσιμο παράθυρο εφαρμογής.
        private static bool IsCandidateWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;

            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "SysListView32") return false;

            long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
            if ((style & WS_CAPTION) == 0) return false;

            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;

            return true;
        }

        private const long WS_CAPTION = 0x00C00000;
        private const long WS_EX_TOOLWINDOW = 0x00000080;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, int gaFlags);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);
        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    }
}
