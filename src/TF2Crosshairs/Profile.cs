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
        public bool Dark = true;
        public readonly List<Assignment> Assignments = new List<Assignment>();
        /// <summary>Custom crosshairs the user imported: name -> original .vtf path.</summary>
        public readonly List<KeyValuePair<string, string>> CustomCrosshairs = new List<KeyValuePair<string, string>>();

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TF2Crosshairs", "profile.txt");

        public static Profile Load(string path)
        {
            var p = new Profile();
            if (!File.Exists(path)) return p;
            foreach (var raw in File.ReadAllLines(path))
            {
                var parts = raw.Split('\t');
                if (parts.Length == 0) continue;
                switch (parts[0])
                {
                    case "tf":
                        if (parts.Length > 1) p.TfFolder = parts[1];
                        break;
                    case "dark":
                        if (parts.Length > 1) p.Dark = parts[1] != "0";
                        break;
                    case "custom":
                        if (parts.Length > 2) p.CustomCrosshairs.Add(new KeyValuePair<string, string>(parts[1], parts[2]));
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
            sb.Append("dark\t").Append(Dark ? "1" : "0").Append("\r\n");
            foreach (var c in CustomCrosshairs) sb.Append("custom\t").Append(c.Key).Append('\t').Append(c.Value).Append("\r\n");
            foreach (var a in Assignments)
                sb.Append("weapon\t").Append(a.Script).Append('\t').Append(a.Crosshair).Append('\t')
                  .Append(a.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\t').Append(a.Explosion).Append("\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
