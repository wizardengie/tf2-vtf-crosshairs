using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TF2Crosshairs
{
    /// <summary>Game screenshot with the chosen crosshair drawn over its centre.</summary>
    sealed class PreviewPanel : Control
    {
        public Image? Background;
        public Bitmap? Crosshair;
        public int CrosshairSize = 64;
        public int Zoom = 1;

        public PreviewPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(70, 80, 90);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int cx = Width / 2, cy = Height / 2;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (Background != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                int w = Background.Width * Zoom, h = Background.Height * Zoom;
                g.DrawImage(Background, new Rectangle(cx - w / 2, cy - h / 2, w, h));
            }
            if (Crosshair != null)
            {
                int s = Math.Max(1, CrosshairSize * Zoom);
                g.InterpolationMode = s >= Crosshair.Width ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                g.DrawImage(Crosshair, new Rectangle(cx - s / 2, cy - s / 2, s, s));
            }
        }
    }
}
