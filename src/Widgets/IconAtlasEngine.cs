using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MotionDesk.Widgets
{
    // "IconAtlas" — αλλαγή εικονιδίων συστήματος (This PC, Κάδος Ανακύκλωσης άδειος/γεμάτος,
    // εικονίδιο ανά φάκελο) μέσω επίσημων registry mechanisms — ό,τι ακριβώς χρησιμοποιεί το ίδιο
    // το Explorer/Personalization panel, καμία τροποποίηση αρχείων συστήματος. Επαληθεύτηκε έναντι
    // πραγματικού registry state πριν γραφτεί (CLSID paths, μορφή τιμών "path,index").
    internal static class IconAtlasEngine
    {
        private const string ThisPcClsid = "{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        private const string RecycleBinClsid = "{645FF040-5081-101B-9F08-00AA002F954E}";

        public static string? GetThisPcIcon() => GetClsidIcon(ThisPcClsid, null);
        public static string? GetRecycleBinEmptyIcon() => GetClsidIcon(RecycleBinClsid, "empty");
        public static string? GetRecycleBinFullIcon() => GetClsidIcon(RecycleBinClsid, "full");

        public static void SetThisPcIcon(string? icoPath) => SetClsidIcon(ThisPcClsid, null, icoPath);
        public static void SetRecycleBinEmptyIcon(string? icoPath) => SetClsidIcon(RecycleBinClsid, "empty", icoPath);
        public static void SetRecycleBinFullIcon(string? icoPath) => SetClsidIcon(RecycleBinClsid, "full", icoPath);

        private static string? GetClsidIcon(string clsid, string? namedValue)
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{clsid}\DefaultIcon");
            var raw = key?.GetValue(namedValue ?? string.Empty) as string;
            if (string.IsNullOrEmpty(raw)) return null;
            int comma = raw.LastIndexOf(',');
            return comma > 0 ? raw[..comma] : raw;
        }

        private static void SetClsidIcon(string clsid, string? namedValue, string? icoPath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{clsid}\DefaultIcon");
            if (key == null) return;
            string valueName = namedValue ?? string.Empty;
            if (string.IsNullOrEmpty(icoPath)) key.DeleteValue(valueName, false);
            else key.SetValue(valueName, $"{icoPath},0", RegistryValueKind.ExpandString);
            RefreshShellIcons();
        }

        // Γενικό εικονίδιο ενός συγκεκριμένου φακέλου μέσω desktop.ini — ίδια τεχνική με αυτή που
        // χρησιμοποιεί το ίδιο το Explorer όταν ο χρήστης αλλάζει εικονίδιο φακέλου από το UI του
        // ("Ιδιότητες" → "Προσαρμογή"). Το ReadOnly attribute στον ΦΑΚΕΛΟ (όχι στα περιεχόμενά του)
        // είναι το επίσημο σήμα προς το Explorer "αυτός ο φάκελος έχει custom desktop.ini".
        public static void SetFolderIcon(string folderPath, string? icoPath)
        {
            string iniPath = Path.Combine(folderPath, "desktop.ini");
            if (string.IsNullOrEmpty(icoPath))
            {
                if (File.Exists(iniPath))
                {
                    try { File.SetAttributes(iniPath, FileAttributes.Normal); File.Delete(iniPath); } catch (IOException) { }
                }
                try { File.SetAttributes(folderPath, File.GetAttributes(folderPath) & ~FileAttributes.ReadOnly); } catch (IOException) { }
            }
            else
            {
                File.WriteAllText(iniPath, $"[.ShellClassInfo]\r\nIconResource={icoPath},0\r\n");
                File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
                try { File.SetAttributes(folderPath, File.GetAttributes(folderPath) | FileAttributes.ReadOnly); } catch (IOException) { }
            }
            RefreshShellIcons();
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        public static void RefreshShellIcons() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }
}
