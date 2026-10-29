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
        readonly Profile profile;
        string? tfFolder;
        bool updatingUi;

        readonly WeaponListView weaponList = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            MultiSelect = true, ShowGroups = false, ShowItemToolTips = true,
        };
        readonly TextBox filterBox = new() { Dock = DockStyle.Fill };
        readonly Gallery gallery = new() { Dock = DockStyle.Fill };
        readonly ImageList smallImages = new() { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
        readonly PreviewPanel preview = new() { Dock = DockStyle.Fill };
        readonly NumericUpDown sizeBox = new() { Minimum = 8, Maximum = 512, Value = 64, Width = 64 };
        readonly ComboBox explosionBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        readonly ComboBox zoomBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64 };
        readonly Label selectedLabel = new() { AutoSize = false, Size = new Size(300, 36), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        readonly TextBox tfBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
        readonly Label warnLabel = new() { AutoSize = true, ForeColor = Color.Firebrick, Visible = false, Dock = DockStyle.Fill };
        readonly Label statusLabel = new() { AutoSize = true, Padding = new Padding(4, 7, 0, 0) };
        readonly CheckBox darkBox = new() { Text = "Dark mode", AutoSize = true, Checked = true, Margin = new Padding(10, 5, 0, 0) };

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
            LoadWeapons();
            LoadBundledCrosshairs();
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
            var top = BuildTopBar();
            var bottom = BuildBottomBar();
            var left = BuildWeaponPanel();
            gallery.SelectionChanged += (s, e) => OnGallerySelectionChanged();
            var settings = BuildSettingsPanel();

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

        /// <summary>Intro text, the install folder row and the cl_crosshair_file warning.</summary>
        Control BuildTopBar()
        {
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(10, 8, 10, 4) };
            AddColumns(top, SizeType.Percent);
            top.Controls.Add(new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = "Pick one or more weapons on the left (click a class name to take the whole class), then click a crosshair on the right to assign it. " +
                       "Weapons you leave alone keep their stock crosshair. When you are done, click \"Install into TF2\" " +
                       "(or save a ZIP to share). In TF2 the crosshair must be set to None / cl_crosshair_file \"\" for these to show."
            });

            var tfRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 5, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 0) };
            AddColumns(tfRow, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize);
            darkBox.CheckedChanged += (s, e) =>
            {
                profile.Dark = darkBox.Checked;
                ApplyTheme(darkBox.Checked);
                SaveProfile();
            };
            AddCells(tfRow, 0, Caption("Install folder:"), tfBox, MakeButton("Browse...", BrowseTfFolder), MakeButton("How it works", ShowHelp), darkBox);
            top.Controls.Add(tfRow);
            top.Controls.Add(warnLabel);
            return top;
        }

        /// <summary>Install / ZIP / folder / reset buttons and the status text.</summary>
        Control BuildBottomBar()
        {
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 4, 8, 6), WrapContents = false };
            var resetAllBtn = MakeButton("Reset ALL weapons", ClearAll, padded: true);
            resetAllBtn.Margin = new Padding(14, 3, 3, 3);
            bottom.Controls.Add(MakeButton("Install into TF2", Install, padded: true));
            bottom.Controls.Add(MakeButton("Save ZIP...", SaveZip, padded: true));
            bottom.Controls.Add(MakeButton("Open custom folder", OpenCustomFolder, padded: true));
            bottom.Controls.Add(resetAllBtn);
            bottom.Controls.Add(statusLabel);
            return bottom;
        }

        /// <summary>Filter box over the weapon list.</summary>
        Control BuildWeaponPanel()
        {
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            var filterRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 0, 0, 4) };
            AddColumns(filterRow, SizeType.AutoSize, SizeType.Percent);
            filterBox.TextChanged += (s, e) => PopulateWeapons();
            AddCells(filterRow, 0, Caption("Filter:", left: 4), filterBox);
            weaponList.SmallImageList = smallImages;
            weaponList.Columns.Add("Weapon", 390);
            weaponList.Columns.Add("Crosshair", 135);
            weaponList.Columns.Add("Size", 40, HorizontalAlignment.Right);
            weaponList.Columns.Add("Explosion", 100);
            weaponList.SelectedIndexChanged += (s, e) => OnWeaponSelectionChanged();
            weaponList.HeadingClicked += SelectClass;
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
            return left;
        }

        /// <summary>The preview with the size / explosion / zoom settings and the crosshair buttons beside it.</summary>
        Control BuildSettingsPanel()
        {
            var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(4) };
            AddColumns(settings, SizeType.Percent, SizeType.AutoSize);
            preview.Background = Assets.PreviewBackground();
            settings.Controls.Add(preview, 0, 0);

            var side = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(8, 0, 0, 0) };
            AddColumns(side, SizeType.AutoSize, SizeType.AutoSize);
            side.Controls.Add(selectedLabel, 0, 0);
            side.SetColumnSpan(selectedLabel, 2);
            sizeBox.ValueChanged += (s, e) => OnSizeChanged();
            AddCells(side, 1, Caption("Size (px):"), sizeBox);
            foreach (var o in ExplosionEffects.Options) explosionBox.Items.Add(o.Value);
            explosionBox.SelectedIndex = 0;
            explosionBox.Enabled = false;
            explosionBox.SelectedIndexChanged += (s, e) => OnExplosionChanged();
            AddCells(side, 2, Caption("Explosion:"), explosionBox);
            zoomBox.Items.AddRange(["1x", "2x", "3x", "4x"]);
            zoomBox.SelectedIndex = 1;
            zoomBox.SelectedIndexChanged += (s, e) => { preview.Zoom = zoomBox.SelectedIndex + 1; preview.Invalidate(); };
            AddCells(side, 3, Caption("Preview zoom:"), zoomBox);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 8, 0, 0) };
            foreach (var b in new[]
            {
                MakeButton("Use this crosshair for ALL weapons", ApplyToAll),
                MakeButton("Reset selected weapons to stock", ClearSelected),
                MakeButton("Add custom crosshair (.vtf)...", AddCustomCrosshairs),
            })
            {
                b.Width = 230;
                b.Margin = new Padding(0, 2, 0, 2);
                buttons.Controls.Add(b);
            }
            side.Controls.Add(buttons, 0, 4);
            side.SetColumnSpan(buttons, 2);
            settings.Controls.Add(side, 1, 0);
            return settings;
        }

        static Label Caption(string text, int left = 0) =>
            new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(left, 6, 6, 0) };

        static Button MakeButton(string text, Action onClick, bool padded = false)
        {
            var b = new Button { Text = text, AutoSize = true };
            if (padded) b.Padding = new Padding(6, 2, 6, 2);
            b.Click += (s, e) => onClick();
            return b;
        }

        /// <summary>One column style per entry: a Percent column takes all the spare width, AutoSize ones fit their content.</summary>
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
            UpdateTfFolderUi();

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
            RefreshRows();
            UpdateStatusCount();
        }

        // ------------------------------------------------------------------ list population

        void PopulateGallery() => WithoutEvents(() =>
        {
            smallImages.Images.Clear();
            smallImages.Images.Add("none", new Bitmap(20, 20, PixelFormat.Format32bppArgb));

            // keep the catalog's pack order, custom crosshairs first
            var items = new List<GalleryItem>();
            foreach (var c in crosshairs.GroupBy(c => c.Pack).OrderBy(g => g.Key == "Custom" ? -1 : 0).SelectMany(g => g))
            {
                smallImages.Images.Add(c.Name, MakeThumb(c.Image, 20));
                items.Add(new GalleryItem { Name = c.Name, Group = c.Pack, Image = MakeThumb(c.Image, Cell), Tag = c });
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

        void PopulateWeapons()
        {
            var selected = new HashSet<string>(SelectedWeapons().Select(w => w.Script), StringComparer.OrdinalIgnoreCase);
            string filter = filterBox.Text.Trim();
            bool Matches(WeaponInfo w) => filter.Length == 0 ||
                w.Names.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.Script.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.Group.Contains(filter, StringComparison.OrdinalIgnoreCase);

            WithoutEvents(() =>
            {
                weaponList.BeginUpdate();
                weaponList.Items.Clear();
                foreach (var g in weapons.Select(w => w.Group).Distinct().OrderBy(GroupRank))
                {
                    var rows = weapons.Where(w => w.Group == g && Matches(w)).ToList();
                    if (rows.Count == 0) continue;
                    weaponList.Items.Add(new ListViewItem([g, "", "", ""]) { Tag = null, ImageKey = "none", ToolTipText = "Click to select every weapon under " + g });
                    foreach (var w in rows)
                    {
                        var item = new ListViewItem([w.Names, "", "", ""]) { Tag = w, ToolTipText = w.Names + "\n(" + w.Script + ".txt)", UseItemStyleForSubItems = false, IndentCount = 1 };
                        item.Selected = selected.Contains(w.Script);
                        weaponList.Items.Add(item);
                        FillRow(item);
                    }
                }
                weaponList.EndUpdate();
            });
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
            int fixedWidth = weaponList.Columns.Cast<ColumnHeader>().Skip(1).Sum(c => c.Width);
            weaponList.Columns[0].Width = Math.Max(240, weaponList.ClientSize.Width - fixedWidth - 4);
        }

        void FillRow(ListViewItem item)
        {
            if (item.Tag is not WeaponInfo w) return; // class heading row
            var a = assignments.TryGetValue(w.Script, out var found) && FindCrosshair(found.Crosshair) != null ? found : null;
            Color fore = Theme.Current.Fore, muted = Theme.Current.Muted;
            item.ImageKey = a?.Crosshair ?? "none";
            item.SubItems[0].ForeColor = fore;
            item.SubItems[1].Text = a?.Crosshair ?? "stock";
            item.SubItems[1].ForeColor = a != null ? fore : muted;
            item.SubItems[2].Text = a?.Size.ToString() ?? "";
            item.SubItems[2].ForeColor = fore;
            item.SubItems[3].Text = !w.Explosive || a == null ? "" : a.Explosion.Length == 0 ? "Default" : ExplosionEffects.Label(a.Explosion).Replace(" (no smoke)", "");
            item.SubItems[3].ForeColor = a != null && a.Explosion.Length > 0 ? fore : muted;
        }

        void RefreshRows()
        {
            foreach (ListViewItem item in weaponList.Items) FillRow(item);
        }

        // ------------------------------------------------------------------ selection handling

        List<WeaponInfo> SelectedWeapons() => weaponList.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<WeaponInfo>().ToList();

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
                // a class heading reached with the keyboard (arrows, Ctrl+A) stands for the weapons under it
                foreach (var h in weaponList.SelectedItems.Cast<ListViewItem>().Where(i => i.Tag == null).ToList())
                {
                    h.Selected = false;
                    foreach (var it in ClassRows(h)) it.Selected = true;
                }
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

        /// <summary>The weapon rows listed under a class heading.</summary>
        IEnumerable<ListViewItem> ClassRows(ListViewItem heading)
        {
            for (int i = heading.Index + 1; i < weaponList.Items.Count && weaponList.Items[i].Tag != null; i++)
                yield return weaponList.Items[i];
        }

        /// <summary>Clicking a class heading selects the whole class; Ctrl/Shift-click adds it to the current selection.</summary>
        void SelectClass(ListViewItem heading, bool add)
        {
            WithoutEvents(() =>
            {
                weaponList.BeginUpdate();
                if (!add)
                    foreach (var it in weaponList.SelectedItems.Cast<ListViewItem>().ToList()) it.Selected = false;
                foreach (var it in ClassRows(heading)) it.Selected = true;
                weaponList.EndUpdate();
            });
            OnWeaponSelectionChanged();
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
                if (weaponList.SelectedItems.Count > 0)
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
                : who + " \u2192 " + c.Name;
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
                "1. Select one or more weapons in the list (Ctrl/Shift-click for several, or click a class name to select the whole class).\n" +
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
