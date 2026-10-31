using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace TF2Crosshairs
{
    /// <summary>
    /// TF2's own fonts, read from the player's install because they are Valve's and cannot ship in this public repo.
    /// Segoe UI stands in until the game's folder is known.
    /// </summary>
    public static class Tf2Fonts
    {
        const string Fallback = "Segoe UI";

        [DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr font, uint size, IntPtr reserved, out uint fontsAdded);

        // kept for the life of the app: GDI+ reads the font data from this memory whenever it draws
        static readonly List<PrivateFontCollection> collections = [];
        static readonly Dictionary<(bool, float, FontStyle), Font> cache = [];
        static FontFamily? heading, body;

        /// <summary>Loads the fonts from a tf folder; true when that changed how text will look.</summary>
        public static bool Load(string? tfFolder)
        {
            if (tfFolder == null || heading != null) return false;
            heading = Read(Path.Combine(tfFolder, "resource", "tf2build.ttf"));
            body = Read(Path.Combine(tfFolder, "resource", "tf2secondary.ttf"));
            cache.Clear();
            return heading != null || body != null;
        }

        /// <summary>Loaded from memory rather than by path so the app does not lock a file Steam may want to update.</summary>
        static FontFamily? Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] data = File.ReadAllBytes(path);
                var memory = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, memory, data.Length);
                var collection = new PrivateFontCollection();
                collection.AddMemoryFont(memory, data.Length);
                collections.Add(collection);
                AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, out _); // the same font for GDI, so native text boxes can use it
                return collection.Families.Length > 0 ? collection.Families[0] : null;
            }
            catch
            {
                return null; // a damaged or changed font file just means Segoe UI
            }
        }

        public static Font Heading(float points) => Get(true, heading, points, FontStyle.Regular);

        public static Font Body(float points, FontStyle style = FontStyle.Regular) => Get(false, body, points, style);

        static Font Get(bool isHeading, FontFamily? family, float points, FontStyle style)
        {
            var key = (isHeading, points, style);
            if (cache.TryGetValue(key, out var font)) return font;
            if (family == null)
                font = new Font(Fallback, points, isHeading ? FontStyle.Bold : style);
            else
                font = new Font(family, points, family.IsStyleAvailable(style) ? style : FontStyle.Regular);
            cache[key] = font;
            return font;
        }
    }
}
