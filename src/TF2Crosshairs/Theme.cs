using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TF2Crosshairs
{
    public sealed class Palette
    {
        public bool Dark;
        public Color Bg, Panel, Input, Fore, Muted, Border, Button, ButtonHover, Warn, GroupText, Accent, HeaderRow;
    }

    /// <summary>
    /// WinForms on .NET Framework has no dark theme of its own, so this paints one: colours on every
    /// control, plus the Windows dark-mode window theme for the title bar, scrollbars and list selection.
    /// </summary>
    public static class Theme
    {
        public static readonly Palette DarkPalette = new()
        {
            Dark = true,
            Bg = Color.FromArgb(32, 32, 34),
            Panel = Color.FromArgb(41, 41, 44),
            Input = Color.FromArgb(50, 50, 54),
            Fore = Color.FromArgb(232, 232, 232),
            Muted = Color.FromArgb(145, 145, 150),
            Border = Color.FromArgb(82, 82, 88),
            Button = Color.FromArgb(60, 60, 66),
            ButtonHover = Color.FromArgb(80, 80, 88),
            Warn = Color.FromArgb(255, 128, 118),
            GroupText = Color.FromArgb(120, 170, 255),
            Accent = Color.FromArgb(38, 110, 190),
            HeaderRow = Color.FromArgb(34, 34, 37),
        };

        public static readonly Palette LightPalette = new()
        {
            Dark = false,
            Bg = SystemColors.Control,
            Panel = SystemColors.Window,
            Input = SystemColors.Window,
            Fore = SystemColors.ControlText,
            Muted = SystemColors.GrayText,
            Border = Color.FromArgb(205, 205, 205),
            Button = SystemColors.Control,
            ButtonHover = SystemColors.ControlLight,
            Warn = Color.Firebrick,
            GroupText = SystemColors.HotTrack,
            Accent = SystemColors.Highlight,
            HeaderRow = Color.FromArgb(240, 240, 240),
        };

        public static Palette Current = DarkPalette;

        public static void Apply(Form root, Palette p)
        {
            Current = p;
            ApplyColors(root, p);
            if (root.IsHandleCreated) ApplyNative(root, p);
        }

        static void ApplyColors(Control c, Palette p)
        {
            var border = p.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
            var flat = p.Dark ? FlatStyle.Flat : FlatStyle.Standard;
            switch (c)
            {
                case ListView lv:
                    lv.BackColor = p.Panel;
                    lv.ForeColor = p.Fore;
                    lv.BorderStyle = border;
                    break;
                case TextBox tb:
                    tb.BackColor = p.Input;
                    tb.ForeColor = p.Fore;
                    tb.BorderStyle = border;
                    break;
                case NumericUpDown n:
                    n.BackColor = p.Input;
                    n.ForeColor = p.Fore;
                    n.BorderStyle = border;
                    break;
                case ComboBox cb:
                    cb.BackColor = p.Input;
                    cb.ForeColor = p.Fore;
                    cb.FlatStyle = flat;
                    break;
                case Button b:
                    b.BackColor = p.Button;
                    b.ForeColor = p.Fore;
                    b.FlatStyle = flat;
                    b.FlatAppearance.BorderColor = p.Border;
                    b.FlatAppearance.MouseOverBackColor = p.ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = p.Border;
                    b.UseVisualStyleBackColor = !p.Dark;
                    break;
                case PreviewPanel or Gallery:
                    break; // paint themselves from Theme.Current
                case SplitContainer sc:
                    sc.BackColor = p.Bg;
                    sc.Panel1.BackColor = p.Bg;
                    sc.Panel2.BackColor = p.Bg;
                    break;
                default: // the form, labels, the checkbox and the layout panels
                    c.BackColor = p.Bg;
                    c.ForeColor = p.Fore;
                    break;
            }
            foreach (Control child in c.Controls) ApplyColors(child, p);
        }

        // ---- native dark mode (Windows 10 1809+; silently ignored elsewhere) ----

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

        public static void ApplyNative(Form root, Palette p)
        {
            try
            {
                int v = p.Dark ? 1 : 0;
                // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on 20H1+, 19 on older 1809-1909 builds.
                if (DwmSetWindowAttribute(root.Handle, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(root.Handle, 19, ref v, 4);
            }
            catch
            {
                // older Windows: no dark title bar, nothing else breaks
            }
            WalkNative(root, p);
        }

        static void WalkNative(Control c, Palette p)
        {
            if (c.IsHandleCreated && c is not Form)
            {
                try
                {
                    SetWindowTheme(c.Handle, p.Dark ? "DarkMode_Explorer" : "Explorer", null);
                }
                catch
                {
                    // theming is cosmetic
                }
            }
            foreach (Control child in c.Controls) WalkNative(child, p);
        }

        /// <summary>Paints a class-heading row of the weapon list (an item whose Tag is null).</summary>
        public static void DrawHeaderRow(DrawListViewItemEventArgs e)
        {
            var item = e.Item;
            if (item == null) return;
            var p = Current;
            var g = e.Graphics;
            var b = e.Bounds;
            using var fill = new SolidBrush(p.HeaderRow);
            using var pen = new Pen(p.Border);
            using var bold = new Font(item.Font, FontStyle.Bold);
            g.FillRectangle(fill, b);
            g.DrawLine(pen, b.Left + 4, b.Bottom - 1, b.Right - 4, b.Bottom - 1);
            TextRenderer.DrawText(g, item.Text, bold, new Rectangle(b.X + 6, b.Y, b.Width - 8, b.Height), p.GroupText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        /// <summary>Paints a ListView column header in the current palette.</summary>
        public static void DrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            var header = e.Header;
            if (header == null)
            {
                e.DrawDefault = true;
                return;
            }
            var p = Current;
            var g = e.Graphics;
            var r = e.Bounds;
            using var fill = new SolidBrush(p.Panel);
            using var pen = new Pen(p.Border);
            g.FillRectangle(fill, r);
            g.DrawLine(pen, r.Right - 1, r.Top, r.Right - 1, r.Bottom);
            g.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                        (header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
            TextRenderer.DrawText(g, header.Text, e.Font ?? SystemFonts.DefaultFont,
                new Rectangle(r.X + 6, r.Y, Math.Max(0, r.Width - 10), r.Height), p.Fore, flags);
        }
    }
}
