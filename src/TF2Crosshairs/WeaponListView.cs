using System;
using System.Windows.Forms;

namespace TF2Crosshairs
{
    /// <summary>
    /// ListView whose class heading rows (items with a null Tag) act as "select the whole class" buttons.
    /// The click is handled before the native control sees it, otherwise the control's own click
    /// processing would undo the selection.
    /// </summary>
    class WeaponListView : ListView
    {
        const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONDBLCLK = 0x0203;

        public event Action<ListViewItem, bool>? HeadingClicked;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg is WM_LBUTTONDOWN or WM_LBUTTONDBLCLK)
            {
                long lp = (long)m.LParam;
                var hit = GetItemAt((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                if (hit is { Tag: null })
                {
                    Focus();
                    HeadingClicked?.Invoke(hit, (ModifierKeys & (Keys.Control | Keys.Shift)) != 0);
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }
}
