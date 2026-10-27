using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public byte[] Vtf = new byte[0];
        public Bitmap Image = null!;
        public string? SourcePath;   // set for crosshairs the user imported
    }

    /// <summary>Game screenshot with the chosen crosshair drawn over its centre.</summary>
    sealed class PreviewPanel : Control
    {
        public Image? Background;
        public Bitmap? Crosshair;
        public int CrosshairSize = 64;
        public int Zoom = 1;

        public PreviewPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(70, 80, 90);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int cx = Width / 2, cy = Height / 2;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (Background != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                int w = Background.Width * Zoom, h = Background.Height * Zoom;
                g.DrawImage(Background, new Rectangle(cx - w / 2, cy - h / 2, w, h));
            }
            if (Crosshair != null)
            {
                int s = Math.Max(1, CrosshairSize * Zoom);
                g.InterpolationMode = s >= Crosshair.Width ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                g.DrawImage(Crosshair, new Rectangle(cx - s / 2, cy - s / 2, s, s));
            }
        }
    }

    public sealed class MainForm : Form
    {
        static readonly string[] GroupOrder = { "Scout", "Soldier", "Pyro", "Demoman", "Heavy", "Engineer", "Medic", "Sniper", "Spy", "All classes", "Custom" };
        const int Cell = 64;
        const string StockText = "stock";

        readonly List<WeaponInfo> weapons = new List<WeaponInfo>();
        readonly List<CrosshairInfo> crosshairs = new List<CrosshairInfo>();
        readonly Dictionary<string, Assignment> assignments = new Dictionary<string, Assignment>(StringComparer.OrdinalIgnoreCase);
        readonly Profile profile;
        string? tfFolder;
        bool updatingUi;

        readonly ListView weaponList = new ListView();
        readonly TextBox filterBox = new TextBox();
        readonly Gallery gallery = new Gallery();
        readonly ImageList smallImages = new ImageList { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
        readonly PreviewPanel preview = new PreviewPanel();
        readonly NumericUpDown sizeBox = new NumericUpDown { Minimum = 8, Maximum = 512, Value = 64, Width = 64 };
        readonly ComboBox explosionBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        readonly ComboBox zoomBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64 };
        readonly Label selectedLabel = new Label { AutoSize = false, Size = new Size(300, 36), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        readonly TextBox tfBox = new TextBox { ReadOnly = true };
        readonly Label warnLabel = new Label { AutoSize = true, ForeColor = Color.Firebrick, Visible = false };
        readonly Label statusLabel = new Label { AutoSize = true, Padding = new Padding(4, 7, 0, 0) };
        readonly Button installBtn = new Button { Text = "Install into TF2", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        readonly Button openFolderBtn = new Button { Text = "Open custom folder", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        readonly CheckBox darkBox = new CheckBox { Text = "Dark mode", AutoSize = true, Checked = true, Margin = new Padding(10, 5, 0, 0) };

        public MainForm()
        {
            Text = "TF2 Weapon Crosshairs";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1300, 760);
            MinimumSize = new Size(1000, 640);

            profile = SafeLoadProfile();
            BuildLayout();
            LoadCatalog();
            LoadCustomCrosshairs();
            PopulateGallery();
            PopulateWeapons();
            darkBox.Checked = profile.Dark;
            ApplyTheme(profile.Dark);
            RestoreState();
            FormClosing += (s, e) => SaveProfile();
        }

        void ApplyTheme(bool dark)
        {
            Theme.Apply(this, dark ? Theme.DarkPalette : Theme.LightPalette);
            RefreshRows();
            UpdateTfFolderUi();
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyNative(this, Theme.Current);
        }

        // ------------------------------------------------------------------ layout

        void BuildLayout()
        {
            // --- top: intro + TF2 folder
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(10, 8, 10, 4) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var intro = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = "Pick one or more weapons on the left, then click a crosshair on the right to assign it. " +
                       "Weapons you leave alone keep their stock crosshair. When you are done, click \"Install into TF2\" " +
                       "(or save a ZIP to share). In TF2 the crosshair must be set to None / cl_crosshair_file \"\" for these to show."
            };
            top.Controls.Add(intro);

            var tfRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 5, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 0) };
            tfRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tfRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tfRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tfRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tfRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tfRow.Controls.Add(new Label { Text = "Install folder:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, 0);
            tfBox.Dock = DockStyle.Fill;
            tfRow.Controls.Add(tfBox, 1, 0);
            var browse = new Button { Text = "Browse...", AutoSize = true };
            browse.Click += (s, e) => BrowseTfFolder();
            tfRow.Controls.Add(browse, 2, 0);
            var help = new Button { Text = "How it works", AutoSize = true };
            help.Click += (s, e) => ShowHelp();
            tfRow.Controls.Add(help, 3, 0);
            darkBox.CheckedChanged += (s, e) =>
            {
                profile.Dark = darkBox.Checked;
                ApplyTheme(darkBox.Checked);
                SaveProfile();
            };
            tfRow.Controls.Add(darkBox, 4, 0);
            top.Controls.Add(tfRow);
            warnLabel.Dock = DockStyle.Fill;
            top.Controls.Add(warnLabel);

            // --- bottom: actions + status
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 4, 8, 6), WrapContents = false };
            installBtn.Click += (s, e) => Install();
            var zipBtn = new Button { Text = "Save ZIP...", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            zipBtn.Click += (s, e) => SaveZip();
            openFolderBtn.Click += (s, e) => OpenCustomFolder();
            bottom.Controls.Add(installBtn);
            bottom.Controls.Add(zipBtn);
            bottom.Controls.Add(openFolderBtn);
            bottom.Controls.Add(statusLabel);

            // --- left: weapon list
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            var filterRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 0, 0, 4) };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filterRow.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 6, 6, 0) }, 0, 0);
            filterBox.Dock = DockStyle.Fill;
            filterBox.TextChanged += (s, e) => PopulateWeapons();
            filterRow.Controls.Add(filterBox, 1, 0);
            weaponList.Dock = DockStyle.Fill;
            weaponList.View = View.Details;
            weaponList.FullRowSelect = true;
            weaponList.HideSelection = false;
            weaponList.MultiSelect = true;
            weaponList.ShowGroups = false;
            weaponList.SmallImageList = smallImages;
            weaponList.Columns.Add("Weapon", 390);
            weaponList.Columns.Add("Crosshair", 135);
            weaponList.Columns.Add("Size", 40, HorizontalAlignment.Right);
            weaponList.Columns.Add("Explosion", 100);
            weaponList.SelectedIndexChanged += (s, e) => OnWeaponSelectionChanged();
            weaponList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { ClearSelected(); e.Handled = true; } };
            weaponList.Resize += (s, e) => FitWeaponColumn();
            // the column header is system-drawn in light grey, so paint it ourselves when the theme is dark
            // class headings are plain rows with a null Tag, painted here; weapons use the default drawing
            weaponList.OwnerDraw = true;
            weaponList.DrawColumnHeader += (s, e) => Theme.DrawColumnHeader(e);
            weaponList.DrawItem += (s, e) => { if (e.Item?.Tag == null) Theme.DrawHeaderRow(e); else e.DrawDefault = true; };
            weaponList.DrawSubItem += (s, e) => e.DrawDefault = e.Item?.Tag != null;
            left.Controls.Add(weaponList);
            left.Controls.Add(filterRow);

            // --- right: gallery over preview/settings
            gallery.Dock = DockStyle.Fill;
            gallery.SelectionChanged += (s, e) => OnGallerySelectionChanged();

            var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(4) };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            preview.Dock = DockStyle.Fill;
            preview.Background = Assets.PreviewBackground();
            settings.Controls.Add(preview, 0, 0);

            var side = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(8, 0, 0, 0) };
            side.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            side.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int row = 0;
            side.Controls.Add(selectedLabel, 0, row);
            side.SetColumnSpan(selectedLabel, 2);
            row++;
            side.Controls.Add(new Label { Text = "Size (px):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, row);
            sizeBox.ValueChanged += (s, e) => OnSizeChanged();
            side.Controls.Add(sizeBox, 1, row++);
            side.Controls.Add(new Label { Text = "Explosion:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, row);
            foreach (var o in ExplosionEffects.Options) explosionBox.Items.Add(o.Value);
            explosionBox.SelectedIndex = 0;
            explosionBox.Enabled = false;
            explosionBox.SelectedIndexChanged += (s, e) => OnExplosionChanged();
            side.Controls.Add(explosionBox, 1, row++);
            side.Controls.Add(new Label { Text = "Preview zoom:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, row);
            zoomBox.Items.AddRange(new object[] { "1x", "2x", "3x", "4x" });
            zoomBox.SelectedIndex = 1;
            zoomBox.SelectedIndexChanged += (s, e) => { preview.Zoom = zoomBox.SelectedIndex + 1; preview.Invalidate(); };
            side.Controls.Add(zoomBox, 1, row++);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 8, 0, 0) };
            var applyAll = new Button { Text = "Use this crosshair for ALL weapons", AutoSize = true };
            applyAll.Click += (s, e) => ApplyToAll();
            var clearSel = new Button { Text = "Reset selected weapons to stock", AutoSize = true };
            clearSel.Click += (s, e) => ClearSelected();
            var clearAll = new Button { Text = "Reset ALL weapons to stock", AutoSize = true };
            clearAll.Click += (s, e) => ClearAll();
            var addCustom = new Button { Text = "Add custom crosshair (.vtf)...", AutoSize = true };
            addCustom.Click += (s, e) => AddCustomCrosshairs();
            foreach (var b in new[] { applyAll, clearSel, clearAll, addCustom }) { b.Width = 230; b.Margin = new Padding(0, 2, 0, 2); buttons.Controls.Add(b); }
            side.Controls.Add(buttons, 0, row);
            side.SetColumnSpan(buttons, 2);
            settings.Controls.Add(side, 1, 0);

            var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6 };
            rightSplit.Panel1.Controls.Add(gallery);
            rightSplit.Panel2.Controls.Add(settings);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6 };
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(rightSplit);

            Controls.Add(split);
            Controls.Add(bottom);
            Controls.Add(top);

            // Min sizes and splitter positions can only be applied once the containers have their real size.
            Shown += (s, e) =>
            {
                try
                {
                    split.Panel1MinSize = 380;
                    split.SplitterDistance = Math.Max(split.Panel1MinSize, (int)(split.Width * 0.54));
                    rightSplit.Panel2MinSize = 250;
                    rightSplit.SplitterDistance = Math.Max(rightSplit.Panel1MinSize, rightSplit.Height - 290);
                    FitWeaponColumn();
                }
                catch (InvalidOperationException)
                {
                    // window too small for the preferred split; the defaults are fine
                }
            };
        }

        // ------------------------------------------------------------------ data

        void LoadCatalog()
        {
            foreach (var raw in Assets.Text("weapons.tsv").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('\t');
                if (parts.Length < 3 || !Assets.HasScript(parts[1])) continue;
                weapons.Add(new WeaponInfo
                {
                    Group = parts[0],
                    Script = parts[1],
                    Names = parts[2],
                    Explosive = Packager.IsExplosive(Assets.Script(parts[1]))
                });
            }

            var packs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var raw in Assets.Text("crosshairs.tsv").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('\t');
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
                    crosshairs.Add(new CrosshairInfo { Name = name, Pack = packs.TryGetValue(name, out var p) ? p : "Other", Vtf = vtf, Image = Vtf.Decode(vtf) });
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
            foreach (var kv in profile.CustomCrosshairs.ToList())
            {
                try
                {
                    if (!File.Exists(kv.Value)) { missing.Add(kv.Key); continue; }
                    var bytes = File.ReadAllBytes(kv.Value);
                    AddCrosshair(kv.Key, bytes, Vtf.Decode(bytes), kv.Value);
                }
                catch (Exception)
                {
                    missing.Add(kv.Key);
                }
            }
            if (missing.Count > 0)
                SetStatus("Could not reload custom crosshair(s): " + string.Join(", ", missing));
        }

        CrosshairInfo AddCrosshair(string name, byte[] vtf, Bitmap image, string? sourcePath)
        {
            var existing = crosshairs.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Vtf = vtf;
                existing.Image.Dispose();
                existing.Image = image;
                existing.SourcePath = sourcePath;
                return existing;
            }
            var info = new CrosshairInfo { Name = name, Pack = sourcePath != null ? "Custom" : "Other", Vtf = vtf, Image = image, SourcePath = sourcePath };
            crosshairs.Add(info);
            return info;
        }

        CrosshairInfo? FindCrosshair(string? name) =>
            string.IsNullOrEmpty(name) ? null : crosshairs.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        void RestoreState()
        {
            tfFolder = Tf2Locator.NormalizeTfFolder(profile.TfFolder) ?? Tf2Locator.FindTfFolder();
            UpdateTfFolderUi();

            var source = profile.Assignments.ToList();
            if (source.Count == 0 && tfFolder != null)
            {
                // Nothing saved yet: pick up whatever this app (or an earlier run) installed last time.
                string manifest = Path.Combine(Tf2Locator.CustomFolder(tfFolder), Packager.FolderName, Packager.ManifestName);
                try { if (File.Exists(manifest)) source = Packager.ParseManifest(File.ReadAllText(manifest)); } catch { }
            }
            foreach (var a in source)
            {
                if (!weapons.Any(w => w.Script.Equals(a.Script, StringComparison.OrdinalIgnoreCase))) continue;
                if (FindCrosshair(a.Crosshair) == null) continue;
                var w = weapons.First(x => x.Script.Equals(a.Script, StringComparison.OrdinalIgnoreCase));
                if (!w.Explosive) a.Explosion = "";
                assignments[a.Script] = a.Clone();
            }
            RefreshRows();
            UpdateStatusCount();
        }

        // ------------------------------------------------------------------ list population

        void PopulateGallery()
        {
            updatingUi = true;
            try
            {
                smallImages.Images.Clear();
                smallImages.Images.Add("none", new Bitmap(20, 20, PixelFormat.Format32bppArgb));

                // keep the catalog's pack order, custom crosshairs first
                var packs = crosshairs.Select(c => c.Pack).Distinct().OrderBy(p => p == "Custom" ? -1 : 0).ToList();
                var items = new List<GalleryItem>();
                foreach (var pack in packs)
                    foreach (var c in crosshairs.Where(x => x.Pack == pack))
                    {
                        smallImages.Images.Add(c.Name, MakeThumb(c.Image, 20));
                        items.Add(new GalleryItem { Name = c.Name, Group = pack, Image = MakeThumb(c.Image, Cell), Tag = c });
                    }
                gallery.SetItems(items);
            }
            finally
            {
                updatingUi = false;
            }
        }

        static Bitmap MakeThumb(Bitmap src, int cell)
        {
            var bmp = new Bitmap(cell, cell, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
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
            }
            return bmp;
        }

        void PopulateWeapons()
        {
            var selected = new HashSet<string>(SelectedWeapons().Select(w => w.Script), StringComparer.OrdinalIgnoreCase);
            updatingUi = true;
            try
            {
                weaponList.BeginUpdate();
                weaponList.Items.Clear();
                string filter = filterBox.Text.Trim();
                foreach (var g in weapons.Select(w => w.Group).Distinct().OrderBy(GroupRank))
                {
                    var rows = weapons.Where(w => w.Group == g && (filter.Length == 0 ||
                        w.Names.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        w.Script.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        w.Group.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    if (rows.Count == 0) continue;
                    weaponList.Items.Add(new ListViewItem(new[] { g, "", "", "" }) { Tag = null, ImageKey = "none" });
                    foreach (var w in rows)
                    {
                        var item = new ListViewItem(new[] { w.Names, "", "", "" }) { Tag = w, ToolTipText = w.Names + "\n(" + w.Script + ".txt)", UseItemStyleForSubItems = false, IndentCount = 1 };
                        item.Selected = selected.Contains(w.Script);
                        weaponList.Items.Add(item);
                        FillRow(item);
                    }
                }
                weaponList.ShowItemToolTips = true;
                weaponList.EndUpdate();
            }
            finally
            {
                updatingUi = false;
            }
            OnWeaponSelectionChanged();
        }

        static int GroupRank(string g)
        {
            int i = Array.IndexOf(GroupOrder, g);
            return i < 0 ? 50 : i;
        }

        /// <summary>The weapon column takes whatever width the fixed columns leave over.</summary>
        void FitWeaponColumn()
        {
            if (weaponList.Columns.Count < 4) return;
            int fixedWidth = 0;
            for (int i = 1; i < weaponList.Columns.Count; i++) fixedWidth += weaponList.Columns[i].Width;
            int w = weaponList.ClientSize.Width - fixedWidth - 4;
            weaponList.Columns[0].Width = Math.Max(240, w);
        }

        void FillRow(ListViewItem item)
        {
            if (!(item.Tag is WeaponInfo w)) return; // class heading row
            Assignment? a = assignments.TryGetValue(w.Script, out var found) && FindCrosshair(found.Crosshair) != null ? found : null;
            item.ImageKey = a != null ? a.Crosshair : "none";
            item.SubItems[0].ForeColor = Theme.Current.Fore;
            item.SubItems[1].Text = a != null ? a.Crosshair : StockText;
            item.SubItems[1].ForeColor = a != null ? Theme.Current.Fore : Theme.Current.Muted;
            item.SubItems[2].Text = a != null ? a.Size.ToString() : "";
            item.SubItems[2].ForeColor = Theme.Current.Fore;
            item.SubItems[3].Text = !w.Explosive || a == null ? "" : a.Explosion.Length == 0 ? "Default" : ExplosionEffects.Label(a.Explosion).Replace(" (no smoke)", "");
            item.SubItems[3].ForeColor = a != null && a.Explosion.Length > 0 ? Theme.Current.Fore : Theme.Current.Muted;
        }

        void RefreshRows()
        {
            foreach (ListViewItem item in weaponList.Items) FillRow(item);
        }

        // ------------------------------------------------------------------ selection handling

        List<WeaponInfo> SelectedWeapons() => weaponList.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag as WeaponInfo).Where(w => w != null).Select(w => w!).ToList();

        CrosshairInfo? SelectedCrosshair() => gallery.SelectedItem?.Tag as CrosshairInfo;

        string SelectedExplosionKey => explosionBox.SelectedIndex >= 0 && explosionBox.SelectedIndex < ExplosionEffects.Options.Length
            ? ExplosionEffects.Options[explosionBox.SelectedIndex].Key : "";

        void SetExplosionKey(string key)
        {
            for (int i = 0; i < ExplosionEffects.Options.Length; i++)
                if (ExplosionEffects.Options[i].Key == key) { explosionBox.SelectedIndex = i; return; }
            explosionBox.SelectedIndex = 0;
        }

        void SelectGalleryItem(string? name) => gallery.Select(name);

        void OnWeaponSelectionChanged()
        {
            if (updatingUi) return;
            updatingUi = true;
            try
            {
                // class heading rows are not selectable
                foreach (ListViewItem it in weaponList.SelectedItems.Cast<ListViewItem>().Where(i => i.Tag == null).ToList())
                    it.Selected = false;
                var sel = SelectedWeapons();
                explosionBox.Enabled = sel.Any(w => w.Explosive);
                var first = sel.FirstOrDefault();
                Assignment? a = null;
                if (first != null) assignments.TryGetValue(first.Script, out a);
                if (a != null && FindCrosshair(a.Crosshair) != null)
                {
                    SelectGalleryItem(a.Crosshair);
                    sizeBox.Value = Math.Min(sizeBox.Maximum, Math.Max(sizeBox.Minimum, a.Size));
                    SetExplosionKey(first!.Explosive ? a.Explosion : "");
                }
                else if (first != null)
                {
                    SelectGalleryItem(null);
                    SetExplosionKey("");
                }
            }
            finally
            {
                updatingUi = false;
            }
            UpdatePreview();
        }

        void OnGallerySelectionChanged()
        {
            if (updatingUi) return;
            var c = SelectedCrosshair();
            if (c != null)
            {
                updatingUi = true;
                try
                {
                    var first = SelectedWeapons().FirstOrDefault();
                    if (first != null && assignments.TryGetValue(first.Script, out var a) && a.Crosshair.Equals(c.Name, StringComparison.OrdinalIgnoreCase))
                        sizeBox.Value = Math.Min(sizeBox.Maximum, Math.Max(sizeBox.Minimum, a.Size));
                    else
                        sizeBox.Value = Math.Min(sizeBox.Maximum, Math.Max(sizeBox.Minimum, c.Image.Width));
                }
                finally
                {
                    updatingUi = false;
                }
                if (weaponList.SelectedItems.Count > 0) AssignToSelected(c.Name);
            }
            UpdatePreview();
        }

        void OnSizeChanged()
        {
            if (updatingUi) return;
            bool changed = false;
            foreach (var w in SelectedWeapons())
                if (assignments.TryGetValue(w.Script, out var a)) { a.Size = (int)sizeBox.Value; changed = true; }
            if (changed) { RefreshRows(); SaveProfile(); }
            UpdatePreview();
        }

        void OnExplosionChanged()
        {
            if (updatingUi) return;
            bool changed = false;
            foreach (var w in SelectedWeapons())
                if (w.Explosive && assignments.TryGetValue(w.Script, out var a)) { a.Explosion = SelectedExplosionKey; changed = true; }
            if (changed) { RefreshRows(); SaveProfile(); }
        }

        void UpdatePreview()
        {
            var c = SelectedCrosshair();
            preview.Crosshair = c?.Image;
            preview.CrosshairSize = (int)sizeBox.Value;
            preview.Invalidate();

            var sel = SelectedWeapons();
            string text;
            if (sel.Count == 0)
                text = c != null ? c.Name + "  (" + c.Image.Width + "x" + c.Image.Height + ")\nSelect weapons on the left to assign it." : "Select a weapon on the left.";
            else if (c == null)
                text = (sel.Count == 1 ? Short(sel[0].Names) : sel.Count + " weapons") + ": stock crosshair\nClick a crosshair to assign it.";
            else
                text = (sel.Count == 1 ? Short(sel[0].Names) : sel.Count + " weapons") + " \u2192 " + c.Name;
            selectedLabel.Text = text;
        }

        static string Short(string names)
        {
            int i = names.IndexOf(',');
            return i > 0 ? names.Substring(0, i) : names;
        }

        // ------------------------------------------------------------------ assignment actions

        void AssignToSelected(string crosshairName)
        {
            var c = FindCrosshair(crosshairName);
            if (c == null) return;
            foreach (var w in SelectedWeapons()) Assign(w, c);
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
                MessageBox.Show(this, "Click a crosshair first, then use this button to give it to every weapon.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (var w in weapons) Assign(w, c);
            RefreshRows();
            UpdateStatusCount();
            SaveProfile();
            SetStatus("Assigned " + c.Name + " to all " + weapons.Count + " weapons.");
        }

        void ClearSelected()
        {
            var sel = SelectedWeapons();
            if (sel.Count == 0) return;
            foreach (var w in sel) assignments.Remove(w.Script);
            updatingUi = true;
            try { SelectGalleryItem(null); SetExplosionKey(""); }
            finally { updatingUi = false; }
            RefreshRows();
            UpdatePreview();
            UpdateStatusCount();
            SaveProfile();
        }

        void ClearAll()
        {
            if (assignments.Count == 0) return;
            if (MessageBox.Show(this, "Reset every weapon to its stock crosshair?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            assignments.Clear();
            updatingUi = true;
            try { SelectGalleryItem(null); SetExplosionKey(""); }
            finally { updatingUi = false; }
            RefreshRows();
            UpdatePreview();
            UpdateStatusCount();
            SaveProfile();
        }

        void AddCustomCrosshairs()
        {
            using (var dlg = new OpenFileDialog { Title = "Add crosshair textures", Filter = "Valve texture (*.vtf)|*.vtf", Multiselect = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var errors = new List<string>();
                string? last = null;
                foreach (var path in dlg.FileNames)
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        var image = Vtf.Decode(bytes);
                        string name = Regex.Replace(Path.GetFileNameWithoutExtension(path), "[^A-Za-z0-9_\\-]", "_");
                        if (name.Length == 0) name = "custom";
                        var clash = FindCrosshair(name);
                        if (clash != null && clash.SourcePath == null) name += "_custom";
                        AddCrosshair(name, bytes, image, path);
                        profile.CustomCrosshairs.RemoveAll(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
                        profile.CustomCrosshairs.Add(new KeyValuePair<string, string>(name, path));
                        last = name;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(Path.GetFileName(path) + ": " + ex.Message);
                    }
                }
                PopulateGallery();
                RefreshRows();
                SaveProfile();
                if (last != null)
                {
                    updatingUi = true;
                    try { SelectGalleryItem(last); } finally { updatingUi = false; }
                    UpdatePreview();
                }
                if (errors.Count > 0)
                    MessageBox.Show(this, "Some files could not be added:\n\n" + string.Join("\n", errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------------------------ output

        SortedDictionary<string, byte[]>? BuildFiles()
        {
            var list = assignments.Values.Where(a => FindCrosshair(a.Crosshair) != null).ToList();
            if (list.Count == 0)
            {
                MessageBox.Show(this, "Assign a crosshair to at least one weapon first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            try
            {
                return Packager.BuildFiles(list, name => FindCrosshair(name)!.Vtf, Assets.Script);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not build the package:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        void Install()
        {
            if (tfFolder == null)
            {
                MessageBox.Show(this, "Pick your Team Fortress 2\\tf\\custom folder first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                BrowseTfFolder();
                if (tfFolder == null) return;
            }
            var files = BuildFiles();
            if (files == null) return;
            string custom = Tf2Locator.CustomFolder(tfFolder);
            string target = Path.Combine(custom, Packager.FolderName);
            if (Packager.InstallFolderExists(custom) && !Packager.OwnsInstallFolder(custom))
            {
                var r = MessageBox.Show(this,
                    target + "\n\nalready exists and was not written by this app (probably a cfg.tf package). " +
                    "Replace its weapon scripts and crosshair textures with your current setup?",
                    "Replace existing folder?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
            try
            {
                Directory.CreateDirectory(custom);
                Packager.WriteToCustom(files, custom);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not write into the TF2 folder:\n" + ex.Message +
                    "\n\nTry running this app as administrator, or use \"Save ZIP\" and extract it into tf\\custom yourself.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            int count = assignments.Count;
            SetStatus("Installed crosshairs for " + count + " weapon" + (count == 1 ? "" : "s") + " into " + target + ". Restart TF2 if it is running.");
            CheckCrosshairCvar();
        }

        void SaveZip()
        {
            var files = BuildFiles();
            if (files == null) return;
            using (var dlg = new SaveFileDialog { Title = "Save crosshair package", Filter = "ZIP archive (*.zip)|*.zip", FileName = "crosshairs.zip" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Packager.WriteZip(files, dlg.FileName);
                    SetStatus("Saved " + dlg.FileName + ". Extract it into Team Fortress 2\\tf\\custom so you get tf\\custom\\crosshairs.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the ZIP:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void OpenCustomFolder()
        {
            if (tfFolder == null) { BrowseTfFolder(); if (tfFolder == null) return; }
            string custom = Tf2Locator.CustomFolder(tfFolder);
            try
            {
                Directory.CreateDirectory(custom);
                Process.Start("explorer.exe", "\"" + custom + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------ TF2 folder

        void BrowseTfFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Select your Team Fortress 2\\tf\\custom folder (the tf or Team Fortress 2 folder above it works too)", ShowNewFolderButton = false })
            {
                if (tfFolder != null) dlg.SelectedPath = Tf2Locator.CustomFolder(tfFolder);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var tf = Tf2Locator.NormalizeTfFolder(dlg.SelectedPath);
                if (tf == null)
                {
                    MessageBox.Show(this, "That does not look like a TF2 folder. Pick the tf\\custom folder of your TF2 install, for example:\n" +
                        "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Team Fortress 2\\tf\\custom", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                tfFolder = tf;
                profile.TfFolder = tf;
                UpdateTfFolderUi();
                SaveProfile();
            }
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
            if (bad)
                warnLabel.Text = "Heads-up: your config.cfg has cl_crosshair_file \"" + value + "\". Set the crosshair to None in TF2's options " +
                                 "(or run cl_crosshair_file \"\" in the console) or weapon crosshairs will not show.";
        }

        // ------------------------------------------------------------------ misc

        Profile SafeLoadProfile()
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

        void UpdateStatusCount()
        {
            int n = assignments.Count;
            SetStatus(n == 0 ? "No weapons assigned yet." : n + " weapon" + (n == 1 ? "" : "s") + " assigned.");
        }

        void SetStatus(string text) => statusLabel.Text = text;

        void ShowHelp()
        {
            MessageBox.Show(this,
                "1. Select one or more weapons in the list (Ctrl/Shift-click for several).\n" +
                "2. Click a crosshair on the right. It is assigned immediately; the preview shows it over a game screenshot.\n" +
                "3. Size changes how big the crosshair is drawn in game. Explosive weapons can also swap the explosion effect for a smoke-free one.\n" +
                "4. Click \"Install into TF2\" to write tf\\custom\\crosshairs, or \"Save ZIP\" to share the package.\n" +
                "5. In TF2 set the crosshair to None (cl_crosshair_file \"\"), otherwise the game draws that one instead.\n" +
                "   The crosshair colour still comes from TF2's crosshair colour settings (cl_crosshair_red / green / blue).\n\n" +
                "Weapons you do not assign keep their stock crosshair. Your setup is saved automatically and restored next time.\n" +
                "Custom .vtf crosshairs can be added with the button on the right; they are copied into the package.\n\n" +
                "This mirrors the cfg.tf weapon-specific crosshair tool, with the weapons it was missing added.",
                "How it works", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

    }
}
