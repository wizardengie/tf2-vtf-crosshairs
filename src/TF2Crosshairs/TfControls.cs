using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TF2Crosshairs
{
    /// <summary>Text and shape helpers shared by the hand-painted controls.</summary>
    static class TfDraw
    {
        const float OutlinePixels = 1f;

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
        }

        /// <summary>
        /// Fills the letters as vector shapes, because GDI+ text rendering leaves the TF2 fonts jagged at small sizes
        /// in every mode. Outlined text, which is TF2 Build, sits on a 1px black edge.
        /// </summary>
        public static void Text(Graphics g, string text, Font font, Color color, RectangleF r, StringAlignment align = StringAlignment.Near,
            bool wrap = false, StringAlignment lines = StringAlignment.Center, bool outlined = false)
        {
            using var format = new StringFormat(wrap ? StringFormatFlags.LineLimit : StringFormatFlags.NoWrap)
            {
                Alignment = align, LineAlignment = lines, Trimming = StringTrimming.EllipsisCharacter, HotkeyPrefix = HotkeyPrefix.None,
            };
            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, font.SizeInPoints * g.DpiY / 72f, r, format);
            var offset = g.PixelOffsetMode;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (outlined)
            {
                // a pen straddles the edge and the fill covers its inner half, so twice the width shows outside the letters
                using var pen = new Pen(Color.Black, 2 * OutlinePixels * g.DpiY / 96f) { LineJoin = LineJoin.Round };
                g.DrawPath(pen, path);
            }
            using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
            g.PixelOffsetMode = offset;
        }
    }

    /// <summary>A layout panel on the raised panel colour; Theme gives its children the same background.</summary>
    sealed class RaisedPanel : TableLayoutPanel { }

    enum TfTextRole { Normal, Muted, Accent }

    /// <summary>Label in a TF2 font, drawn through TfDraw. Compatible text rendering keeps its measuring in GDI+ as well.</summary>
    sealed class TfLabel : Label
    {
        public bool IsHeading;
        public float Points = 10f;
        public FontStyle TextStyle = FontStyle.Regular;
        public TfTextRole Role = TfTextRole.Normal;

        public TfLabel()
        {
            UseCompatibleTextRendering = true;
        }

        public void ApplyFont() => Font = IsHeading ? Tf2Fonts.Heading(Points) : Tf2Fonts.Body(Points, TextStyle);

        protected override void OnPaint(PaintEventArgs e)
        {
            TfDraw.Prepare(e.Graphics);
            var area = new RectangleF(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical);
            bool middle = TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight;
            TfDraw.Text(e.Graphics, Text, Font, ForeColor, area, wrap: true, lines: middle ? StringAlignment.Center : StringAlignment.Near, outlined: IsHeading);
        }
    }

    enum TfButtonKind { Normal, Primary, Danger }

    /// <summary>The chunky menu button: a filled face on a dark lip that sinks when pressed. Also serves as the class tabs and the RED/BLU switch.</summary>
    sealed class TfButton : Button
    {
        const int MinHeight = 34, SidePadding = 13;
        const float MinPoints = 8f;

        bool hover, pressed;

        public TfButtonKind Kind = TfButtonKind.Normal;
        public float Points = 11f;
        /// <summary>Selected buttons use this fill, or the team colour when it is null.</summary>
        public Color? SelectedFill;
        public bool Selected
        {
            get;
            set { field = value; Invalidate(); }
        }
        /// <summary>A small second line under the text, for the class tabs.</summary>
        public string? SubText
        {
            get;
            set { field = value; Invalidate(); }
        }

        public TfButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            UseVisualStyleBackColor = false;
        }

        public void ApplyFont() => Font = Tf2Fonts.Heading(Points);

        public override Size GetPreferredSize(Size proposedSize)
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            var text = g.MeasureString(Text.ToUpperInvariant(), Font);
            return new Size((int)Math.Ceiling(text.Width) + LogicalToDeviceUnits(SidePadding) * 2,
                            Math.Max(Math.Max(MinimumSize.Height, LogicalToDeviceUnits(MinHeight)), (int)Math.Ceiling(text.Height) + LogicalToDeviceUnits(4)));
        }

        /// <summary>TF2 Build runs wide, so a button narrower than its text steps the size down instead of cutting it off.</summary>
        Font Fitted(Graphics g, string label)
        {
            float room = Width - LogicalToDeviceUnits(8);
            for (float points = Points; points > MinPoints; points -= 0.5f)
            {
                var font = Tf2Fonts.Heading(points);
                if (g.MeasureString(label, font).Width <= room) return font;
            }
            return Tf2Fonts.Heading(MinPoints);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.Clear(BackColor);
            TfDraw.Prepare(g);
            int lip = LogicalToDeviceUnits(2);
            var corner = new Size(LogicalToDeviceUnits(4), LogicalToDeviceUnits(4));
            var face = new Rectangle(0, pressed ? lip : 0, Width - 1, Height - lip - 1);

            if (Kind == TfButtonKind.Danger)
            {
                using var pen = new Pen(hover ? p.Danger : p.Border);
                g.DrawRoundedRectangle(pen, face, corner);
            }
            else
            {
                if (!pressed)
                {
                    using var shadow = new SolidBrush(Color.FromArgb(120, Color.Black));
                    g.FillRoundedRectangle(shadow, new Rectangle(0, lip, Width - 1, Height - lip - 1), corner);
                }
                bool accent = Selected || Kind == TfButtonKind.Primary;
                Color fill = Selected && SelectedFill is { } own ? own : accent ? p.Accent : p.Button;
                if (hover && Enabled) fill = accent ? ControlPaint.Light(fill, 0.25f) : p.ButtonHover;
                using var brush = new SolidBrush(fill);
                g.FillRoundedRectangle(brush, face, corner);
                using var shine = new Pen(Color.FromArgb(30, Color.White));
                g.DrawLine(shine, face.X + 3, face.Y + 1, face.Right - 3, face.Y + 1);
            }

            Color text = !Enabled ? p.Muted : Kind == TfButtonKind.Danger ? p.Danger : p.Fore;
            string label = Text.ToUpperInvariant();
            if (SubText is { } sub)
            {
                float mid = face.Y + face.Height * 0.60f;
                TfDraw.Text(g, label, Fitted(g, label), text, new RectangleF(0, face.Y, Width, mid - face.Y), StringAlignment.Center, outlined: true);
                TfDraw.Text(g, sub, Tf2Fonts.Body(8.5f), p.Fore, new RectangleF(0, mid - 1, Width, face.Bottom - mid), StringAlignment.Center);
            }
            else
            {
                TfDraw.Text(g, label, Fitted(g, label), text, face, StringAlignment.Center, outlined: true);
            }

            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(p.Fore);
                g.DrawRoundedRectangle(focus, Rectangle.Inflate(face, -2, -2), corner);
            }
        }
    }

    /// <summary>Drop-down painted over the native one, whose flat border and arrow stay in light system colours.</summary>
    sealed class TfComboBox : ComboBox
    {
        const int WM_PAINT = 0x000F;

        public TfComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            ItemHeight = LogicalToDeviceUnits(20);
        }

        RectangleF TextArea(Rectangle r) => new(r.X + LogicalToDeviceUnits(5), r.Y, Math.Max(0, r.Width - LogicalToDeviceUnits(5)), r.Height);

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var p = Theme.Current;
            using var fill = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? p.Accent : p.Input);
            e.Graphics.FillRectangle(fill, e.Bounds);
            TfDraw.Prepare(e.Graphics);
            TfDraw.Text(e.Graphics, GetItemText(Items[e.Index]) ?? "", Font, p.Fore, TextArea(e.Bounds));
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WM_PAINT) return;
            var p = Theme.Current;
            using var g = Graphics.FromHwnd(Handle);
            TfDraw.Prepare(g);
            g.Clear(p.Input);
            int arrow = LogicalToDeviceUnits(18);
            string text = (SelectedIndex >= 0 ? GetItemText(Items[SelectedIndex]) : null) ?? "";
            TfDraw.Text(g, text, Font, Enabled ? p.Fore : p.Muted, TextArea(new Rectangle(0, 0, Width - arrow, Height)));
            using var mark = new SolidBrush(Enabled ? p.Fore : p.Border);
            int cx = Width - arrow / 2, cy = Height / 2, half = LogicalToDeviceUnits(4);
            g.FillPolygon(mark, new Point[] { new(cx - half, cy - half / 2), new(cx + half, cy - half / 2), new(cx, cy + half / 2 + 1) });
            using var border = new Pen(Focused ? p.Fore : p.Border);
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Slider in the team colour; the native TrackBar's thumb cannot be recoloured.</summary>
    sealed class TfSlider : Control
    {
        int value;

        public int Minimum = 8, Maximum = 256;

        /// <summary>Raised for mouse and keyboard changes, not when code sets Value.</summary>
        public event EventHandler? UserChanged;

        public TfSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public int Value
        {
            get => value;
            set
            {
                int v = Math.Clamp(value, Minimum, Maximum);
                if (v == this.value) return;
                this.value = v;
                Invalidate();
            }
        }

        int ThumbWidth => LogicalToDeviceUnits(12);

        void SetByUser(int v)
        {
            int before = Value;
            Value = v;
            if (Value != before) UserChanged?.Invoke(this, EventArgs.Empty);
        }

        void SetFromMouse(int x)
        {
            int range = Math.Max(1, Width - ThumbWidth);
            SetByUser(Minimum + (int)Math.Round((x - ThumbWidth / 2.0) / range * (Maximum - Minimum)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left) SetFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left) SetFromMouse(e.X);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            SetByUser(Value + Math.Sign(e.Delta) * 4);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Left or Keys.Down) SetByUser(Value - 1);
            else if (e.KeyCode is Keys.Right or Keys.Up) SetByUser(Value + 1);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.Clear(BackColor);
            TfDraw.Prepare(g);
            int track = LogicalToDeviceUnits(6), thumbH = Math.Min(Height - 2, LogicalToDeviceUnits(22));
            var corner = new Size(LogicalToDeviceUnits(2), LogicalToDeviceUnits(2));
            int x = (int)((double)(Value - Minimum) / Math.Max(1, Maximum - Minimum) * (Width - ThumbWidth));
            int mid = Height / 2;
            using var rail = new SolidBrush(p.Input);
            using var done = new SolidBrush(p.Accent);
            using var thumb = new SolidBrush(p.Fore);
            g.FillRoundedRectangle(rail, new Rectangle(0, mid - track / 2, Width - 1, track), corner);
            g.FillRoundedRectangle(done, new Rectangle(0, mid - track / 2, x + ThumbWidth / 2, track), corner);
            g.FillRoundedRectangle(thumb, new Rectangle(x, mid - thumbH / 2, ThumbWidth, thumbH), corner);
            if (Focused)
            {
                using var focus = new Pen(p.Accent, 2);
                g.DrawRoundedRectangle(focus, new Rectangle(x + 2, mid - thumbH / 2 + 2, ThumbWidth - 5, thumbH - 5), corner);
            }
        }
    }
}
