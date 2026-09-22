using System;
using System.Windows.Forms;
using MotionDesk.Widgets;

namespace MotionDesk
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Ελάχιστος elevated "helper" branch — καμία θυρίδα UI, μόνο μία εγγραφή HKLM και
            // έξοδος. Ενεργοποιείται ΜΟΝΟ όταν το κύριο (μη-elevated) UI ξεκινήσει ξανά την ίδια
            // exe με Verb="runas" (βλ. LockScreenEngine.TrySetLockScreenImageElevated) — έτσι δεν
            // χρειάζεται ολόκληρη η εφαρμογή να τρέχει elevated για μία μόνο ενέργεια.
            if (args.Length == 2 && args[0] == "--set-lockscreen")
            {
                try { LockScreenEngine.WriteLockScreenRegistry(args[1]); } catch (Exception) { }
                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportError(e.ExceptionObject as Exception);

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext());
        }

        private static void ReportError(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MotionDeskStudio", "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex}\n\n");
            }
            catch (System.IO.IOException) { }

            MessageBox.Show($"Παρουσιάστηκε σφάλμα:\n\n{ex.Message}", "MotionDesk Studio",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
