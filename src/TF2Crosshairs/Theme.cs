using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TF2Crosshairs
{
    /// <summary>The game's menu browns and cream are shared; a team only changes the accent colour.</summary>
    public sealed class Palette
    {
        public string Team = "";
        public Color Accent, AccentText;
        public readonly Color Bg = Color.FromArgb(42, 39, 37);
        public readonly Color Panel = Color.FromArgb(55, 50, 46);
        public readonly Color Input = Color.FromArgb(31, 29, 27);
        public readonly Color Fore = Color.FromArgb(235, 226, 202);
        public readonly Color Muted = Color.FromArgb(201, 189, 166);
        public readonly Color Border = Color.FromArgb(106, 95, 82);
        public readonly Color Button = Color.FromArgb(76, 68, 60);
        public readonly Color ButtonHover = Color.FromArgb(94, 84, 74);
        public readonly Color Danger = Color.FromArgb(240, 154, 138);
        public readonly Color Warn = Color.FromArgb(244, 201, 138);
        public readonly Color WarnBack = Color.FromArgb(74, 47, 30);
    }

    /// <summary>
    /// WinForms has no theming of its own, so this paints one: colours and fonts on every control, plus the
    /// Windows dark-mode window theme for the title bar and scrollbars.
    /// </summary>
    public static class Theme
    {
        const float InputPoints = 10f;

        public static readonly Palette Red = new() { Team = "RED", Accent = Color.FromArgb(169, 55, 55), AccentText = Color.FromArgb(240, 154, 138) };
        public static readonly Palette Blu = new() { Team = "BLU", Accent = Color.FromArgb(57, 98, 125), AccentText = Color.FromArgb(156, 198, 224) };

        public static Palette Current = Red;

        public static Palette ForTeam(string team) => team == Blu.Team ? Blu : Red;

        public static void Apply(Form root, Palette p)
        {
            Current = p;
            ApplyColors(root, p, p.Bg);
            if (root.IsHandleCreated) ApplyNative(root, p);
        }

        static void ApplyColors(Control c, Palette p, Color back)
        {
            switch (c)
            {
                case TextBox tb:
                    tb.BackColor = p.Input;
                    tb.ForeColor = p.Fore;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    tb.Font = Tf2Fonts.Body(InputPoints);
                    break;
                case NumericUpDown n:
                    n.BackColor = p.Input;
                    n.ForeColor = p.Fore;
                    n.BorderStyle = BorderStyle.FixedSingle;
                    n.Font = Tf2Fonts.Body(InputPoints);
                    break;
                case TfComboBox cb:
                    cb.BackColor = p.Input;
                    cb.ForeColor = p.Fore;
                    cb.Font = Tf2Fonts.Body(InputPoints);
                    cb.Invalidate();
                    break;
                case TfButton b:
                    b.BackColor = back;
                    b.ApplyFont();
                    break;
                case TfLabel l:
                    l.BackColor = back;
                    l.ForeColor = l.Role switch { TfTextRole.Muted => p.Muted, TfTextRole.Accent => p.AccentText, _ => p.Fore };
                    l.ApplyFont();
                    break;
                case RaisedPanel:
                    back = p.Panel;
                    c.BackColor = back;
                    break;
                case PreviewPanel or Gallery or WeaponCards:
                    break; // paint themselves from Theme.Current
                default: // the form, the sliders and the layout panels
                    c.BackColor = back;
                    c.ForeColor = p.Fore;
                    break;
            }
            foreach (Control child in c.Controls) ApplyColors(child, p, back);
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
                int v = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on 20H1+, 19 on older 1809-1909 builds.
                if (DwmSetWindowAttribute(root.Handle, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(root.Handle, 19, ref v, 4);
                // DWMWA_CAPTION_COLOR (Windows 11) blends the title bar into the window.
                int caption = ColorTranslator.ToWin32(p.Bg);
                DwmSetWindowAttribute(root.Handle, 35, ref caption, 4);
            }
            catch
            {
                // older Windows: no dark title bar, nothing else breaks
            }
            WalkNative(root);
        }

        static void WalkNative(Control c)
        {
            if (c.IsHandleCreated && c is not Form)
            {
                try
                {
                    SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                }
                catch
                {
                    // theming is cosmetic
                }
            }
            foreach (Control child in c.Controls) WalkNative(child);
        }
    }
}
