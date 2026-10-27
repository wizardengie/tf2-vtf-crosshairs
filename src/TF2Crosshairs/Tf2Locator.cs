using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TF2Crosshairs
{
    /// <summary>Finds the Team Fortress 2 "tf" folder through Steam's registry keys and library list.</summary>
    public static class Tf2Locator
    {
        public static string? FindTfFolder()
        {
            foreach (var steam in SteamRoots())
            {
                var libraries = new List<string> { steam };
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    string text;
                    try { text = File.ReadAllText(vdf); } catch { continue; }
                    foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
                        libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                foreach (var lib in libraries)
                {
                    string tf = Path.Combine(lib, "steamapps", "common", "Team Fortress 2", "tf");
                    if (IsTfFolder(tf)) return Path.GetFullPath(tf);
                }
            }
            return null;
        }

        static IEnumerable<string> SteamRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new[]
            {
                ReadRegistry(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                ReadRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                ReadRegistry(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            };
            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                string p = c!.Replace('/', '\\');
                if (Directory.Exists(p) && seen.Add(p)) yield return p;
            }
        }

        static string? ReadRegistry(RegistryKey hive, string key, string name)
        {
            try
            {
                using (var k = hive.OpenSubKey(key))
                    return k?.GetValue(name) as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Accepts the "tf\custom" folder (where the package is installed), the "tf" folder above it, or the
        /// "Team Fortress 2" folder above that. Returns the "tf" folder.
        /// </summary>
        public static string? NormalizeTfFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string p = path!.Trim().TrimEnd('\\', '/');
            if (IsTfFolder(p)) return Path.GetFullPath(p);
            string child = Path.Combine(p, "tf");
            if (IsTfFolder(child)) return Path.GetFullPath(child);
            if (string.Equals(Path.GetFileName(p), "custom", StringComparison.OrdinalIgnoreCase))
            {
                string? parent = Path.GetDirectoryName(p);
                if (IsTfFolder(parent)) return Path.GetFullPath(parent!);
            }
            return null;
        }

        public static bool IsTfFolder(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path!;
            try
            {
                return File.Exists(Path.Combine(p, "gameinfo.txt")) &&
                       string.Equals(Path.GetFileName(p.TrimEnd('\\', '/')), "tf", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string CustomFolder(string tf) => Path.Combine(tf, "custom");

        /// <summary>Value of cl_crosshair_file saved in cfg/config.cfg, or null when it is not there.</summary>
        public static string? ReadCrosshairFileCvar(string tf)
        {
            try
            {
                string cfg = Path.Combine(tf, "cfg", "config.cfg");
                if (!File.Exists(cfg)) return null;
                var m = Regex.Match(File.ReadAllText(cfg), "^\\s*cl_crosshair_file\\s+\"?([^\"\\r\\n]*)\"?",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
