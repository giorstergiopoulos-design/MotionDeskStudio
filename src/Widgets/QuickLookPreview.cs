using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    // Ελαφριά, δική μας εκδοχή του macOS Quick Look για τα DeskContainers: Space (ή "Προεπισκόπηση"
    // από το context menu) πάνω σε ένα αρχείο δείχνει άμεσα εικόνα/κείμενο/περιεχόμενα φακέλου, χωρίς
    // να ανοίξει καμία εξωτερική εφαρμογή. ΔΕΝ γαντζώνεται στο πραγματικό Windows shell preview
    // (IPreviewHandler) — θα απαιτούσε COM interop ανά τύπο αρχείου εφαρμογής τρίτων· αντ' αυτού
    // καλύπτει με το δικό της rendering τις πιο κοινές περιπτώσεις (εικόνες, απλό κείμενο, φάκελοι),
    // που είναι και η πλειοψηφία του περιεχομένου ενός desktop container.
    internal static class QuickLookPreview
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        private static readonly string[] TextExtensions = { ".txt", ".md", ".json", ".xml", ".csv", ".log", ".ini", ".cs", ".html", ".htm", ".js", ".css", ".ps1", ".py", ".yml", ".yaml" };
        private const long MaxTextPreviewBytes = 200_000;

        public static void Show(string path)
        {
            try
            {
                if (Directory.Exists(path)) { ShowFolderPreview(path); return; }
                if (!File.Exists(path)) return;

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ImageExtensions.Contains(ext)) ShowImagePreview(path);
                else if (TextExtensions.Contains(ext)) ShowTextPreview(path);
                else ShowGenericPreview(path);
            }
            catch { }
        }

        private static Form CreateShell(string title, Size size)
        {
            var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = size,
                BackColor = UiTheme.Sidebar,
                ForeColor = UiTheme.TextPrimary,
                KeyPreview = true
            };
            form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space) form.Close(); };
            return form;
        }

        private static void ShowImagePreview(string path)
        {
            Image img;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                img = Image.FromStream(fs);

            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            int maxW = (int)(screen.Width * 0.6), maxH = (int)(screen.Height * 0.6);
            double scale = Math.Min(1.0, Math.Min((double)maxW / img.Width, (double)maxH / img.Height));
            var size = new Size(Math.Max(200, (int)(img.Width * scale)), Math.Max(150, (int)(img.Height * scale)) + 24);

            var form = CreateShell(Path.GetFileName(path), size);
            var box = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = img, BackColor = UiTheme.Background };
            form.Controls.Add(box);
            var info = new Label { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, Text = $"{img.Width}×{img.Height}  •  {FormatSize(new FileInfo(path).Length)}" };
            form.Controls.Add(info);
            form.FormClosed += (_, _) => img.Dispose();
            form.Show();
        }

        private static void ShowTextPreview(string path)
        {
            var fi = new FileInfo(path);
            string content;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var reader = new StreamReader(fs))
            {
                var buffer = new char[Math.Min(fi.Length, MaxTextPreviewBytes)];
                int read = reader.Read(buffer, 0, buffer.Length);
                content = new string(buffer, 0, read);
                if (fi.Length > MaxTextPreviewBytes) content += MotionDesk.Services.LocalizationManager.T("QuickLook.Truncated");
            }
            // WinForms TextBox δεν αναγνωρίζει μοναχά-\n αλλαγές γραμμής (π.χ. αρχεία από git με LF) —
            // κανονικοποίηση σε \r\n πριν την εμφάνιση, αλλιώς όλο το κείμενο φαίνεται σε μία γραμμή.
            content = content.Replace("\r\n", "\n").Replace("\n", "\r\n");

            var form = CreateShell(Path.GetFileName(path), new Size(640, 480));
            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Cascadia Mono", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                Text = content
            };
            form.Controls.Add(box);
            form.Shown += (_, _) => box.SelectionStart = 0;
            form.Show();
        }

        private static void ShowGenericPreview(string path)
        {
            var fi = new FileInfo(path);
            Icon? icon = null;
            try { icon = Icon.ExtractAssociatedIcon(path); } catch { }

            var form = CreateShell(Path.GetFileName(path), new Size(340, 220));
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(16) };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var iconBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.CenterImage };
            if (icon != null) iconBox.Image = icon.ToBitmap();
            panel.Controls.Add(iconBox, 0, 0);

            var nameLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Text = fi.Name };
            panel.Controls.Add(nameLabel, 0, 1);

            var detail = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopCenter,
                ForeColor = UiTheme.TextMuted,
                Text = $"{FormatSize(fi.Length)}\n{fi.LastWriteTime:g}\n{fi.Extension.ToUpperInvariant()} file"
            };
            panel.Controls.Add(detail, 0, 2);

            form.Controls.Add(panel);
            form.FormClosed += (_, _) => icon?.Dispose();
            form.Show();
        }

        private static void ShowFolderPreview(string path)
        {
            var form = CreateShell(Path.GetFileName(path.TrimEnd('\\', '/')), new Size(420, 460));
            var list = new ListBox { Dock = DockStyle.Fill, BackColor = UiTheme.Background, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f) };
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(path).Take(200)) list.Items.Add("📁 " + Path.GetFileName(dir));
                foreach (var file in Directory.EnumerateFiles(path).Take(200)) list.Items.Add("📄 " + Path.GetFileName(file));
                if (list.Items.Count == 0) list.Items.Add(MotionDesk.Services.LocalizationManager.T("QuickLook.EmptyFolder"));
            }
            catch (Exception ex) { list.Items.Add(string.Format(MotionDesk.Services.LocalizationManager.T("QuickLook.ReadErrorFormat"), ex.Message)); }
            form.Controls.Add(list);
            form.Show();
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int i = 0;
            while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
            return $"{size:0.#} {units[i]}";
        }
    }
}
