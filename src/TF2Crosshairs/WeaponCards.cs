using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TF2Crosshairs
{
    public sealed class WeaponCard
    {
        public string Title = "";
        public string Also = "";
        public string Info = "";
        public string ToolTip = "";
        public Image? Thumb;     // null while the weapon keeps its stock crosshair
        public object? Tag;
        internal Rectangle Bounds;
    }

    /// <summary>
    /// Scrollable column of loadout-style weapon cards. Click selects one, Ctrl-click toggles, Shift-click takes a
    /// range; the arrow keys and Ctrl+A work once it has focus.
    /// </summary>
    public sealed class WeaponCards : ScrollableControl
    {
        const int CardH = 76, Gap = 4, TileSize = 56, Inset = 10;

        readonly List<WeaponCard> items = [];
        readonly HashSet<WeaponCard> selected = [];
        readonly ToolTip tip = new();
        WeaponCard? hover, anchor;

        public event EventHandler? SelectionChanged;

        public WeaponCards()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
            TabStop = true;
        }

        public IReadOnlyList<WeaponCard> Items => items;

        /// <summary>The selected cards, top to bottom.</summary>
        public List<WeaponCard> SelectedItems => items.Where(selected.Contains).ToList();

        /// <summary>Replaces the list and clears the selection without raising SelectionChanged.</summary>
        public void SetItems(IEnumerable<WeaponCard> newItems)
        {
            items.Clear();
            items.AddRange(newItems);
            selected.Clear();
            hover = anchor = null;
            AutoScrollPosition = Point.Empty;
            Relayout();
            Invalidate();
        }

        public void SelectFirst()
        {
            if (items.Count > 0) SelectOnly(items[0]);
            else SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SelectAll()
        {
            selected.UnionWith(items);
            anchor = items.FirstOrDefault();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        void SelectOnly(WeaponCard card)
        {
            selected.Clear();
            selected.Add(card);
            anchor = card;
            EnsureVisible(card);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        void SelectRange(WeaponCard card)
        {
            int from = anchor != null ? items.IndexOf(anchor) : 0, to = items.IndexOf(card);
            selected.Clear();
            selected.UnionWith(items.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1));
            EnsureVisible(card);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        void Toggle(WeaponCard card)
        {
            if (!selected.Remove(card)) selected.Add(card);
            anchor = card;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        void EnsureVisible(WeaponCard card)
        {
            int top = -AutoScrollPosition.Y;
            int bottom = top + ClientSize.Height;
            if (card.Bounds.Top < top) AutoScrollPosition = new Point(0, card.Bounds.Top);
            else if (card.Bounds.Bottom > bottom) AutoScrollPosition = new Point(0, card.Bounds.Bottom - ClientSize.Height);
        }

        void Relayout()
        {
            int cardH = LogicalToDeviceUnits(CardH), gap = LogicalToDeviceUnits(Gap);
            int y = 0;
            foreach (var card in items)
            {
                card.Bounds = new Rectangle(0, y, ClientSize.Width, cardH);
                y += cardH + gap;
            }
            AutoScrollMinSize = new Size(0, Math.Max(0, y - gap));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
            Invalidate();
        }

        WeaponCard? HitTest(Point p)
        {
            int y = p.Y - AutoScrollPosition.Y;
            return items.FirstOrDefault(c => c.Bounds.Contains(p.X, y));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitTest(e.Location);
            if (!ReferenceEquals(h, hover))
            {
                hover = h;
                tip.SetToolTip(this, h?.ToolTip ?? "");
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
            if (e.Button != MouseButtons.Left || HitTest(e.Location) is not { } card) return;
            if ((ModifierKeys & Keys.Shift) != 0) SelectRange(card);
            else if ((ModifierKeys & Keys.Control) != 0) Toggle(card);
            else SelectOnly(card);
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & ~Keys.Shift) is Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAll();
                e.Handled = true;
            }
            else if (e.KeyCode is Keys.Up or Keys.Down && items.Count > 0)
            {
                int current = anchor != null ? items.IndexOf(anchor) : -1;
                var next = items[Math.Clamp(current + (e.KeyCode == Keys.Down ? 1 : -1), 0, items.Count - 1)];
                if (e.Shift) SelectRangeFromKeyboard(next);
                else SelectOnly(next);
                e.Handled = true;
            }
        }

        /// <summary>Shift+arrow grows the selection one card at a time and moves on from there.</summary>
        void SelectRangeFromKeyboard(WeaponCard next)
        {
            selected.Add(next);
            anchor = next;
            EnsureVisible(next);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.Clear(p.Bg);
            TfDraw.Prepare(g);
            int oy = AutoScrollPosition.Y;
            int inset = LogicalToDeviceUnits(Inset), tile = LogicalToDeviceUnits(TileSize);
            var corner = new Size(LogicalToDeviceUnits(4), LogicalToDeviceUnits(4));

            foreach (var card in items)
            {
                var b = card.Bounds;
                b.Offset(0, oy);
                if (b.Bottom < 0 || b.Top > ClientSize.Height) continue;
                bool on = selected.Contains(card);
                using (var fill = new SolidBrush(on ? p.Accent : ReferenceEquals(card, hover) ? p.ButtonHover : p.Panel))
                    g.FillRoundedRectangle(fill, new Rectangle(b.X, b.Y, b.Width - 1, b.Height - 1), corner);

                var thumb = new Rectangle(b.X + inset, b.Y + (b.Height - tile) / 2, tile, tile);
                if (card.Thumb != null)
                {
                    g.DrawImage(card.Thumb, thumb);
                }
                else
                {
                    using var empty = new SolidBrush(p.Bg);
                    g.FillRectangle(empty, thumb);
                }

                float x = thumb.Right + inset, w = b.Right - inset - x;
                float titleH = b.Height * 0.40f, lineH = b.Height * 0.24f, y = b.Y + b.Height * 0.06f;
                TfDraw.Text(g, card.Title.ToUpperInvariant(), Tf2Fonts.Heading(12.5f), p.Fore, new RectangleF(x, y, w, titleH));
                TfDraw.Text(g, card.Also, Tf2Fonts.Body(9f), on ? p.Fore : p.Muted, new RectangleF(x, y + titleH, w, lineH));
                TfDraw.Text(g, card.Info, Tf2Fonts.Body(9.5f, FontStyle.Bold), card.Thumb != null || on ? p.Fore : p.Muted, new RectangleF(x, y + titleH + lineH, w, lineH));
            }

            if (Focused && ShowFocusCues && anchor != null)
            {
                var b = anchor.Bounds;
                b.Offset(0, oy);
                using var pen = new Pen(p.Fore);
                g.DrawRoundedRectangle(pen, new Rectangle(b.X + 1, b.Y + 1, b.Width - 3, b.Height - 3), corner);
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
