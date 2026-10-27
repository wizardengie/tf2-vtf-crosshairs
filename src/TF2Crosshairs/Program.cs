using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TF2Crosshairs
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Developer helper: "TF2Crosshairs.exe --dump-png <folder>" writes every bundled crosshair as PNG
            // plus a _sheet.png contact sheet, to eyeball the VTF decoder.
            if (args.Length == 2 && args[0] == "--dump-png")
            {
                DumpPng(args[1]);
                return 0;
            }

            ApplicationConfiguration.Initialize(); // DPI mode, visual styles and default font from the project file
            Application.ThreadException += (s, e) => ReportCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(e.ExceptionObject as Exception);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ReportCrash(ex);
                return 1;
            }
            return 0;
        }

        /// <summary>Writes %APPDATA%\TF2Crosshairs\crash.log and shows the error instead of dying silently.</summary>
        static void ReportCrash(Exception? ex)
        {
            string text = ex?.ToString() ?? "Unknown error";
            string logPath = "";
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TF2Crosshairs");
                Directory.CreateDirectory(dir);
                logPath = Path.Combine(dir, "crash.log");
                File.WriteAllText(logPath, DateTime.Now.ToString("u") + "\r\n" + text);
            }
            catch
            {
                // nothing more we can do
            }
            MessageBox.Show("TF2 Weapon Crosshairs ran into an error:\n\n" + text +
                (logPath.Length > 0 ? "\n\nThis was also written to " + logPath : ""),
                "TF2 Weapon Crosshairs", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static void DumpPng(string dir)
        {
            Directory.CreateDirectory(dir);
            var names = Assets.CrosshairNames().ToList();
            const int cols = 10, cell = 96;
            int rows = (names.Count + cols - 1) / cols;
            using (var sheet = new Bitmap(cols * cell, Math.Max(1, rows) * cell))
            using (var g = Graphics.FromImage(sheet))
            using (var font = new Font("Segoe UI", 7f))
            {
                g.Clear(Color.FromArgb(96, 104, 112));
                for (int i = 0; i < names.Count; i++)
                {
                    using (var bmp = Vtf.Decode(Assets.Crosshair(names[i])))
                    {
                        bmp.Save(Path.Combine(dir, names[i] + ".png"), ImageFormat.Png);
                        int x = (i % cols) * cell, y = (i / cols) * cell;
                        g.DrawImage(bmp, x + (cell - bmp.Width) / 2, y + 6 + (64 - bmp.Height) / 2, bmp.Width, bmp.Height);
                        g.DrawString(names[i], font, Brushes.White, x + 2, y + cell - 16);
                    }
                }
                sheet.Save(Path.Combine(dir, "_sheet.png"), ImageFormat.Png);
            }
        }
    }
}
