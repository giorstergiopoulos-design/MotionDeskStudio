using System;
using System.Drawing;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    // Ο κλασικός "trick" (Progman -> 0x052C -> WorkerW) για να τοποθετηθεί ένα παράθυρο
    // ακριβώς πίσω από τα εικονίδια της επιφάνειας εργασίας (DreamScene-style wallpapers).
    public static class WallpaperInterop
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_STYLE = -16;
        private const long WS_CHILD = 0x40000000L;
        private const long WS_POPUP = unchecked((long)0x80000000);
        private const long WS_CAPTION = 0x00C00000L;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        // Πραγματικό, ενεργό HWND στο οποίο είμαστε αυτή τη στιγμή reparented — χρησιμεύει στο
        // watchdog για να ελέγχει αν η Explorer/Progman το κατέστρεψε στο μεταξύ (βλ. IsWorkerWAlive).
        public static IntPtr LastAttachedWorkerW { get; private set; } = IntPtr.Zero;

        // True όσο το τελευταίο WorkerW στο οποίο κάναμε attach εξακολουθεί να υπάρχει.
        // Καλείται περιοδικά (βλ. WallpaperWindow.EnsureFullscreenWatcher) ώστε να ανιχνεύσουμε
        // ΑΜΕΣΑ το σκηνικό "Explorer.exe επανεκκινήθηκε -> το παλιό WorkerW καταστράφηκε μαζί με
        // το παράθυρό μας που ήταν παιδί του" (βλ. Lively issue #2407 "WorkerW destroyed") χωρίς
        // να περιμένουμε το ασθενέστερο TaskbarCreated broadcast σήμα.
        public static bool IsWorkerWAlive() => LastAttachedWorkerW != IntPtr.Zero && IsWindow(LastAttachedWorkerW);

        // Επιστρέφει το WorkerW που φιλοξενεί τα εικονίδια (SHELLDLL_DefView) -> χρησιμεύει
        // ώστε να ΜΗΝ επιλέξουμε ποτέ αυτό ως στόχο (θα έκρυβε τα εικονίδια αν μπει κάτι πάνω/πίσω
        // του λάθος), αλλά και για να επιβεβαιώσουμε ότι το "αδερφό" WorkerW πράγματι βρέθηκε.
        private static IntPtr FindIconsWorkerW()
        {
            IntPtr progman = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;

            IntPtr shellViewParent = IntPtr.Zero;
            IntPtr directShellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (directShellView != IntPtr.Zero) return progman;

            EnumWindows((hwnd, _) =>
            {
                if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    shellViewParent = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return shellViewParent;
        }

        // Βρίσκει το "κενό" WorkerW-αδερφό του παραπάνω (αυτό ΔΕΝ περιέχει SHELLDLL_DefView) —
        // εκεί μπαίνει ασφαλώς το wallpaper μας, πίσω από τα εικονίδια αλλά μπροστά από το φόντο.
        public static IntPtr FindDesktopWorkerW()
        {
            IntPtr progman = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;

            // Ζητάει από το Progman να δημιουργήσει (αν δεν υπάρχει ήδη) ένα WorkerW πίσω από τα εικονίδια.
            // Σε ΜΕΡΙΚΑ builds των Windows το αδερφό WorkerW δεν υπάρχει ακόμη τη στιγμή που
            // επιστρέφει το SendMessageTimeout -> χρειάζεται μικρό retry loop (γνωστό ζήτημα σε
            // όλες τις υλοποιήσεις αυτού του "trick").
            for (int attempt = 0; attempt < 8; attempt++)
            {
                // SMTO_ABORTIFHUNG (0x0002): μην περιμένεις όλο το timeout αν ο Explorer έχει
                // κολλήσει — γύρνα αμέσως αντί να μπλοκάρεις έως 1000ms άδικα ανά προσπάθεια.
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out _);

                IntPtr iconsHost = FindIconsWorkerW();
                if (iconsHost != IntPtr.Zero && iconsHost != progman)
                {
                    IntPtr sibling = FindWindowEx(IntPtr.Zero, iconsHost, "WorkerW", null);
                    if (sibling != IntPtr.Zero) return sibling;
                }

                Thread.Sleep(60);
            }

            return progman;
        }

        // Επισυνάπτει το παράθυρο πίσω από τα εικονίδια. Επαληθεύει ότι το SetParent όντως
        // "έπιασε" (μπορεί να αποτύχει σιωπηλά ανάμεσα σε processes) και ξαναδοκιμάζει με τον
        // αμέσως προηγούμενο διαθέσιμο στόχο (Progman) αν το αρχικό WorkerW δεν λειτούργησε,
        // αντί να αφήσει το wallpaper να μείνει ως κανονικό top-level παράθυρο που καλύπτει
        // ολόκληρη την οθόνη ΠΑΝΩ από τα εικονίδια (αυτό ήταν το bug: "δεν εμφανίζονται καθόλου
        // τα εικονίδια" όταν το SetParent απέτυχε σιωπηλά).
        //
        // targetBounds: οι ΑΠΟΛΥΤΕΣ συντεταγμένες οθόνης (Screen.Bounds) όπου πρέπει να
        // εμφανίζεται αυτό το παράθυρο. ΚΡΙΣΙΜΟ bug-fix: το SetParent ΔΕΝ μεταφράζει αυτόματα
        // τις συντεταγμένες του παιδιού σε "σχετικές με τον νέο parent" — μετά το SetParent, το
        // (X,Y) του παραθύρου ερμηνεύεται ως offset μέσα στο WorkerW, όχι ως απόλυτη θέση οθόνης.
        // Σε setups με δευτερεύουσα οθόνη αριστερά/πάνω από την κύρια (WorkerW top-left != (0,0)
        // στις συντεταγμένες virtual-screen), αυτό μεταφράζεται σε λάθος/εκτός-οθόνης τοποθέτηση
        // — ακριβώς το γνωστό bug "wallpaper εμφανίζεται μόνο στην κύρια οθόνη" που αναφέρεται
        // και στο Lively Wallpaper (GitHub issue #2438). Διόρθωση: μετά το SetParent, υπολογίζουμε
        // ρητά τη θέση σχετικά με το πραγματικό screen-rect του WorkerW και κάνουμε MoveWindow.
        // Τεχνική δανεισμένη από το Lively Wallpaper (WindowOperations.SetParentSafe): ένα
        // WinForms Form δημιουργείται πάντα με native style WS_POPUP (ακόμη και με
        // FormBorderStyle.None). Το SetParent αλλάζει ΜΟΝΟ τον λογικό parent (GetParent το
        // επιβεβαιώνει), αλλά ΔΕΝ μετατρέπει αυτόματα το WS_POPUP σε WS_CHILD — σε αρκετά
        // builds των Windows 10/11 ένα WS_POPUP παράθυρο μέσα σε WorkerW δεν clip-άρεται ούτε
        // συμμετέχει σωστά στο z-order/compositing του parent του, με αποτέλεσμα να παραμένει
        // αόρατο ή να αναβοσβήνει ακόμη κι όταν το SetParent "πέτυχε" τυπικά. Αυτό εξηγεί γιατί
        // το wallpaper εξακολουθούσε να μην εμφανίζεται σωστά παρόλο που το attach dance
        // (WorkerW discovery, retry loop, Explorer-restart watchdog) ήταν ήδη σωστό.
        private static void MakeChildStyle(IntPtr windowHandle)
        {
            long style = GetWindowLongPtr(windowHandle, GWL_STYLE).ToInt64();
            style = (style & ~WS_POPUP & ~WS_CAPTION) | WS_CHILD;
            SetWindowLongPtr(windowHandle, GWL_STYLE, new IntPtr(style));
            // SWP_FRAMECHANGED: αναγκάζει τα Windows να ξαναϋπολογίσουν το non-client frame μετά
            // την αλλαγή style· χωρίς αυτό η αλλαγή style μπορεί να μην εφαρμοστεί οπτικά.
            SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        public static bool AttachToDesktop(IntPtr windowHandle, Rectangle targetBounds)
        {
            IntPtr target = FindDesktopWorkerW();
            if (target == IntPtr.Zero) return false;

            MakeChildStyle(windowHandle);
            SetParent(windowHandle, target);
            if (GetParent(windowHandle) == target)
            {
                RepositionRelativeToParent(windowHandle, target, targetBounds);
                LastAttachedWorkerW = target;
                return true;
            }

            // Δεύτερη προσπάθεια: ίσως το αδερφό WorkerW άλλαξε handle στο μεταξύ· ξαναβρές το.
            Thread.Sleep(120);
            target = FindDesktopWorkerW();
            if (target == IntPtr.Zero) return false;
            MakeChildStyle(windowHandle);
            SetParent(windowHandle, target);
            if (GetParent(windowHandle) != target) return false;

            RepositionRelativeToParent(windowHandle, target, targetBounds);
            LastAttachedWorkerW = target;
            return true;
        }

        private static void RepositionRelativeToParent(IntPtr windowHandle, IntPtr parent, Rectangle targetBounds)
        {
            // Το WorkerW καλύπτει όλο το virtual desktop· το γωνιαίο του σημείο (Left,Top) σε
            // συντεταγμένες οθόνης μπορεί να είναι αρνητικό (π.χ. -1920,0) όταν υπάρχει οθόνη
            // αριστερά/πάνω από την κύρια. Το παιδί πρέπει να τοποθετηθεί σε συντεταγμένες
            // ΣΧΕΤΙΚΕΣ με αυτή τη γωνία, όχι στις απόλυτες συντεταγμένες της Screen.Bounds.
            if (!GetWindowRect(parent, out var parentRect)) return;
            int relX = targetBounds.X - parentRect.Left;
            int relY = targetBounds.Y - parentRect.Top;
            MoveWindow(windowHandle, relX, relY, targetBounds.Width, targetBounds.Height, true);
        }

        // Παλιά υπογραφή — διατηρείται για συμβατότητα, αλλά ΔΕΝ διορθώνει το πολυ-οθονικό bug
        // παραπάνω γιατί δεν ξέρει πού θέλουμε να καταλήξει το παράθυρο. Προτιμήστε πάντα την
        // υπερφόρτωση με Rectangle targetBounds.
        public static bool AttachToDesktop(IntPtr windowHandle)
        {
            IntPtr target = FindDesktopWorkerW();
            if (target == IntPtr.Zero) return false;

            MakeChildStyle(windowHandle);
            SetParent(windowHandle, target);
            if (GetParent(windowHandle) == target) { LastAttachedWorkerW = target; return true; }

            Thread.Sleep(120);
            target = FindDesktopWorkerW();
            if (target == IntPtr.Zero) return false;
            MakeChildStyle(windowHandle);
            SetParent(windowHandle, target);
            bool ok = GetParent(windowHandle) == target;
            if (ok) LastAttachedWorkerW = target;
            return ok;
        }
    }

    // Ανιχνεύει επανεκκίνηση της Explorer.exe (crash, "Restart Explorer" από Task Manager,
    // ή προβλήματα Windows theme service) μέσω του καθιερωμένου broadcast μηνύματος
    // "TaskbarCreated" — το ίδιο μήνυμα που χρησιμοποιεί κάθε tray-icon εφαρμογή για να
    // ξαναδημιουργήσει το tray icon της μετά από restart της Explorer. Όταν η Explorer
    // επανεκκινείται, το ΠΑΛΙΟ WorkerW καταστρέφεται — και μαζί του καταστρέφεται και το δικό
    // μας wallpaper window αφού ήταν child του (Windows καταστρέφει αυτόματα τα child windows
    // ενός destroyed παραθύρου). Αυτό ακριβώς είναι το bug "WorkerW destroyed" που αναφέρεται
    // στο Lively Wallpaper (GitHub issue #2407) — χωρίς αυτό το watchdog, το wallpaper απλά
    // εξαφανίζεται μόνιμα μέχρι να κάνει ο χρήστης χειροκίνητα Disable/Enable.
    public sealed class ExplorerRestartWatcher : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        private readonly uint _taskbarCreatedMsg;
        public event Action? ExplorerRestarted;

        public ExplorerRestartWatcher()
        {
            _taskbarCreatedMsg = (uint)RegisterWindowMessage("TaskbarCreated");
            var cp = new CreateParams
            {
                // HWND_MESSAGE (-3): message-only window — δεν χρειάζεται ορατό/UI handle,
                // απλά λαμβάνει broadcast μηνύματα σε όλη τη διάρκεια ζωής της εφαρμογής.
                Parent = new IntPtr(-3)
            };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if ((uint)m.Msg == _taskbarCreatedMsg)
            {
                ExplorerRestarted?.Invoke();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
