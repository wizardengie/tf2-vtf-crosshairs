using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace TF2Crosshairs
{
    /// <summary>Access to the crosshairs, weapon scripts and catalog files embedded in the exe.</summary>
    public static class Assets
    {
        static readonly Assembly Asm = typeof(Assets).Assembly;
        static readonly string[] Names = Asm.GetManifestResourceNames();

        public static byte[] Bytes(string logicalName)
        {
            using var s = Asm.GetManifestResourceStream(logicalName) ?? throw new FileNotFoundException("Missing embedded resource: " + logicalName);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        public static string Text(string logicalName) => new UTF8Encoding(false).GetString(Bytes(logicalName));

        /// <summary>The tab-separated fields of each line, skipping blank lines and # comments.</summary>
        public static IEnumerable<string[]> TsvRows(string text) =>
            text.Split('\n').Select(line => line.TrimEnd('\r'))
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(line => line.Split('\t'));

        public static IEnumerable<string> CrosshairNames() =>
            Names.Where(n => n.StartsWith("crosshairs/", StringComparison.Ordinal) && n.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase))
                 .Select(n => n["crosshairs/".Length..^".vtf".Length])
                 .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        public static byte[] Crosshair(string name) => Bytes("crosshairs/" + name + ".vtf");

        public static bool HasScript(string script) => Names.Contains("scripts/" + script + ".txt");

        public static string Script(string script) => Text("scripts/" + script + ".txt");

        /// <summary>The window/taskbar icon. ApplicationIcon only stamps the exe file; a Form shows the default icon until told otherwise.</summary>
        public static Icon AppIcon() => new(new MemoryStream(Bytes("app.ico")));

        public static Image PreviewBackground() => Image.FromStream(new MemoryStream(Bytes("preview_bg.jpg")));
    }
}
