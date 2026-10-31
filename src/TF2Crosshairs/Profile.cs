using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TF2Crosshairs
{
    /// <summary>What the user set up last time, saved under %APPDATA%\TF2Crosshairs so it survives restarts.</summary>
    public sealed class Profile
    {
        public string? TfFolder;
        public string Team = Theme.Red.Team;
        public readonly List<Assignment> Assignments = [];
        /// <summary>Custom crosshairs the user imported: name -> original .vtf path.</summary>
        public readonly List<KeyValuePair<string, string>> CustomCrosshairs = [];

        /// <summary>%APPDATA%\TF2Crosshairs, which also gets the crash log.</summary>
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TF2Crosshairs");

        public static string DefaultPath => Path.Combine(Folder, "profile.txt");

        public static Profile Load(string path)
        {
            var p = new Profile();
            if (!File.Exists(path)) return p;
            foreach (var raw in File.ReadAllLines(path))
            {
                var parts = raw.Split('\t');
                switch (parts[0])
                {
                    case "tf":
                        if (parts.Length > 1) p.TfFolder = parts[1];
                        break;
                    case "team":
                        if (parts.Length > 1) p.Team = parts[1];
                        break;
                    case "custom":
                        if (parts.Length > 2) p.CustomCrosshairs.Add(new(parts[1], parts[2]));
                        break;
                    case "weapon":
                        if (parts.Length > 2)
                        {
                            var a = new Assignment { Script = parts[1], Crosshair = parts[2] };
                            if (parts.Length > 3 && int.TryParse(parts[3], out int size) && size > 0) a.Size = size;
                            if (parts.Length > 4 && ExplosionEffects.IsKnown(parts[4])) a.Explosion = parts[4];
                            p.Assignments.Add(a);
                        }
                        break;
                }
            }
            return p;
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(TfFolder)) sb.Append("tf\t").Append(TfFolder).Append("\r\n");
            sb.Append("team\t").Append(Team).Append("\r\n");
            foreach (var c in CustomCrosshairs) sb.Append("custom\t").Append(c.Key).Append('\t').Append(c.Value).Append("\r\n");
            foreach (var a in Assignments) sb.Append("weapon\t").Append(a.ToTsv()).Append("\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
