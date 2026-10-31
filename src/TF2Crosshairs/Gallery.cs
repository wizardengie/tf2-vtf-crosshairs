using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TF2Crosshairs
{
    public sealed class GalleryItem
    {
        public string Name = "";
        public string Group = "";
        public Image Image = null!;
        public object? Tag;
        internal Rectangle Bounds;
    }

    /// <summary>
    /// Scrollable grid of picture-only thumbnails with a heading per group; hovering a tile shows its name.
    /// Painted by hand because a ListView's group headers cannot be recoloured.
    /// </summary>
    public sealed class Gallery : ScrollableControl
    {
        const int TileSize = 64, CellSize = 76, HeadingH = 30, Margin_ = 10;

        readonly List<GalleryItem> items = [];
        readonly List<(string Text, Rectangle Bounds)> headers = [];
        readonly ToolTip tip = new();
        GalleryItem? selected, hover;

        public event EventHandler? SelectionChanged;

        public Gallery()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
            TabStop = true;
        }

        int Tile => LogicalToDeviceUnits(TileSize);
        int CellW => LogicalToDeviceUnits(CellSize);
        int CellH => LogicalToDeviceUnits(CellSize);
        int HeaderH => LogicalToDeviceUnits(HeadingH);
        int Pad => LogicalToDeviceUnits(Margin_);

        public GalleryItem? SelectedItem => selected;

        public void SetItems(IEnumerable<GalleryItem> newItems)
        {
            items.Clear();
            items.AddRange(newItems);
            selected = null;
            hover = null;
            Relayout();
            Invalidate();
        }

        /// <summary>Selects the item with that name (null clears) and raises SelectionChanged if it changed.</summary>
        public void Select(string? name)
        {
            var it = name == null ? null : items.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (ReferenceEquals(it, selected)) return;
            selected = it;
            if (it != null) EnsureVisible(it);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        void EnsureVisible(GalleryItem it)
        {
            int top = -AutoScrollPosition.Y;
            int bottom = top + ClientSize.Height;
            if (it.Bounds.Top < top) AutoScrollPosition = new Point(0, Math.Max(0, it.Bounds.Top - Pad));
            else if (it.Bounds.Bottom > bottom) AutoScrollPosition = new Point(0, it.Bounds.Bottom - ClientSize.Height + Pad);
        }

        void Relayout()
        {
            headers.Clear();
            int cols = Math.Max(1, (ClientSize.Width - Pad) / CellW);
            int y = Pad, col = 0;
            string? group = null;
            foreach (var it in items)
            {
                if (!string.Equals(it.Group, group, StringComparison.Ordinal))
                {
                    if (group != null)
                    {
                        if (col > 0) { y += CellH; col = 0; }
                        y += 8;
                    }
                    headers.Add((it.Group, new Rectangle(Pad, y, Math.Max(100, cols * CellW - Pad), HeaderH)));
                    y += HeaderH;
                    group = it.Group;
                }
                if (col >= cols) { col = 0; y += CellH; }
                it.Bounds = new Rectangle(Pad + col * CellW, y, CellW, CellH);
                col++;
            }
            if (col > 0) y += CellH;
            AutoScrollMinSize = new Size(0, y + Pad);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
            Invalidate();
        }

        GalleryItem? HitTest(Point p)
        {
            int y = p.Y - AutoScrollPosition.Y;
            return items.FirstOrDefault(i => i.Bounds.Contains(p.X, y));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitTest(e.Location);
            if (!ReferenceEquals(h, hover))
            {
                hover = h;
                tip.SetToolTip(this, h == null ? "" : h.Name + " (" + h.Image.Width + "x" + h.Image.Height + ")");
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            var h = HitTest(e.Location);
            if (h != null) Select(h.Name);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.Clear(p.Panel);
            TfDraw.Prepare(g);
            // scroll by offsetting the rectangles rather than a Graphics transform, to keep the text pixel-aligned
            int oy = AutoScrollPosition.Y;
            int top = -oy, bottom = top + ClientSize.Height;
            bool OnScreen(Rectangle r) => r.Bottom >= top && r.Top <= bottom;

            using (var line = new Pen(p.Border))
            {
                foreach (var (text, bounds) in headers)
                {
                    if (!OnScreen(bounds)) continue;
                    var r = bounds;
                    r.Offset(0, oy);
                    TfDraw.Text(g, text, Tf2Fonts.Body(10f, FontStyle.Bold), p.Fore, r);
                    g.DrawLine(line, r.Left, r.Bottom - 5, r.Right, r.Bottom - 5);
                }
            }

            int inset = (CellW - Tile) / 2;
            foreach (var it in items)
            {
                if (!OnScreen(it.Bounds)) continue;
                var b = it.Bounds;
                b.Offset(0, oy);
                bool sel = ReferenceEquals(it, selected);
                if (sel || ReferenceEquals(it, hover))
                {
                    using var fill = new SolidBrush(sel ? p.Accent : p.ButtonHover);
                    g.FillRectangle(fill, b);
                }
                var tile = new Rectangle(b.X + inset, b.Y + inset, Tile, Tile);
                g.DrawImage(it.Image, tile);
                if (sel)
                {
                    using var pen = new Pen(p.Fore, 2);
                    g.DrawRectangle(pen, tile.X - 1, tile.Y - 1, tile.Width + 1, tile.Height + 1);
                }
            }
        }
    }
}
