using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TF2Crosshairs
{
    sealed class WeaponInfo
    {
        public string Group = "";
        public string Script = "";
        public string Names = "";
        public bool Explosive;
    }

    sealed class CrosshairInfo
    {
        public string Name = "";
        public string Pack = "";
        public byte[] Vtf = [];
        public Bitmap Image = null!;
        public string? SourcePath;   // set for crosshairs the user imported
    }

    public sealed class MainForm : Form
    {
        static readonly string[] GroupOrder = ["Scout", "Soldier", "Pyro", "Demoman", "Heavy", "Engineer", "Medic", "Sniper", "Spy", "All classes", "Custom"];
        const int Cell = 64;

        readonly List<WeaponInfo> weapons = [];
        readonly List<CrosshairInfo> crosshairs = [];
        readonly Dictionary<string, Assignment> assignments = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Image> thumbs = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, TfButton> classTabs = [];
        readonly Profile profile;
        string? tfFolder;
        string currentClass = "";
        bool updatingUi;

        readonly WeaponCards cards = new() { Dock = DockStyle.Fill };
        readonly TfLabel classLabel = new() { IsHeading = true, Points = 14f, Role = TfTextRole.Accent, AutoSize = true, Anchor = AnchorStyles.Left };
        readonly TextBox filterBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Search", Margin = new Padding(8, 5, 8, 0) };
        readonly Gallery gallery = new() { Dock = DockStyle.Fill };
        readonly PreviewPanel preview = new() { Dock = DockStyle.Fill };
        readonly TfSlider sizeSlider = new() { Dock = DockStyle.Fill, Value = 64 };
        readonly NumericUpDown sizeBox = new() { Minimum = 8, Maximum = 512, Value = 64, Width = 64 };
        readonly TfComboBox explosionBox = new() { Dock = DockStyle.Fill };
        readonly TfComboBox zoomBox = new() { Width = 60 };
        readonly TfLabel selectedLabel = new() { TextStyle = FontStyle.Bold, Points = 11f, AutoSize = false, Dock = DockStyle.Fill };
        readonly TextBox tfBox = new() { ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 8, 0) };
        readonly TfLabel warnLabel = new() { AutoSize = true, Dock = DockStyle.Fill, Visible = false, Padding = new Padding(8, 6, 8, 6), Margin = new Padding(0, 0, 0, 6) };
        readonly TfLabel statusLabel = new() { Role = TfTextRole.Muted, AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(10, 0, 10, 0) };
        readonly TfButton redButton = new() { Text = "RED", Width = 58, Margin = new Padding(0), SelectedFill = Theme.Red.Accent };
        readonly TfButton bluButton = new() { Text = "BLU", Width = 58, Margin = new Padding(0), SelectedFill = Theme.Blu.Accent };

        public MainForm()
        {
            Text = "TF2 Weapon Crosshairs";
            Icon = Assets.AppIcon();
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            var screen = Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(S(1300), S(760));
            ClientSize = new Size(Math.Min(S(1300), screen.Width), Math.Min(S(760), screen.Height - S(40)));
            MinimumSize = new Size(S(1000), S(640));

            profile = SafeLoadProfile();
            LoadWeapons();
            currentClass = weapons.Select(w => w.Group).OrderBy(GroupRank).First();
            BuildLayout();
            LoadBundledCrosshairs();
            LoadCustomCrosshairs();
            PopulateGallery();
            RestoreState();
            PopulateWeapons();
            ApplyTheme(Theme.ForTeam(profile.Team));
            Shown += (s, e) =>
            {
                Theme.ApplyNative(this, Theme.Current); // the child controls have their handles by now
                cards.SelectFirst();
                cards.Focus();
            };
            FormClosing += (s, e) => SaveProfile();
        }

        /// <summary>A size in 96 DPI pixels, scaled for this screen.</summary>
        int S(int pixels) => LogicalToDeviceUnits(pixels);

        void ApplyTheme(Palette palette)
        {
            Theme.Apply(this, palette);
            redButton.Selected = palette == Theme.Red;
            bluButton.Selected = palette == Theme.Blu;
            UpdateTfFolderUi();
            Invalidate(true);
        }

        void SetTeam(Palette palette)
        {
            profile.Team = palette.Team;
            ApplyTheme(palette);
            SaveProfile();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyNative(this, Theme.Current);
        }

        // ------------------------------------------------------------------ layout

        void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(S(12), S(10), S(12), S(10)) };
            AddColumns(root, SizeType.Percent);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(62)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(BuildTopBar(), 0, 0);
            root.Controls.Add(warnLabel, 0, 1);
            root.Controls.Add(BuildClassTabs(), 0, 2);
            root.Controls.Add(BuildMainArea(), 0, 3);
            root.Controls.Add(BuildBottomBar(), 0, 4);
            Controls.Add(root);
        }

        /// <summary>Title, install folder, help and the RED/BLU switch.</summary>
        Control BuildTopBar()
        {
            var top = new TableLayoutPanel { AutoSize = true, ColumnCount = 6, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, S(6)) };
            AddColumns(top, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize);
            var title = new TfLabel { Text = "TF2 WEAPON CROSSHAIRS", IsHeading = true, Points = 21f, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, S(14), 0) };

            redButton.Click += (s, e) => SetTeam(Theme.Red);
            bluButton.Click += (s, e) => SetTeam(Theme.Blu);
            redButton.AutoSize = bluButton.AutoSize = false;
            redButton.Size = bluButton.Size = new Size(S(58), S(34));
            var team = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(S(8), 0, 0, 0) };
            team.Controls.Add(redButton);
            team.Controls.Add(bluButton);

            AddCells(top, 0, title, Caption("Install folder"), tfBox, MakeButton("Browse", BrowseTfFolder), MakeButton("How it works", ShowHelp), team);
            return top;
        }

        /// <summary>One tab per class, each counting the weapons that have a crosshair.</summary>
        Control BuildClassTabs()
        {
            var groups = weapons.Select(w => w.Group).Distinct().OrderBy(GroupRank).ToList();
            var tabs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = groups.Count, RowCount = 1, Margin = new Padding(0, 0, 0, S(6)) };
            AddColumns(tabs, groups.Select(g => SizeType.Percent).ToArray());
            for (int i = 0; i < groups.Count; i++)
            {
                string group = groups[i];
                var tab = new TfButton { Text = group, Points = 12f, AutoSize = false, Dock = DockStyle.Fill, Margin = new Padding(0, 0, i < groups.Count - 1 ? S(4) : 0, 0) };
                tab.Click += (s, e) => ShowClass(group);
                classTabs[group] = tab;
                tabs.Controls.Add(tab, i, 0);
            }
            return tabs;
        }

        /// <summary>Weapon cards, preview and settings, crosshair gallery.</summary>
        Control BuildMainArea()
        {
            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, S(8)) };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            gallery.SelectionChanged += (s, e) => OnGallerySelectionChanged();
            main.Controls.Add(BuildWeaponPanel(), 0, 0);
            main.Controls.Add(BuildCentrePanel(), 1, 0);
            main.Controls.Add(BuildGalleryPanel(), 2, 0);
            return main;
        }

        /// <summary>The class heading, the search box and the cards.</summary>
        Control BuildWeaponPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, S(10), 0) };
            AddColumns(panel, SizeType.Percent);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var heading = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, S(6)) };
            AddColumns(heading, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
            filterBox.TextChanged += (s, e) => { if (!updatingUi) { PopulateWeapons(); cards.SelectFirst(); } };
            AddCells(heading, 0, classLabel, filterBox, MakeButton("Select all", () => cards.SelectAll()));

            cards.Margin = new Padding(0);
            cards.SelectionChanged += (s, e) => OnWeaponSelectionChanged();
            cards.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { ClearSelected(); e.Handled = true; } };
            panel.Controls.Add(heading, 0, 0);
            panel.Controls.Add(cards, 0, 1);
            return panel;
        }

        /// <summary>The preview over the size / explosion / zoom settings and the crosshair buttons.</summary>
        Control BuildCentrePanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, S(10), 0) };
            AddColumns(panel, SizeType.Percent);
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            preview.Background = Assets.PreviewBackground();
            preview.Margin = new Padding(0, 0, 0, S(8));
            panel.Controls.Add(preview, 0, 0);

            var settings = new RaisedPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Padding = new Padding(S(10), S(8), S(10), S(10)), Margin = new Padding(0) };
            AddColumns(settings, SizeType.Percent);

            selectedLabel.Height = S(44);
            selectedLabel.Margin = new Padding(0, 0, 0, S(4));
            settings.Controls.Add(selectedLabel, 0, 0);

            var sizeRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, S(6)) };
            AddColumns(sizeRow, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
            sizeSlider.Height = S(26);
            sizeSlider.Margin = new Padding(S(4), 2, S(8), 0);
            sizeSlider.UserChanged += (s, e) => sizeBox.Value = sizeSlider.Value;
            sizeBox.ValueChanged += (s, e) => { sizeSlider.Value = (int)sizeBox.Value; OnSizeChanged(); };
            AddCells(sizeRow, 0, Caption("Size (px)"), sizeSlider, sizeBox);
            settings.Controls.Add(sizeRow, 0, 1);

            var pickRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, S(8)) };
            AddColumns(pickRow, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize);
            foreach (var o in ExplosionEffects.Options) explosionBox.Items.Add(o.Value);
            explosionBox.SelectedIndex = 0;
            explosionBox.Enabled = false;
            explosionBox.SelectedIndexChanged += (s, e) => OnExplosionChanged();
            zoomBox.Items.AddRange(["1x", "2x", "3x", "4x"]);
            zoomBox.SelectedIndex = 1;
            zoomBox.SelectedIndexChanged += (s, e) => { preview.Zoom = zoomBox.SelectedIndex + 1; preview.Invalidate(); };
            AddCells(pickRow, 0, Caption("Explosion"), explosionBox, Caption("Zoom", left: S(8)), zoomBox);
            settings.Controls.Add(pickRow, 0, 2);

            var buttons = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Dock = DockStyle.Top, Margin = new Padding(0) };
            AddColumns(buttons, SizeType.Percent, SizeType.Percent);
            buttons.ColumnStyles[0].Width = buttons.ColumnStyles[1].Width = 50;
            AddCells(buttons, 0, Stretched(MakeButton("Use for all weapons", ApplyToAll)), Stretched(MakeButton(StockOutlinePreset.Label, ApplyStockOutlinePreset)));
            AddCells(buttons, 1, Stretched(MakeButton("Reset selected", ClearSelected)), Stretched(MakeButton("Add custom .vtf", AddCustomCrosshairs)));
            settings.Controls.Add(buttons, 0, 3);

            panel.Controls.Add(settings, 0, 1);
            return panel;
        }

        Control BuildGalleryPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            AddColumns(panel, SizeType.Percent);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var heading = new TfLabel { Text = "CROSSHAIRS", IsHeading = true, Points = 14f, Role = TfTextRole.Accent, AutoSize = true, Margin = new Padding(0, S(4), 0, S(8)) };
            gallery.Margin = new Padding(0);
            panel.Controls.Add(heading, 0, 0);
            panel.Controls.Add(gallery, 0, 1);
            return panel;
        }

        /// <summary>Install, the other output buttons, the status text and, set apart, the reset.</summary>
        Control BuildBottomBar()
        {
            var bottom = new TableLayoutPanel { AutoSize = true, ColumnCount = 5, Dock = DockStyle.Top, Margin = new Padding(0) };
            AddColumns(bottom, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
            var install = MakeButton("Install into TF2", Install, TfButtonKind.Primary, 15f);
            install.MinimumSize = new Size(0, S(46));
            var reset = MakeButton("Reset all weapons", ClearAll, TfButtonKind.Danger);
            reset.Anchor = AnchorStyles.Right;
            var zip = MakeButton("Save ZIP", SaveZip);
            var folder = MakeButton("Open custom folder", OpenCustomFolder);
            zip.Anchor = folder.Anchor = AnchorStyles.Left;
            AddCells(bottom, 0, install, zip, folder, statusLabel, reset);
            return bottom;
        }

        TfLabel Caption(string text, int left = 0) =>
            new() { Text = text, Role = TfTextRole.Muted, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(left, 0, S(6), 0) };

        static TfButton MakeButton(string text, Action onClick, TfButtonKind kind = TfButtonKind.Normal, float points = 11f)
        {
            var b = new TfButton { Text = text, AutoSize = true, Kind = kind, Points = points, Margin = new Padding(0, 0, 6, 0) };
            b.Click += (s, e) => onClick();
            return b;
        }

        static TfButton Stretched(TfButton b)
        {
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0, 0, 4, 4);
            return b;
        }

        /// <summary>One column style per entry: Percent columns share the spare width, AutoSize ones fit their content.</summary>
        static void AddColumns(TableLayoutPanel table, params SizeType[] sizes)
        {
            foreach (var size in sizes)
                table.ColumnStyles.Add(size == SizeType.Percent ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(size));
        }

        static void AddCells(TableLayoutPanel table, int row, params Control[] cells)
        {
            for (int col = 0; col < cells.Length; col++) table.Controls.Add(cells[col], col, row);
        }

        // ------------------------------------------------------------------ data

        void LoadWeapons()
        {
            foreach (var parts in Assets.TsvRows(Assets.Text("weapons.tsv")))
            {
                if (parts.Length < 3 || !Assets.HasScript(parts[1])) continue;
                weapons.Add(new WeaponInfo
                {
                    Group = parts[0],
                    Script = parts[1],
                    Names = parts[2],
                    Explosive = Packager.IsExplosive(Assets.Script(parts[1]))
                });
            }
        }

        void LoadBundledCrosshairs()
        {
            var packs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var parts in Assets.TsvRows(Assets.Text("crosshairs.tsv")))
            {
                if (parts.Length < 2) continue;
                packs[parts[0]] = parts[1];
                order.Add(parts[0]);
            }
            var names = Assets.CrosshairNames().ToList();
            // keep the catalog order, then anything unlisted
            foreach (var name in order.Where(n => names.Contains(n, StringComparer.OrdinalIgnoreCase))
                                     .Concat(names.Where(n => !packs.ContainsKey(n))))
            {
                if (crosshairs.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                byte[] vtf = Assets.Crosshair(name);
                try
                {
                    crosshairs.Add(new CrosshairInfo { Name = name, Pack = packs.GetValueOrDefault(name, "Other"), Vtf = vtf, Image = Vtf.Decode(vtf) });
                }
                catch (Exception)
                {
                    // a broken bundled file should not take the whole app down
                }
            }
        }

        void LoadCustomCrosshairs()
        {
            var missing = new List<string>();
            foreach (var (name, path) in profile.CustomCrosshairs)
            {
                try
                {
                    if (!File.Exists(path)) { missing.Add(name); continue; }
                    var bytes = File.ReadAllBytes(path);
                    AddCustomCrosshair(name, bytes, Vtf.Decode(bytes), path);
                }
                catch (Exception)
                {
                    missing.Add(name);
                }
            }
            if (missing.Count > 0)
                SetStatus("Could not reload custom crosshair(s): " + string.Join(", ", missing));
        }

        /// <summary>Adds an imported crosshair, or replaces the texture of the one with that name.</summary>
        void AddCustomCrosshair(string name, byte[] vtf, Bitmap image, string sourcePath)
        {
            var existing = crosshairs.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Vtf = vtf;
                existing.Image.Dispose();
                existing.Image = image;
                existing.SourcePath = sourcePath;
                return;
            }
            crosshairs.Add(new CrosshairInfo { Name = name, Pack = "Custom", Vtf = vtf, Image = image, SourcePath = sourcePath });
        }

        CrosshairInfo? FindCrosshair(string? name) =>
            string.IsNullOrEmpty(name) ? null : crosshairs.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        void RestoreState()
        {
            tfFolder = Tf2Locator.NormalizeTfFolder(profile.TfFolder) ?? Tf2Locator.FindTfFolder();
            Tf2Fonts.Load(tfFolder);

            var source = profile.Assignments;
            if (source.Count == 0 && tfFolder != null)
            {
                // Nothing saved yet: pick up whatever this app (or an earlier run) installed last time.
                string manifest = Packager.ManifestPath(Tf2Locator.CustomFolder(tfFolder));
                try { if (File.Exists(manifest)) source = Packager.ParseManifest(File.ReadAllText(manifest)); } catch { }
            }
            foreach (var a in source)
            {
                var w = weapons.FirstOrDefault(x => x.Script.Equals(a.Script, StringComparison.OrdinalIgnoreCase));
                if (w == null || FindCrosshair(a.Crosshair) == null) continue;
                if (!w.Explosive) a.Explosion = "";
                assignments[a.Script] = a.Clone();
            }
            UpdateStatusCount();
        }

        // ------------------------------------------------------------------ list population

        void PopulateGallery() => WithoutEvents(() =>
        {
            thumbs.Clear();

            // keep the catalog's pack order, custom crosshairs first
            var items = new List<GalleryItem>();
            foreach (var c in crosshairs.GroupBy(c => c.Pack).OrderBy(g => g.Key == "Custom" ? -1 : 0).SelectMany(g => g))
            {
                var thumb = MakeThumb(c.Image, Cell);
                thumbs[c.Name] = thumb;
                items.Add(new GalleryItem { Name = c.Name, Group = c.Pack, Image = thumb, Tag = c });
            }
            gallery.SetItems(items);
        });

        static Bitmap MakeThumb(Bitmap src, int cell)
        {
            var bmp = new Bitmap(cell, cell, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(96, 104, 112));
            int w = src.Width, h = src.Height;
            if (w > cell || h > cell)
            {
                double f = Math.Min((double)cell / w, (double)cell / h);
                w = Math.Max(1, (int)Math.Round(w * f));
                h = Math.Max(1, (int)Math.Round(h * f));
            }
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(src, new Rectangle((cell - w) / 2, (cell - h) / 2, w, h));
            return bmp;
        }

        void ShowClass(string group)
        {
            currentClass = group;
            WithoutEvents(filterBox.Clear);
            PopulateWeapons();
            cards.SelectFirst();
            cards.Focus();
        }

        /// <summary>Lists the current class, or every class's matches while the search box has text in it.</summary>
        void PopulateWeapons()
        {
            string filter = filterBox.Text.Trim();
            bool Matches(WeaponInfo w) => filter.Length == 0 ?
                w.Group == currentClass :
                w.Names.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.Script.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.Group.Contains(filter, StringComparison.OrdinalIgnoreCase);

            var rows = weapons.Where(Matches).Select(MakeCard).ToList();
            rows.ForEach(FillCard);
            cards.SetItems(rows);
            classLabel.Text = filter.Length > 0 ? "SEARCH" : currentClass == "All classes" ? "ALL" : currentClass.ToUpperInvariant();
            foreach (var (group, tab) in classTabs) tab.Selected = filter.Length == 0 && group == currentClass;
            UpdateClassCounts();
        }

        void UpdateClassCounts()
        {
            foreach (var (group, tab) in classTabs)
            {
                var list = weapons.Where(w => w.Group == group).ToList();
                tab.SubText = list.Count(HasCrosshair) + " of " + list.Count + " set";
            }
        }

        bool HasCrosshair(WeaponInfo w) => assignments.TryGetValue(w.Script, out var a) && FindCrosshair(a.Crosshair) != null;

        static int GroupRank(string g)
        {
            int i = Array.IndexOf(GroupOrder, g);
            return i < 0 ? 50 : i;
        }

        /// <summary>The names column splits at top-level commas: the first name heads the card, the rest go under it.</summary>
        static WeaponCard MakeCard(WeaponInfo w)
        {
            var names = Regex.Split(w.Names, @",\s*(?![^()]*\))");
            var title = Regex.Match(names[0], @"^(.*?)\s*\((.*)\)$");
            string? note = title.Success ? title.Groups[2].Value : null;
            return new WeaponCard
            {
                Title = title.Success ? title.Groups[1].Value : names[0],
                Also = names.Length > 1 ? "Also " + string.Join(", ", names.Skip(1)) : note ?? "Only this weapon",
                ToolTip = w.Names + "\n(" + w.Script + ".txt)",
                Tag = w,
            };
        }

        void FillCard(WeaponCard card)
        {
            if (card.Tag is not WeaponInfo w) return;
            var a = HasCrosshair(w) ? assignments[w.Script] : null;
            card.Thumb = a != null ? thumbs.GetValueOrDefault(a.Crosshair) : null;
            string explosion = w.Explosive && a is { Explosion.Length: > 0 } ? ", " + ExplosionEffects.Label(a.Explosion).Replace(" (no smoke)", "") : "";
            card.Info = a != null ? a.Crosshair + ", " + a.Size + " px" + explosion : "Stock crosshair";
        }

        void RefreshRows()
        {
            foreach (var card in cards.Items) FillCard(card);
            cards.Invalidate();
            UpdateClassCounts();
        }

        // ------------------------------------------------------------------ selection handling

        List<WeaponInfo> SelectedWeapons() => cards.SelectedItems.Select(c => c.Tag).OfType<WeaponInfo>().ToList();

        CrosshairInfo? SelectedCrosshair() => gallery.SelectedItem?.Tag as CrosshairInfo;

        string SelectedExplosionKey => explosionBox.SelectedIndex >= 0 && explosionBox.SelectedIndex < ExplosionEffects.Options.Length
            ? ExplosionEffects.Options[explosionBox.SelectedIndex].Key : "";

        void SetExplosionKey(string key) =>
            explosionBox.SelectedIndex = Math.Max(0, Array.FindIndex(ExplosionEffects.Options, o => o.Key == key));

        void SetSize(int px) => sizeBox.Value = Math.Clamp(px, sizeBox.Minimum, sizeBox.Maximum);

        /// <summary>Runs a UI update without the change handlers reacting to it.</summary>
        void WithoutEvents(Action update)
        {
            updatingUi = true;
            try { update(); }
            finally { updatingUi = false; }
        }

        void OnWeaponSelectionChanged()
        {
            if (updatingUi) return;
            WithoutEvents(() =>
            {
                var sel = SelectedWeapons();
                explosionBox.Enabled = sel.Any(w => w.Explosive);
                var first = sel.FirstOrDefault();
                if (first == null) return;
                if (assignments.TryGetValue(first.Script, out var a) && FindCrosshair(a.Crosshair) != null)
                {
                    gallery.Select(a.Crosshair);
                    SetSize(a.Size);
                    SetExplosionKey(first.Explosive ? a.Explosion : "");
                }
                else
                {
                    gallery.Select(null);
                    SetExplosionKey("");
                }
            });
            UpdatePreview();
        }

        void OnGallerySelectionChanged()
        {
            if (updatingUi) return;
            if (SelectedCrosshair() is { } c)
            {
                WithoutEvents(() =>
                {
                    var first = SelectedWeapons().FirstOrDefault();
                    SetSize(first != null && assignments.TryGetValue(first.Script, out var a) && a.Crosshair.Equals(c.Name, StringComparison.OrdinalIgnoreCase)
                        ? a.Size : c.Image.Width);
                });
                if (cards.SelectedItems.Count > 0)
                {
                    foreach (var w in SelectedWeapons()) Assign(w, c);
                    AssignmentsChanged();
                }
            }
            UpdatePreview();
        }

        void OnSizeChanged()
        {
            if (updatingUi) return;
            EditSelectedAssignments(w => true, a => a.Size = (int)sizeBox.Value);
            UpdatePreview();
        }

        void OnExplosionChanged()
        {
            if (updatingUi) return;
            EditSelectedAssignments(w => w.Explosive, a => a.Explosion = SelectedExplosionKey);
        }

        /// <summary>Applies a settings change to the selected weapons that already have a crosshair.</summary>
        void EditSelectedAssignments(Func<WeaponInfo, bool> applies, Action<Assignment> edit)
        {
            bool changed = false;
            foreach (var w in SelectedWeapons())
                if (applies(w) && assignments.TryGetValue(w.Script, out var a)) { edit(a); changed = true; }
            if (changed) { RefreshRows(); SaveProfile(); }
        }

        void UpdatePreview()
        {
            var c = SelectedCrosshair();
            preview.Crosshair = c?.Image;
            preview.CrosshairSize = (int)sizeBox.Value;
            preview.Invalidate();

            var sel = SelectedWeapons();
            string who = sel.Count == 1 ? Short(sel[0].Names) : sel.Count + " weapons";
            selectedLabel.Text =
                sel.Count == 0 ? (c != null ? c.Name + "  (" + c.Image.Width + "x" + c.Image.Height + ")\nSelect weapons on the left to assign it." : "Select a weapon on the left.")
                : c == null ? who + ": stock crosshair\nClick a crosshair to assign it."
                : who + " → " + c.Name;
        }

        static string Short(string names)
        {
            int i = names.IndexOf(',');
            return i > 0 ? names[..i] : names;
        }

        // ------------------------------------------------------------------ assignment actions

        /// <summary>After assignments were added, changed or removed: redraw the rows, update the count and autosave.</summary>
        void AssignmentsChanged()
        {
            RefreshRows();
            UpdateStatusCount();
            SaveProfile();
        }

        void Assign(WeaponInfo w, CrosshairInfo c)
        {
            if (!assignments.TryGetValue(w.Script, out var a))
            {
                a = new Assignment { Script = w.Script };
                assignments[w.Script] = a;
            }
            a.Crosshair = c.Name;
            a.Size = (int)sizeBox.Value;
            a.Explosion = w.Explosive ? SelectedExplosionKey : "";
        }

        void ApplyToAll()
        {
            var c = SelectedCrosshair();
            if (c == null)
            {
                Notify("Click a crosshair first, then use this button to give it to every weapon.", MessageBoxIcon.Information);
                return;
            }
            foreach (var w in weapons) Assign(w, c);
            AssignmentsChanged();
            SetStatus("Assigned " + c.Name + " to all " + weapons.Count + " weapons.");
        }

        /// <summary>Gives every weapon an outlined copy of its own stock crosshair; weapons that draw none (PDAs, sapper, lunchboxes) go back to stock.</summary>
        void ApplyStockOutlinePreset()
        {
            int set = 0, missing = 0;
            foreach (var w in weapons)
            {
                if (!Assets.HasScript(w.Script) || !StockOutlinePreset.TryGet(Assets.Script(w.Script), out var name, out int size))
                {
                    assignments.Remove(w.Script);
                    continue;
                }
                if (FindCrosshair(name) == null) { missing++; continue; }
                if (!assignments.TryGetValue(w.Script, out var a))
                {
                    a = new Assignment { Script = w.Script };
                    assignments[w.Script] = a;
                }
                a.Crosshair = name;
                a.Size = size;
                set++;
            }
            WithoutEvents(() => gallery.Select(null));
            UpdatePreview();
            AssignmentsChanged();
            SetStatus("Stock-look preset: " + set + " weapons now use an outlined copy of their stock crosshair." +
                      (missing > 0 ? " " + missing + " skipped because their texture is missing." : ""));
        }

        void ClearSelected()
        {
            var sel = SelectedWeapons();
            if (sel.Count == 0) return;
            foreach (var w in sel) assignments.Remove(w.Script);
            AfterReset();
        }

        void ClearAll()
        {
            if (assignments.Count == 0) return;
            if (MessageBox.Show(this, "Reset every weapon to its stock crosshair?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            assignments.Clear();
            AfterReset();
        }

        /// <summary>After weapons went back to stock: clear the crosshair and explosion pickers, then refresh and autosave.</summary>
        void AfterReset()
        {
            WithoutEvents(() => { gallery.Select(null); SetExplosionKey(""); });
            UpdatePreview();
            AssignmentsChanged();
        }

        void AddCustomCrosshairs()
        {
            using var dlg = new OpenFileDialog { Title = "Add crosshair textures", Filter = "Valve texture (*.vtf)|*.vtf", Multiselect = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var errors = new List<string>();
            string? last = null;
            foreach (var path in dlg.FileNames)
            {
                try { last = ImportCrosshair(path); }
                catch (Exception ex) { errors.Add(Path.GetFileName(path) + ": " + ex.Message); }
            }
            PopulateGallery();
            RefreshRows();
            SaveProfile();
            if (last != null)
            {
                WithoutEvents(() => gallery.Select(last));
                UpdatePreview();
            }
            if (errors.Count > 0)
                Notify("Some files could not be added:\n\n" + string.Join("\n", errors), MessageBoxIcon.Warning);
        }

        /// <summary>Loads one .vtf as a custom crosshair and remembers it in the profile; returns the name it got.</summary>
        string ImportCrosshair(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var image = Vtf.Decode(bytes);
            string name = Regex.Replace(Path.GetFileNameWithoutExtension(path), "[^A-Za-z0-9_\\-]", "_");
            if (name.Length == 0) name = "custom";
            if (FindCrosshair(name) is { SourcePath: null }) name += "_custom";
            AddCustomCrosshair(name, bytes, image, path);
            profile.CustomCrosshairs.RemoveAll(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            profile.CustomCrosshairs.Add(new(name, path));
            return name;
        }

        // ------------------------------------------------------------------ output

        SortedDictionary<string, byte[]>? BuildFiles()
        {
            var list = assignments.Values.Where(a => FindCrosshair(a.Crosshair) != null).ToList();
            if (list.Count == 0)
            {
                Notify("Assign a crosshair to at least one weapon first.", MessageBoxIcon.Information);
                return null;
            }
            try
            {
                return Packager.BuildFiles(list, name => FindCrosshair(name)!.Vtf, Assets.Script);
            }
            catch (Exception ex)
            {
                Notify("Could not build the package:\n" + ex.Message, MessageBoxIcon.Error);
                return null;
            }
        }

        void Install()
        {
            if (tfFolder == null) Notify("Pick your Team Fortress 2\\tf\\custom folder first.", MessageBoxIcon.Information);
            if (!EnsureTfFolder()) return;
            var files = BuildFiles();
            if (files == null) return;
            string custom = Tf2Locator.CustomFolder(tfFolder);
            string target = Packager.InstallFolder(custom);
            if (Directory.Exists(target) && !Packager.OwnsInstallFolder(custom) &&
                MessageBox.Show(this,
                    target + "\n\nalready exists and was not written by this app (probably a cfg.tf package). " +
                    "Replace its weapon scripts and crosshair textures with your current setup?",
                    "Replace existing folder?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                Directory.CreateDirectory(custom);
                Packager.WriteToCustom(files, custom);
            }
            catch (Exception ex)
            {
                Notify("Could not write into the TF2 folder:\n" + ex.Message +
                    "\n\nTry running this app as administrator, or use \"Save ZIP\" and extract it into tf\\custom yourself.", MessageBoxIcon.Error);
                return;
            }
            SetStatus("Installed crosshairs for " + WeaponCount(assignments.Count) + " into " + target + ". Restart TF2 if it is running.");
            CheckCrosshairCvar();
        }

        void SaveZip()
        {
            var files = BuildFiles();
            if (files == null) return;
            using var dlg = new SaveFileDialog { Title = "Save crosshair package", Filter = "ZIP archive (*.zip)|*.zip", FileName = "crosshairs.zip" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                Packager.WriteZip(files, dlg.FileName);
                SetStatus("Saved " + dlg.FileName + ". Extract it into Team Fortress 2\\tf\\custom so you get tf\\custom\\crosshairs.");
            }
            catch (Exception ex)
            {
                Notify("Could not save the ZIP:\n" + ex.Message, MessageBoxIcon.Error);
            }
        }

        void OpenCustomFolder()
        {
            if (!EnsureTfFolder()) return;
            string custom = Tf2Locator.CustomFolder(tfFolder);
            try
            {
                Directory.CreateDirectory(custom);
                Process.Start("explorer.exe", "\"" + custom + "\"");
            }
            catch (Exception ex)
            {
                Notify(ex.Message, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------ TF2 folder

        /// <summary>Asks for the TF2 folder when none is known yet; false when the user still has not picked one.</summary>
        [MemberNotNullWhen(true, nameof(tfFolder))]
        bool EnsureTfFolder()
        {
            if (tfFolder == null) BrowseTfFolder();
            return tfFolder != null;
        }

        void BrowseTfFolder()
        {
            using var dlg = new FolderBrowserDialog { Description = "Select your Team Fortress 2\\tf\\custom folder (the tf or Team Fortress 2 folder above it works too)", ShowNewFolderButton = false };
            if (tfFolder != null) dlg.SelectedPath = Tf2Locator.CustomFolder(tfFolder);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var tf = Tf2Locator.NormalizeTfFolder(dlg.SelectedPath);
            if (tf == null)
            {
                Notify("That does not look like a TF2 folder. Pick the tf\\custom folder of your TF2 install, for example:\n" +
                    "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Team Fortress 2\\tf\\custom", MessageBoxIcon.Warning);
                return;
            }
            tfFolder = tf;
            profile.TfFolder = tf;
            if (Tf2Fonts.Load(tf)) ApplyTheme(Theme.Current);
            UpdateTfFolderUi();
            SaveProfile();
        }

        void UpdateTfFolderUi()
        {
            tfBox.Text = tfFolder != null ? Tf2Locator.CustomFolder(tfFolder) : "(not found - click Browse and pick your Team Fortress 2\\tf\\custom folder)";
            tfBox.ForeColor = tfFolder != null ? Theme.Current.Fore : Theme.Current.Warn;
            CheckCrosshairCvar();
        }

        void CheckCrosshairCvar()
        {
            string? value = tfFolder != null ? Tf2Locator.ReadCrosshairFileCvar(tfFolder) : null;
            bool bad = !string.IsNullOrEmpty(value) && value != "\"\"";
            warnLabel.Visible = bad;
            warnLabel.ForeColor = Theme.Current.Warn;
            warnLabel.BackColor = Theme.Current.WarnBack;
            if (bad)
                warnLabel.Text = "Heads-up: your config.cfg has cl_crosshair_file \"" + value + "\". Set the crosshair to None in TF2's options " +
                                 "(or run cl_crosshair_file \"\" in the console) or weapon crosshairs will not show.";
        }

        // ------------------------------------------------------------------ misc

        static Profile SafeLoadProfile()
        {
            try { return Profile.Load(Profile.DefaultPath); }
            catch { return new Profile(); }
        }

        void SaveProfile()
        {
            try
            {
                profile.TfFolder = tfFolder;
                profile.Assignments.Clear();
                profile.Assignments.AddRange(assignments.Values.OrderBy(a => a.Script, StringComparer.OrdinalIgnoreCase).Select(a => a.Clone()));
                profile.Save(Profile.DefaultPath);
            }
            catch
            {
                // losing the autosave is not worth interrupting the user
            }
        }

        void UpdateStatusCount() =>
            SetStatus(assignments.Count == 0 ? "No weapons assigned yet." : WeaponCount(assignments.Count) + " assigned.");

        static string WeaponCount(int n) => n + " weapon" + (n == 1 ? "" : "s");

        void SetStatus(string text) => statusLabel.Text = text;

        void Notify(string text, MessageBoxIcon icon) => MessageBox.Show(this, text, Text, MessageBoxButtons.OK, icon);

        void ShowHelp()
        {
            MessageBox.Show(this,
                "1. Pick a class tab, then select one or more weapons (Ctrl/Shift-click for several, or \"Select all\" for the whole class). The search box looks through every class.\n" +
                "2. Click a crosshair on the right. It is assigned immediately; the preview shows it over a game screenshot.\n" +
                "3. Size changes how big the crosshair is drawn in game. Explosive weapons can also swap the explosion effect for a smoke-free one.\n" +
                "4. Click \"Install into TF2\" to write tf\\custom\\crosshairs, or \"Save ZIP\" to share the package.\n" +
                "5. In TF2 set the crosshair to None (cl_crosshair_file \"\"), otherwise the game draws that one instead.\n" +
                "   The crosshair colour still comes from TF2's crosshair colour settings (cl_crosshair_red / green / blue).\n\n" +
                "Weapons you do not assign keep their stock crosshair. Your setup is saved automatically and restored next time.\n" +
                "Custom .vtf crosshairs can be added with the button under the preview; they are copied into the package.\n\n" +
                "This mirrors the cfg.tf weapon-specific crosshair tool, with the weapons it was missing added.",
                "How it works", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
