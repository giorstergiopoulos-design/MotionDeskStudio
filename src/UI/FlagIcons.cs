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

        // 1.7.8 - σημαίες για ΟΛΕΣ τις γλώσσες της εφαρμογής (ίδιες 14 με το GearWin), ζωγραφισμένες με GDI+ primitives.
        // Απλοποιημένες για μέγεθος 20x14 (π.χ. οι σημαίες Νότιας Κορέας/Σαουδικής Αραβίας/Ινδίας δείχνουν το
        // χαρακτηριστικό σύμβολο χωρίς λεπτομέρειες που δεν διακρίνονται σε αυτό το μέγεθος).
        public static Bitmap For(string code, int w = 20, int h = 14) => code switch
        {
            "el" => Greece(w, h),
            "en" => UnitedKingdom(w, h),
            "de" => HorizontalStripes(w, h, Color.Black, Color.FromArgb(221, 0, 0), Color.FromArgb(255, 206, 0)),
            "fr" => VerticalStripes(w, h, Color.FromArgb(0, 85, 164), Color.White, Color.FromArgb(239, 65, 53)),
            "es" => Spain(w, h),
            "it" => VerticalStripes(w, h, Color.FromArgb(0, 146, 70), Color.White, Color.FromArgb(206, 43, 55)),
            "ru" => HorizontalStripes(w, h, Color.White, Color.FromArgb(0, 57, 166), Color.FromArgb(213, 43, 30)),
            "ja" => Disc(w, h, Color.White, Color.FromArgb(188, 0, 45), 0.30f),
            "pt" => Portugal(w, h),
            "tr" => Turkey(w, h),
            "zh" => China(w, h),
            "ko" => Korea(w, h),
            "ar" => SaudiArabia(w, h),
            "hi" => India(w, h),
            _ => Globe(w, h),
        };

        private static Bitmap HorizontalStripes(int w, int h, Color a, Color b, Color c)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.None;
            using (var ba = new SolidBrush(a)) g.FillRectangle(ba, 0, 0, w, h / 3f);
            using (var bb = new SolidBrush(b)) g.FillRectangle(bb, 0, h / 3f, w, h / 3f);
            using (var bc = new SolidBrush(c)) g.FillRectangle(bc, 0, h * 2 / 3f, w, h / 3f + 1);
            return bmp;
        }

        private static Bitmap VerticalStripes(int w, int h, Color a, Color b, Color c)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.None;
            using (var ba = new SolidBrush(a)) g.FillRectangle(ba, 0, 0, w / 3f, h);
            using (var bb = new SolidBrush(b)) g.FillRectangle(bb, w / 3f, 0, w / 3f, h);
            using (var bc = new SolidBrush(c)) g.FillRectangle(bc, w * 2 / 3f, 0, w / 3f + 1, h);
            return bmp;
        }

        private static Bitmap Disc(int w, int h, Color bg, Color disc, float radiusFraction)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, 0, 0, w, h);
            float r = h * radiusFraction * 1.5f;
            using (var d = new SolidBrush(disc)) g.FillEllipse(d, w / 2f - r, h / 2f - r, r * 2, r * 2);
            using (var edge = new Pen(Color.FromArgb(60, 0, 0, 0))) g.DrawRectangle(edge, 0, 0, w - 1, h - 1);
            return bmp;
        }

        private static Bitmap Spain(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.None;
            using (var red = new SolidBrush(Color.FromArgb(170, 21, 27))) g.FillRectangle(red, 0, 0, w, h);
            using (var yellow = new SolidBrush(Color.FromArgb(241, 191, 0))) g.FillRectangle(yellow, 0, h / 4f, w, h / 2f);
            return bmp;
        }

        private static Bitmap Portugal(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var green = new SolidBrush(Color.FromArgb(0, 102, 0))) g.FillRectangle(green, 0, 0, w * 0.4f, h);
            using (var red = new SolidBrush(Color.FromArgb(255, 0, 0))) g.FillRectangle(red, w * 0.4f, 0, w * 0.6f, h);
            using (var yellow = new SolidBrush(Color.FromArgb(255, 204, 0))) g.FillEllipse(yellow, w * 0.4f - h * 0.28f, h / 2f - h * 0.28f, h * 0.56f, h * 0.56f);
            return bmp;
        }

        private static Bitmap Turkey(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var red = new SolidBrush(Color.FromArgb(227, 10, 23))) g.FillRectangle(red, 0, 0, w, h);
            using var white = new SolidBrush(Color.White);
            float r = h * 0.30f;
            g.FillEllipse(white, w * 0.30f - r, h / 2f - r, r * 2, r * 2);
            using (var red2 = new SolidBrush(Color.FromArgb(227, 10, 23)))
            {
                float r2 = r * 0.80f;
                g.FillEllipse(red2, w * 0.30f - r2 + r * 0.30f, h / 2f - r2, r2 * 2, r2 * 2);
            }
            g.FillEllipse(white, w * 0.57f - 1.4f, h / 2f - 1.4f, 2.8f, 2.8f);
            return bmp;
        }

        private static Bitmap China(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var red = new SolidBrush(Color.FromArgb(222, 41, 16))) g.FillRectangle(red, 0, 0, w, h);
            using var yellow = new SolidBrush(Color.FromArgb(255, 222, 0));
            g.FillEllipse(yellow, w * 0.10f, h * 0.12f, h * 0.34f, h * 0.34f);
            for (int i = 0; i < 4; i++)
                g.FillEllipse(yellow, w * (0.34f + (i % 2) * 0.07f), h * (0.08f + i * 0.2f), h * 0.12f, h * 0.12f);
            return bmp;
        }

        private static Bitmap Korea(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            float d = h * 0.52f, cx = w / 2f, cy = h / 2f;
            using (var red = new SolidBrush(Color.FromArgb(205, 46, 58))) g.FillPie(red, cx - d / 2, cy - d / 2, d, d, 180, 180);
            using (var blue = new SolidBrush(Color.FromArgb(0, 71, 160))) g.FillPie(blue, cx - d / 2, cy - d / 2, d, d, 0, 180);
            using (var bar = new SolidBrush(Color.Black))
            {
                g.FillRectangle(bar, 1.5f, 1.5f, 3.5f, 1.2f); g.FillRectangle(bar, w - 5f, 1.5f, 3.5f, 1.2f);
                g.FillRectangle(bar, 1.5f, h - 2.7f, 3.5f, 1.2f); g.FillRectangle(bar, w - 5f, h - 2.7f, 3.5f, 1.2f);
            }
            using (var edge = new Pen(Color.FromArgb(60, 0, 0, 0))) g.DrawRectangle(edge, 0, 0, w - 1, h - 1);
            return bmp;
        }

        private static Bitmap SaudiArabia(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var green = new SolidBrush(Color.FromArgb(0, 106, 68))) g.FillRectangle(green, 0, 0, w, h);
            using var white = new Pen(Color.White, 1.4f);
            g.DrawLine(white, w * 0.22f, h * 0.42f, w * 0.78f, h * 0.42f);   // γραφή (απλοποιημένη)
            g.DrawLine(white, w * 0.25f, h * 0.68f, w * 0.75f, h * 0.68f);   // ξίφος
            return bmp;
        }

        private static Bitmap India(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var saffron = new SolidBrush(Color.FromArgb(255, 153, 51))) g.FillRectangle(saffron, 0, 0, w, h / 3f);
            using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, 0, h / 3f, w, h / 3f);
            using (var green = new SolidBrush(Color.FromArgb(19, 136, 8))) g.FillRectangle(green, 0, h * 2 / 3f, w, h / 3f + 1);
            using (var navy = new Pen(Color.FromArgb(0, 0, 128), 1f)) g.DrawEllipse(navy, w / 2f - 2.4f, h / 2f - 2.4f, 4.8f, 4.8f);
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
