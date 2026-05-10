using Microsoft.Win32;
using System.Drawing;
using System.Windows.Forms;

namespace TPL
{
    public static class ThemeManager
    {
        public static bool IsDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int val)
                    return val == 0;
            }
            catch { }
            return false;
        }

        // Dark palette
        public static readonly Color DarkBg = Color.FromArgb(28, 28, 28);
        public static readonly Color DarkSurface = Color.FromArgb(45, 45, 48);
        public static readonly Color DarkInput = Color.FromArgb(60, 60, 63);
        public static readonly Color DarkBorder = Color.FromArgb(80, 80, 80);
        public static readonly Color DarkText = Color.FromArgb(220, 220, 220);
        public static readonly Color DarkMuted = Color.FromArgb(155, 155, 155);
        public static readonly Color Accent = Color.FromArgb(0, 122, 204);
        public static readonly Color AccentDark = Color.FromArgb(0, 99, 164);

        public static void Apply(Control root, bool dark)
        {
            ApplySingle(root, dark);
            foreach (Control child in root.Controls)
                Apply(child, dark);
        }

        private static void ApplySingle(Control c, bool dark)
        {
            if (!dark) return;

            if (c is Form)
            {
                c.BackColor = DarkBg;
                c.ForeColor = DarkText;
            }
            else if (c is Panel)
            {
                c.BackColor = DarkSurface;
                c.ForeColor = DarkText;
            }
            else if (c is Label lbl)
            {
                c.BackColor = Color.Transparent;
                if (c.ForeColor == Color.Green) c.ForeColor = Color.FromArgb(100, 200, 100);
                else if (c.ForeColor == Color.DimGray) c.ForeColor = DarkMuted;
                else if (c.ForeColor == Color.Red) { /* keep red */ }
                else if (c.ForeColor != Accent) c.ForeColor = lbl.Font.Bold ? DarkText : DarkMuted;
            }
            else if (c is TextBox)
            {
                c.BackColor = DarkInput;
                c.ForeColor = DarkText;
            }
            else if (c is ComboBox cb)
            {
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = DarkInput;
                cb.ForeColor = DarkText;
            }
            else if (c is RadioButton || c is CheckBox)
            {
                c.BackColor = Color.Transparent;
                if (c.ForeColor == Color.DimGray) c.ForeColor = DarkMuted;
                else if (c.ForeColor == Color.Green) c.ForeColor = Color.FromArgb(100, 200, 100);
                else if (c.ForeColor == Color.Blue) c.ForeColor = Color.FromArgb(100, 180, 255);
                else if (c.ForeColor == Color.Purple) c.ForeColor = Color.FromArgb(220, 130, 255);
                else if (c.ForeColor != Color.FromArgb(100, 200, 100) && c.ForeColor != Color.FromArgb(100, 180, 255) && c.ForeColor != Color.FromArgb(220, 130, 255))
                    c.ForeColor = DarkText;
            }
            else if (c is Button btn)
            {
                // Don't touch the accent plot button
                if (btn.BackColor != Accent && btn.BackColor != AccentDark)
                {
                    btn.BackColor = DarkSurface;
                    btn.ForeColor = DarkText;
                    if (btn.FlatStyle == FlatStyle.Flat)
                        btn.FlatAppearance.BorderColor = DarkBorder;
                }
            }
            else if (c is ListBox || c is ProgressBar)
            {
                c.BackColor = DarkSurface;
                c.ForeColor = DarkText;
            }
        }

        /// <summary>Creates a simple programmatic icon for the dialog window.</summary>
        public static System.Drawing.Icon CreateAppIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Accent);
            // Draw "P" centered
            using (var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.White))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("P", font, brush, new RectangleF(0, 0, 32, 32), sf);
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }
    }
}
