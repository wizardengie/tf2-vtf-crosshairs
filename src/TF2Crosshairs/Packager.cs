using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TF2Crosshairs
{
    /// <summary>One weapon script and the crosshair it should get.</summary>
    public sealed class Assignment
    {
        public string Script = "";     // e.g. tf_weapon_scattergun
        public string Crosshair = "";  // VTF name without extension
        public int Size = 64;          // drawn width/height in the weapon script
        public string Explosion = "";  // key from ExplosionEffects.Options; "" keeps the stock effect

        public Assignment Clone() => new() { Script = Script, Crosshair = Crosshair, Size = Size, Explosion = Explosion };

        /// <summary>"script, crosshair, size, explosion" joined by tabs, as stored in the manifest and the profile.</summary>
        public string ToTsv() => string.Join("\t", Script, Crosshair, Size.ToString(CultureInfo.InvariantCulture), Explosion);
    }

    /// <summary>The "no explosion smoke" particle swaps cfg.tf offers for explosive weapons.</summary>
    public static class ExplosionEffects
    {
        public static readonly KeyValuePair<string, string>[] Options =
        [
            new("", "Default explosion"),
            new("sapper", "Spy sapper (no smoke)"),
            new("electric", "Electric shock (no smoke)"),
            new("muzzle", "Muzzle flash (no smoke)"),
            new("pyropool", "Pyro pool (no smoke)"),
        ];

        public static string Label(string key) => Options.FirstOrDefault(o => o.Key == key).Value ?? key;

        public static bool IsKnown(string key) => Options.Any(o => o.Key == key);

        /// <summary>Particle names for the wall / player / underwater explosion of a weapon script.</summary>
        public static bool TryGetParticles(string key, string script, out string wall, out string player, out string water)
        {
            wall = player = water = "";
            switch (key)
            {
                case "sapper":
                    wall = player = water = "ExplosionCore_sapperdestroyed";
                    return true;
                case "electric":
                    wall = water = "electrocuted_red_flash";
                    player = "electrocuted_blue_flash";
                    return true;
                case "muzzle":
                    wall = player = water = "muzzle_minigun_starflash01";
                    return true;
                case "pyropool":
                    // cfg.tf gives the Cow Mangler a muzzle flash here instead of the pool effect.
                    if (script.Equals("tf_weapon_particle_cannon", StringComparison.OrdinalIgnoreCase))
                        wall = player = water = "muzzle_minigun_starflash01";
                    else
                        wall = player = water = "eotl_pyro_pool_explosion_flash";
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Turns assignments into the files TF2 expects inside tf/custom.</summary>
    public static class Packager
    {
        /// <summary>Folder created inside tf/custom (same name cfg.tf uses, so this package replaces theirs).</summary>
        public const string FolderName = "crosshairs";
        /// <summary>Where the VTF/VMT pairs live. This folder is not checked by sv_pure, which is why crosshair packs use it.</summary>
        public const string MaterialDir = "materials/vgui/replay/thumbnails";
        public const string ManifestName = "tf2crosshairs.txt";

        static readonly UTF8Encoding Utf8NoBom = new(false);
        static readonly Regex CrosshairBlock = new Regex("\"crosshair\"\\s*\\{[^{}]*\\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex ExplosionLine = new Regex("\"(Explosion(?:Player|Water)?Effect)\"\\s+\"[^\"]*\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex StockExplosion = new Regex("\"ExplosionEffect\"\\s+\"ExplosionCore", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>True for weapons whose stock explosion has the smoke cloud the "no smoke" effects replace.</summary>
        public static bool IsExplosive(string scriptText) => StockExplosion.IsMatch(scriptText);

        /// <summary>Rewrites the crosshair block (and optionally the explosion particles) of a stock weapon script.</summary>
        public static string PatchScript(string scriptText, Assignment a)
        {
            if (!CrosshairBlock.IsMatch(scriptText))
                throw new InvalidDataException(a.Script + " has no crosshair block to replace.");

            string nl = scriptText.Contains("\r\n") ? "\r\n" : "\n";
            string size = a.Size.ToString(CultureInfo.InvariantCulture);
            string block =
                "\"crosshair\"" + nl +
                "\t\t{" + nl +
                "\t\t\t\t\"file\"\t\t\"vgui/replay/thumbnails/" + a.Crosshair + "\"" + nl +
                "\t\t\t\t\"x\"\t\t\"0\"" + nl +
                "\t\t\t\t\"y\"\t\t\"0\"" + nl +
                "\t\t\t\t\"width\"\t\t\"" + size + "\"" + nl +
                "\t\t\t\t\"height\"\t\"" + size + "\"" + nl +
                "\t\t}";
            string result = CrosshairBlock.Replace(scriptText, m => block, 1);

            if (!string.IsNullOrEmpty(a.Explosion) &&
                ExplosionEffects.TryGetParticles(a.Explosion, a.Script, out var wall, out var player, out var water))
            {
                result = ExplosionLine.Replace(result, m =>
                {
                    string key = m.Groups[1].Value;
                    string value = key.Equals("ExplosionPlayerEffect", StringComparison.OrdinalIgnoreCase) ? player
                                 : key.Equals("ExplosionWaterEffect", StringComparison.OrdinalIgnoreCase) ? water
                                 : wall;
                    return "\"" + key + "\"\t\t\"" + value + "\"";
                });
            }
            return result;
        }

        public static string MakeVmt(string name) =>
            "\"UnlitGeneric\"\r\n{\r\n" +
            "\t\"$translucent\"\t1\r\n" +
            "\t\"$basetexture\" \"vgui\\replay\\thumbnails\\" + name + "\"\r\n" +
            "\t\"$vertexcolor\" 1\r\n" +
            "\t\"$no_fullbright\" 1\r\n" +
            "\t\"$ignorez\" 1\r\n" +
            "}\r\n";

        /// <summary>
        /// Builds every file of the package, keyed by path relative to tf/custom (forward slashes).
        /// Only crosshairs that are actually assigned are included.
        /// </summary>
        public static SortedDictionary<string, byte[]> BuildFiles(IEnumerable<Assignment> assignments,
            Func<string, byte[]> vtfLookup, Func<string, string> scriptLookup)
        {
            var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var included = new List<Assignment>();
            foreach (var a in assignments)
            {
                if (string.IsNullOrEmpty(a.Crosshair) || string.IsNullOrEmpty(a.Script)) continue;
                string patched = PatchScript(scriptLookup(a.Script), a);
                files[FolderName + "/scripts/" + a.Script + ".txt"] = Utf8NoBom.GetBytes(patched);
                if (used.Add(a.Crosshair))
                {
                    string material = FolderName + "/" + MaterialDir + "/" + a.Crosshair;
                    files[material + ".vtf"] = vtfLookup(a.Crosshair);
                    files[material + ".vmt"] = Encoding.ASCII.GetBytes(MakeVmt(a.Crosshair));
                }
                included.Add(a);
            }
            files[FolderName + "/" + ManifestName] = Utf8NoBom.GetBytes(BuildManifest(included));
            return files;
        }

        public static void WriteZip(IDictionary<string, byte[]> files, string zipPath)
        {
            using var fs = File.Create(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var kv in files)
            {
                using var s = zip.CreateEntry(kv.Key, CompressionLevel.Optimal).Open();
                s.Write(kv.Value, 0, kv.Value.Length);
            }
        }

        /// <summary>tf/custom/crosshairs, the folder the package is installed into.</summary>
        public static string InstallFolder(string customDir) => Path.Combine(customDir, FolderName);

        public static string ManifestPath(string customDir) => Path.Combine(InstallFolder(customDir), ManifestName);

        /// <summary>True when tf/custom/crosshairs exists and was written by this app.</summary>
        public static bool OwnsInstallFolder(string customDir) => File.Exists(ManifestPath(customDir));

        /// <summary>
        /// Writes the package into tf/custom. Weapon scripts and crosshair materials from a previous
        /// run are removed first so an unassigned weapon really goes back to its stock crosshair.
        /// </summary>
        public static void WriteToCustom(IDictionary<string, byte[]> files, string customDir)
        {
            foreach (var sub in new[] { "scripts", MaterialDir })
            {
                string dir = Path.Combine(InstallFolder(customDir), NativePath(sub));
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir))
                    if (Path.GetExtension(f).ToLowerInvariant() is ".txt" or ".vtf" or ".vmt") File.Delete(f);
            }
            foreach (var kv in files)
            {
                string path = Path.Combine(customDir, NativePath(kv.Key));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, kv.Value);
            }
        }

        static string NativePath(string packagePath) => packagePath.Replace('/', Path.DirectorySeparatorChar);

        // ---- manifest: lets the app reload what is installed ----

        public static string BuildManifest(IEnumerable<Assignment> assignments)
        {
            var sb = new StringBuilder();
            sb.Append("# Written by TF2 Weapon Crosshairs. One line per weapon: script, crosshair, size, explosion effect.\r\n");
            foreach (var a in assignments) sb.Append(a.ToTsv()).Append("\r\n");
            return sb.ToString();
        }

        public static List<Assignment> ParseManifest(string text)
        {
            var list = new List<Assignment>();
            foreach (var parts in Assets.TsvRows(text))
            {
                if (parts.Length < 2) continue;
                var a = new Assignment { Script = parts[0].Trim(), Crosshair = parts[1].Trim() };
                if (parts.Length > 2 && int.TryParse(parts[2], out int size) && size > 0) a.Size = size;
                if (parts.Length > 3 && ExplosionEffects.IsKnown(parts[3].Trim())) a.Explosion = parts[3].Trim();
                if (a.Script.Length > 0 && a.Crosshair.Length > 0) list.Add(a);
            }
            return list;
        }
    }
}
