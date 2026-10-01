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

        // GetParent() ΔΕΝ είναι αξιόπιστο εδώ: αν το παράθυρο (έστω στιγμιαία, ή σε build που δεν
        // εφάρμοσε αμέσως το MakeChildStyle) κουβαλάει ταυτόχρονα WS_POPUP, το GetParent γυρνάει
        // τον "owner" (thread-branch) αντί για τον πραγματικό parent — μπορεί να γυρίσει NULL ενώ
        // το SetParent ΠΕΤΥΧΕ. GetAncestor(hwnd, GA_PARENT) λέει πάντα την αλήθεια ανεξάρτητα από
        // τα style bits — χρησιμοποιείται τώρα για την επαλήθευση αντί για GetParent.
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        private const uint GA_PARENT = 1;

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

        // Απαραίτητο ΜΟΝΟ στη "raised desktop" διάταξη (βλ. AttachRaisedDesktop) — επιβεβαιωμένο
        // από το πραγματικό, δουλεμένο source της Lively Wallpaper (WindowUtil.SetWindowTransparency):
        // στη raised desktop διάταξη το SHELLDLL_DefView είναι το ίδιο WS_EX_LAYERED child του
        // Progman· ένα ΜΗ-layered παράθυρο τοποθετημένο στο ίδιο σημείο του z-order δεν
        // compositάρεται σωστά από το DWM (μένει αόρατο/μαύρο) — χρειάζεται ΚΑΙ το δικό μας
        // παράθυρο να γίνει WS_EX_LAYERED, ΠΡΙΝ το SetParent.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        private const int GWL_STYLE = -16;
        private const long WS_CHILD = 0x40000000L;
        private const long WS_POPUP = unchecked((long)0x80000000);
        private const long WS_EX_LAYERED = 0x00080000L;
        private const uint LWA_ALPHA = 0x2;
        private static readonly IntPtr HWND_BOTTOM = new(1);
        private const uint SWP_NOACTIVATE = 0x0010;
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

        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_NOREDIRECTIONBITMAP = 0x00200000L;

        // Windows 11 24H2+ "raised desktop": το Progman κουβαλάει WS_EX_NOREDIRECTIONBITMAP —
        // ανιχνεύσιμο ρητά. Σε αυτή τη διάταξη ΔΕΝ υπάρχει καθόλου ξεχωριστό "αδερφό" WorkerW σαν
        // top-level window (το SHELLDLL_DefView είναι πλέον WS_EX_LAYERED child ΑΠΕΥΘΕΙΑΣ του
        // Progman) — το attach πρέπει να ακολουθήσει εντελώς διαφορετικό μονοπάτι από το κλασικό
        // (βλ. AttachRaisedDesktop). Επιβεβαιώθηκε διαβάζοντας το πραγματικό source της Lively
        // Wallpaper (rocksdanister/lively, WinDesktopCore.SetupDesktopLayer/TryAttachToDesktop) —
        // όχι δεύτερο χέρι από blog/PR περιγραφές.
        private static bool IsRaisedDesktop(IntPtr progman) =>
            (GetWindowLongPtr(progman, GWL_EXSTYLE).ToInt64() & WS_EX_NOREDIRECTIONBITMAP) != 0;

        // Βρίσκει το "κενό" WorkerW-αδερφό (αυτό ΔΕΝ περιέχει SHELLDLL_DefView) — ΜΟΝΟ για την
        // κλασική (μη-raised) διάταξη. Γυρνάει IntPtr.Zero αν δεν βρέθηκε ξεχωριστό αδερφό.
        private static IntPtr FindClassicWorkerWSibling(IntPtr progman)
        {
            IntPtr iconsHost = FindIconsWorkerW();
            if (iconsHost == IntPtr.Zero || iconsHost == progman) return IntPtr.Zero;
            return FindWindowEx(IntPtr.Zero, iconsHost, "WorkerW", null);
        }

        // Επισυνάπτει το παράθυρο πίσω από τα εικονίδια. targetBounds: οι ΑΠΟΛΥΤΕΣ συντεταγμένες
        // οθόνης (Screen.Bounds) όπου πρέπει να εμφανίζεται. Δύο εντελώς διαφορετικά μονοπάτια
        // ανάλογα με τη διάταξη της επιφάνειας εργασίας — βλ. AttachClassic/AttachRaisedDesktop.
        public static bool AttachToDesktop(IntPtr windowHandle, Rectangle targetBounds)
        {
            IntPtr progman = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            if (progman == IntPtr.Zero) return false;

            // Το WS_EX_LAYERED είναι πλέον ήδη στο CreateParams του WallpaperWindow (πριν καν
            // δημιουργηθεί το native handle) — εδώ μένει μόνο το SetLayeredWindowAttributes, ΠΡΙΝ
            // το SetParent, σε ΚΑΘΕ διάταξη (όχι μόνο raised) — ίδια σειρά με το
            // bbabcock1990/tool-animated-wallpapers (AttachToDesktop): "Make the layered window
            // fully opaque so DWM blts our content 1:1 — this is what allows a window hosted
            // under the desktop icons to render at all."
            EnableLayeredWindow(windowHandle);

            bool raised = IsRaisedDesktop(progman);

            // Ζητάει από το Progman να δημιουργήσει (αν δεν υπάρχει ήδη) το WorkerW πίσω από τα
            // εικονίδια. Στέλνουμε ΚΑΙ τις δύο παραλλαγές του 0x052C σε κάθε προσπάθεια: το
            // κλασικό (wParam=0,lParam=0) που δούλευε ήδη, ΚΑΙ wParam=0xD,lParam=1 — το ΜΟΝΟ που
            // στέλνει η ίδια η Lively Wallpaper (επιβεβαιωμένα λειτουργική, σε κάθε έκδοση
            // Windows, όχι μόνο raised desktop). Σε ΜΕΡΙΚΑ builds το WorkerW δεν υπάρχει ακόμη τη
            // στιγμή που επιστρέφει το SendMessageTimeout -> μικρό retry loop.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                // SMTO_ABORTIFHUNG (0x0002): μην περιμένεις όλο το timeout αν ο Explorer έχει
                // κολλήσει — γύρνα αμέσως αντί να μπλοκάρεις έως 1000ms άδικα ανά προσπάθεια.
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out _);
                SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0x0002, 1000, out _);

                if (raised)
                {
                    // Πρώτη προσπάθεια (απλούστερη, επιβεβαιωμένη ΚΑΙ από δεύτερο ανεξάρτητο
                    // ενεργό project που κάνει ακριβώς την ίδια δουλειά με WebView2 —
                    // bbabcock1990/tool-animated-wallpapers): τα ίδια τα Windows δημιουργούν ΗΔΗ
                    // ένα WorkerW-παιδί του Progman, σωστά τοποθετημένο πίσω από το DefView, για
                    // να ζωγραφίσει το προεπιλεγμένο φόντο· γίνεσαι child ΑΥΤΟΥ αντί να παλεύεις
                    // μόνος σου με το z-order σχετικά με το SHELLDLL_DefView.
                    IntPtr systemWallpaperWorkerW = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
                    if (systemWallpaperWorkerW != IntPtr.Zero && AttachClassic(windowHandle, systemWallpaperWorkerW, targetBounds))
                        return true;

                    // Fallback (πιο σύνθετο, από τη Lively Wallpaper): attach απευθείας στο
                    // Progman με ρητό z-order "αμέσως πίσω από το SHELLDLL_DefView".
                    IntPtr shellDllDefView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shellDllDefView != IntPtr.Zero && AttachRaisedDesktop(windowHandle, progman, shellDllDefView, targetBounds))
                        return true;
                }
                else
                {
                    IntPtr sibling = FindClassicWorkerWSibling(progman);
                    if (sibling != IntPtr.Zero && AttachClassic(windowHandle, sibling, targetBounds))
                        return true;
                }

                Thread.Sleep(60);
            }

            // Τελευταία λύση και στις δύο περιπτώσεις: attach απευθείας στο Progman (κάλυπτε ήδη
            // το "δεν εμφανίζονται καθόλου τα εικονίδια" bug όταν δεν βρέθηκε ξεχωριστός στόχος).
            return AttachClassic(windowHandle, progman, targetBounds);
        }

        // Μονοπάτι "raised desktop" (Windows 11 24H2+): parent = Progman ΑΠΕΥΘΕΙΑΣ (όχι κάποιο
        // WorkerW-αδερφό, δεν υπάρχει κανένα σε αυτή τη διάταξη), το παράθυρο πρέπει να γίνει
        // WS_EX_LAYERED ΠΡΙΝ το SetParent (το SHELLDLL_DefView είναι το ίδιο layered — ένα μη-
        // layered αδερφό στο ίδιο σημείο του z-order δεν compositάρεται σωστά από το DWM), και η
        // θέση στο z-order καθορίζεται ρητά "αμέσως πίσω από το SHELLDLL_DefView" αντί για απλά
        // "στον πάτο" — SetWindowPos(..., insertAfter: shellDllDefView, ...) το τοποθετεί ΑΚΡΙΒΩΣ
        // πίσω από τα εικονίδια, μπροστά από το σκέτο φόντο. Ακολουθεί επακριβώς τον αλγόριθμο
        // του WinDesktopCore.TryAttachToDesktop/EnsureWorkerWZOrder της Lively Wallpaper (δικός
        // μας κώδικας, όχι αντιγραμμένος — η ίδια η τεχνική/σειρά ενεργειών επαληθεύτηκε εκεί).
        private static bool AttachRaisedDesktop(IntPtr windowHandle, IntPtr progman, IntPtr shellDllDefView, Rectangle targetBounds)
        {
            MakeChildStyle(windowHandle);
            SetParent(windowHandle, progman);
            if (GetAncestor(windowHandle, GA_PARENT) != progman) return false;

            RepositionRelativeToParent(windowHandle, progman, targetBounds, shellDllDefView);
            LastAttachedWorkerW = progman;
            return true;
        }

        // Κλασικό μονοπάτι (Windows 10 / παλαιότερα Windows 11 builds): parent = ο ξεχωριστός
        // "κενός" WorkerW-αδερφός (ή το Progman απευθείας ως τελευταία λύση) — αμετάβλητο από
        // πριν, ήδη επιβεβαιωμένο ότι δουλεύει σε αυτή τη διάταξη.
        private static bool AttachClassic(IntPtr windowHandle, IntPtr target, Rectangle targetBounds)
        {
            MakeChildStyle(windowHandle);
            SetParent(windowHandle, target);
            if (GetAncestor(windowHandle, GA_PARENT) != target) return false;

            RepositionRelativeToParent(windowHandle, target, targetBounds, HWND_BOTTOM);
            LastAttachedWorkerW = target;
            return true;
        }

        // Τεχνική δανεισμένη από το Lively Wallpaper (WindowOperations.SetParentSafe): ένα
        // WinForms Form δημιουργείται πάντα με native style WS_POPUP (ακόμη και με
        // FormBorderStyle.None). Το SetParent αλλάζει ΜΟΝΟ τον λογικό parent, αλλά ΔΕΝ μετατρέπει
        // αυτόματα το WS_POPUP σε WS_CHILD — σε αρκετά builds των Windows 10/11 ένα WS_POPUP
        // παράθυρο μέσα σε WorkerW δεν clip-άρεται ούτε συμμετέχει σωστά στο z-order/compositing
        // του parent του, με αποτέλεσμα να παραμένει αόρατο ή να αναβοσβήνει ακόμη κι όταν το
        // SetParent "πέτυχε" τυπικά.
        private static void MakeChildStyle(IntPtr windowHandle)
        {
            long style = GetWindowLongPtr(windowHandle, GWL_STYLE).ToInt64();
            style = (style & ~WS_POPUP & ~WS_CAPTION) | WS_CHILD;
            SetWindowLongPtr(windowHandle, GWL_STYLE, new IntPtr(style));
            // SWP_FRAMECHANGED: αναγκάζει τα Windows να ξαναϋπολογίσουν το non-client frame μετά
            // την αλλαγή style· χωρίς αυτό η αλλαγή style μπορεί να μην εφαρμοστεί οπτικά.
            SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        // Κάνει το παράθυρο WS_EX_LAYERED με πλήρη αδιαφάνεια (alpha=255) — απαιτείται ΜΟΝΟ στη
        // raised desktop διάταξη, ΠΡΙΝ το SetParent (ίδια σειρά με τη Lively Wallpaper· κάποιοι
        // rendering engines/frameworks αποτυγχάνουν να εφαρμόσουν σωστά το WS_EX_LAYERED αν
        // εφαρμοστεί ΜΕΤΑ το SetParent).
        private static void EnableLayeredWindow(IntPtr windowHandle)
        {
            long exStyle = GetWindowLongPtr(windowHandle, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_LAYERED) == 0)
                SetWindowLongPtr(windowHandle, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_LAYERED));
            SetLayeredWindowAttributes(windowHandle, 0, 255, LWA_ALPHA);
        }

        private static void RepositionRelativeToParent(IntPtr windowHandle, IntPtr parent, Rectangle targetBounds, IntPtr insertAfter)
        {
            // Το parent (WorkerW ή Progman) καλύπτει όλο το virtual desktop· το γωνιαίο του
            // σημείο (Left,Top) σε συντεταγμένες οθόνης μπορεί να είναι αρνητικό (π.χ. -1920,0)
            // όταν υπάρχει οθόνη αριστερά/πάνω από την κύρια. Το παιδί πρέπει να τοποθετηθεί σε
            // συντεταγμένες ΣΧΕΤΙΚΕΣ με αυτή τη γωνία, όχι στις απόλυτες συντεταγμένες της
            // Screen.Bounds — γνωστό bug "wallpaper εμφανίζεται μόνο στην κύρια οθόνη" (Lively
            // Wallpaper GitHub issue #2438).
            if (!GetWindowRect(parent, out var parentRect)) return;
            int relX = targetBounds.X - parentRect.Left;
            int relY = targetBounds.Y - parentRect.Top;
            MoveWindow(windowHandle, relX, relY, targetBounds.Width, targetBounds.Height, true);

            // insertAfter = HWND_BOTTOM (κλασικό μονοπάτι: πίσω από ΟΛΑ τα αδέρφια μέσα στο
            // WorkerW-αδερφό, δεν υπάρχουν εικονίδια εκεί ούτως ή άλλως) ή το ίδιο το
            // SHELLDLL_DefView (raised desktop: τοποθετείται ΑΚΡΙΒΩΣ πίσω από τα εικονίδια, όχι
            // απλά "στον πάτο" του Progman — HWND_BOTTOM θα το έβαζε πίσω και από το δικό του
            // system WorkerW background renderer, λάθος σημείο).
            SetWindowPos(windowHandle, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
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
