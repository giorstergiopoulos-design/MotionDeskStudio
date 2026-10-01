using System;
using System.IO;
using System.Linq;
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
        // Επέκταση κάλυψης depot (ζητήθηκε ρητά v1.5.0): τα υπόλοιπα τυπικά εικονίδια που τα ίδια
        // τα Windows επιτρέπουν να αλλάξουν μέσω Personalization/registry — δεν υπάρχει δημόσιο
        // API για "όλα τα εικονίδια των Windows" (άπειρο, απροσδιόριστο σύνολο), μόνο αυτό το
        // συγκεκριμένο, γνωστό σύνολο CLSID-based εικονιδίων επιφάνειας εργασίας.
        private const string NetworkClsid = "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
        private const string ControlPanelClsid = "{21EC2020-3AEA-1069-A2DD-08002B30309D}";
        private const string UsersFilesClsid = "{59031A47-3F72-44A7-89C5-5595FE6B30EE}";

        public static string? GetThisPcIcon() => GetClsidIcon(ThisPcClsid, null);
        public static string? GetRecycleBinEmptyIcon() => GetClsidIcon(RecycleBinClsid, "empty");
        public static string? GetRecycleBinFullIcon() => GetClsidIcon(RecycleBinClsid, "full");
        public static string? GetNetworkIcon() => GetClsidIcon(NetworkClsid, null);
        public static string? GetControlPanelIcon() => GetClsidIcon(ControlPanelClsid, null);
        public static string? GetUsersFilesIcon() => GetClsidIcon(UsersFilesClsid, null);

        public static void SetThisPcIcon(string? icoPath) => SetClsidIcon(ThisPcClsid, null, icoPath);
        public static void SetRecycleBinEmptyIcon(string? icoPath) => SetClsidIcon(RecycleBinClsid, "empty", icoPath);
        public static void SetRecycleBinFullIcon(string? icoPath) => SetClsidIcon(RecycleBinClsid, "full", icoPath);
        public static void SetNetworkIcon(string? icoPath) => SetClsidIcon(NetworkClsid, null, icoPath);
        public static void SetControlPanelIcon(string? icoPath) => SetClsidIcon(ControlPanelClsid, null, icoPath);
        public static void SetUsersFilesIcon(string? icoPath) => SetClsidIcon(UsersFilesClsid, null, icoPath);

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

        // "Depot" icon packs: στο web υπάρχουν έτοιμα σετ εικονιδίων Κάδου Ανακύκλωσης που
        // καλύπτουν και τις δύο καταστάσεις (π.χ. sdushantha/recycle-bin-themes στο GitHub),
        // ακολουθώντας τη σύμβαση ονοματοδοσίας "όνομα-empty.ico" / "όνομα-full.ico" (με "-", "_"
        // ή κενό ως διαχωριστικό) — ζητήθηκε ρητά να λαμβάνεται αυτό υπόψη στις ρυθμίσεις του
        // IconAtlas. Σαρώνει έναν φάκελο που επιλέγει ο χρήστης και ταιριάζει αυτόματα το ζευγάρι.
        public sealed class DepotImportResult
        {
            public string? EmptyIconPath { get; set; }
            public string? FullIconPath { get; set; }
            public string? SingleIconPath { get; set; }
        }

        public static DepotImportResult ScanDepotFolder(string folderPath)
        {
            var result = new DepotImportResult();
            if (!Directory.Exists(folderPath)) return result;

            var icoFiles = Directory.GetFiles(folderPath, "*.ico", SearchOption.TopDirectoryOnly);
            string? FindByKeyword(string keyword) => icoFiles.FirstOrDefault(f =>
            {
                string name = Path.GetFileNameWithoutExtension(f);
                return name.EndsWith("-" + keyword, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("_" + keyword, StringComparison.OrdinalIgnoreCase)
                    || name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            });

            result.EmptyIconPath = FindByKeyword("empty");
            result.FullIconPath = FindByKeyword("full");
            if (result.EmptyIconPath == null && result.FullIconPath == null && icoFiles.Length == 1)
                result.SingleIconPath = icoFiles[0];
            return result;
        }

        public static void ApplyDepotToRecycleBin(DepotImportResult depot)
        {
            if (depot.EmptyIconPath != null) SetRecycleBinEmptyIcon(depot.EmptyIconPath);
            if (depot.FullIconPath != null) SetRecycleBinFullIcon(depot.FullIconPath);
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        public static void RefreshShellIcons() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }
}
