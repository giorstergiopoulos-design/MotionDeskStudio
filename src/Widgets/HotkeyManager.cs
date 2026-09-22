using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    // Message-only παράθυρο που καταγράφει παγκόσμιες συντομεύσεις πληκτρολογίου (RegisterHotKey)
    // και σηκώνει ένα .NET event, ώστε να μη χρειάζεται να "κρύβουμε" hotkey λογική μέσα σε φόρμες UI.
    public sealed class HotkeyManager : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;

        private int _nextId = 9000;
        private readonly System.Collections.Generic.Dictionary<int, Action> _handlers = new();

        public HotkeyManager()
        {
            CreateHandle(new CreateParams());
        }

        public void RegisterCtrlAlt(char key, Action onPressed)
        {
            int id = _nextId++;
            _handlers[id] = onPressed;
            RegisterHotKey(Handle, id, MOD_CONTROL | MOD_ALT, (uint)key);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && _handlers.TryGetValue(m.WParam.ToInt32(), out var handler))
            {
                handler.Invoke();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (var id in _handlers.Keys)
                UnregisterHotKey(Handle, id);
            _handlers.Clear();
            DestroyHandle();
        }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
