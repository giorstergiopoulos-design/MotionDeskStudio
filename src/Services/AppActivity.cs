using System.Runtime.InteropServices;
using System.Text;

namespace MotionDesk.Services;

/// <summary>
/// Κεντρική γνώση του αν το desktop είναι καλυμμένο από εφαρμογή πλήρους οθόνης (παιχνίδι, video
/// player). Πριν, μόνο το wallpaper σταματούσε σε fullscreen — όλα τα widgets (ρολόι, system monitor,
/// audio visualizer στα 25Hz) συνέχιζαν να τρέχουν timers και να ζωγραφίζουν κάτω από το παιχνίδι,
/// κλέβοντας CPU/GPU ακριβώς όταν χρειάζεται όλη η ισχύς. Τώρα υπάρχει ΕΝΑ shared state: τα widgets
/// ελέγχουν <see cref="IsFullscreenAppActive"/> στα ticks τους και το wallpaper εγγράφεται στο
/// <see cref="FullscreenChanged"/>. Πρέπει να δημιουργηθεί/ξεκινήσει από το UI thread.
/// </summary>
public static class AppActivity
{
    private static System.Windows.Forms.Timer? _timer;
    public static bool IsFullscreenAppActive { get; private set; }
    public static event Action<bool>? FullscreenChanged;

    public static void Start()
    {
        if (_timer != null) return;
        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) =>
        {
            bool now = IsForegroundWindowFullscreen();
            if (now == IsFullscreenAppActive) return;
            IsFullscreenAppActive = now;
            FullscreenChanged?.Invoke(now);
        };
        _timer.Start();
    }

    private static bool IsForegroundWindowFullscreen()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        if (!GetWindowRect(hwnd, out var rect)) return false;

        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        string cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        var screen = Screen.FromRectangle(new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
        var b = screen.Bounds;
        return rect.Left <= b.Left && rect.Top <= b.Top && rect.Right >= b.Right && rect.Bottom >= b.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
