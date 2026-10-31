using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TF2Crosshairs
{
    /// <summary>
    /// The "stock look, outlined" preset: every weapon gets an outlined copy of the crosshair it draws by default
    /// (with cl_crosshair_file set to None), so the game looks the same as stock but with a black outline.
    /// </summary>
    public static class StockOutlinePreset
    {
        public const string Label = "Preset: stock, outlined";

        /// <summary>
        /// The regions of materials/sprites/crosshairs the stock scripts point at, and the bundled outlined copy of each.
        /// The copies centre the sprite in a canvas twice its size (so the outline has room), which is why the drawn
        /// size is twice the sprite's: with the default cl_crosshair_scale that is pixel-for-pixel the stock size.
        /// </summary>
        static readonly Dictionary<(int X, int Y, int W, int H), (string Crosshair, int Size)> Regions = new()
        {
            [(0, 0, 32, 32)] = ("tf2_default_bracketsOL", 64),     // shotguns, pistols, SMGs, scattergun, syringe gun...
            [(64, 0, 32, 32)] = ("tf2_default_plusOL", 64),        // sniper rifles, knife, revolver, bonesaw, fire axe
            [(32, 32, 32, 32)] = ("tf2_default_circleOL", 64),     // rocket/grenade launchers, flamethrower, most melee
            [(0, 64, 32, 32)] = ("tf2_default_mediccrossOL", 64),  // medigun
            [(64, 64, 64, 64)] = ("tf2_default_bigcircleOL", 128), // minigun
            // (0, 48, 24, 24) is the blank region PDAs, the sapper and lunchboxes use: those weapons draw nothing and are left stock.
        };

        static readonly Regex Block = new Regex(@"""crosshair""\s*\{([^{}]*)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The outlined crosshair and size for a weapon script, or false if the weapon has no stock crosshair.</summary>
        public static bool TryGet(string scriptText, out string crosshair, out int size)
        {
            crosshair = ""; size = 0;
            var m = Block.Match(scriptText);
            if (!m.Success) return false;
            string body = m.Groups[1].Value;
            if (!Field(body, "file").Replace('\\', '/').Equals("sprites/crosshairs", StringComparison.OrdinalIgnoreCase)) return false;
            if (!int.TryParse(Field(body, "x"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(Field(body, "y"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                !int.TryParse(Field(body, "width"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) ||
                !int.TryParse(Field(body, "height"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
                return false;
            if (!Regions.TryGetValue((x, y, w, h), out var hit)) return false;
            (crosshair, size) = hit;
            return true;
        }

        static string Field(string body, string key)
        {
            var m = Regex.Match(body, "\"" + key + @"""\s*""?([^""\s]+)""?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}
