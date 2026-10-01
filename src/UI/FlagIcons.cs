using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MotionDesk.UI
{
    // Μικρά bitmap σημαιών σχεδιασμένα από το μηδέν με GDI+ primitives — ΟΧΙ unicode emoji
    // χαρακτήρες (🇬🇷/🇬🇧). Βρέθηκε ότι τα πραγματικά emoji σημαίες αποδίδονται ως απλά γράμματα
    // περιφερειακού δείκτη ("GR"/"GB" σε πλαισιάκι) τόσο μέσω GDI (ToolStripMenuItem text) όσο
    // και μέσω GDI+ (Graphics.DrawString) — καμία από τις δύο κλασικές WinForms text APIs
    // υποστηρίζει πραγματικά έγχρωμες (COLR/CPAL) emoji γραμματοσειρές, ανεξάρτητα από ποια
    // γραμματοσειρά ζητηθεί (ακόμα και "Segoe UI Emoji"). Μόνο DirectWrite/Direct2D (εκτός
    // WinForms) τα αποδίδει σωστά. Λύση: πραγματικά bitmap εικονίδια μέσω Image — λειτουργεί
    // πάντα, ανεξάρτητα από font/rendering quirks.
    internal static class FlagIcons
    {
        public static Bitmap Greece(int w = 20, int h = 14)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.None;
            var blue = Color.FromArgb(13, 94, 175);
            using var blueBrush = new SolidBrush(blue);
            using var whiteBrush = new SolidBrush(Color.White);
            g.FillRectangle(whiteBrush, 0, 0, w, h);
            float stripeH = h / 9f;
            for (int i = 0; i < 9; i += 2)
                g.FillRectangle(blueBrush, 0, i * stripeH, w, stripeH);
            int cantonW = w * 5 / 9, cantonH = (int)(stripeH * 5);
            g.FillRectangle(blueBrush, 0, 0, cantonW, cantonH);
            using var whitePen = new Pen(Color.White, Math.Max(1f, cantonW / 5f));
            g.DrawLine(whitePen, cantonW / 2f, 0, cantonW / 2f, cantonH);
            g.DrawLine(whitePen, 0, cantonH / 2f, cantonW, cantonH / 2f);
            return bmp;
        }

        public static Bitmap UnitedKingdom(int w = 20, int h = 14)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var navy = Color.FromArgb(1, 33, 105);
            var red = Color.FromArgb(200, 16, 46);
            using (var navyBrush = new SolidBrush(navy)) g.FillRectangle(navyBrush, 0, 0, w, h);
            using (var whiteDiag = new Pen(Color.White, h / 3.5f))
            {
                g.DrawLine(whiteDiag, 0, 0, w, h);
                g.DrawLine(whiteDiag, 0, h, w, 0);
            }
            using (var redDiag = new Pen(red, h / 7f))
            {
                g.DrawLine(redDiag, 0, 0, w, h);
                g.DrawLine(redDiag, 0, h, w, 0);
            }
            float vW = h / 2.2f, hH = h / 2.8f;
            using (var whiteCross = new SolidBrush(Color.White))
            {
                g.FillRectangle(whiteCross, w / 2f - vW / 2f, 0, vW, h);
                g.FillRectangle(whiteCross, 0, h / 2f - hH / 2f, w, hH);
            }
            float vW2 = h / 4.5f, hH2 = h / 5.5f;
            using (var redCross = new SolidBrush(red))
            {
                g.FillRectangle(redCross, w / 2f - vW2 / 2f, 0, vW2, h);
                g.FillRectangle(redCross, 0, h / 2f - hH2 / 2f, w, hH2);
            }
            return bmp;
        }

        public static Bitmap Globe(int w = 20, int h = 14)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(UiTheme.TextSecondary, 1.2f);
            float d = h - 2f;
            var rect = new RectangleF((w - d) / 2f, 1f, d, d);
            g.DrawEllipse(pen, rect);
            g.DrawLine(pen, rect.Left, rect.Top + d / 2f, rect.Right, rect.Top + d / 2f);
            g.DrawEllipse(pen, new RectangleF(rect.Left + d * 0.22f, rect.Top, d * 0.56f, d));
            return bmp;
        }
    }
}
