using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;
using Size = System.Drawing.Size;

namespace TPL
{
    public partial class MainForm : Form
    {
        private ComboBox cbPrinters, cbPapers, cbStyles, cbOrd1, cbOrd2, cbBase;
        private RadioButton rbAll, rbCurrent, rbSelect;
        private RadioButton rbBlockMode, rbLayerMode;
        private TextBox txtFuzz, txtPath, txtBlocks, txtLayers, txtFileName;
        private CheckBox chkMark, chkMergePdf, chkOpenPdf, chkConvertImage;
        private RadioButton rbPng, rbJpg;
        private TextBox txtDpi;
        private Label lblCount;
        private Button btnPlot, btnCancel, btnBrowsePath, btnSelectBlock, btnSelectLayer, btnSelectManual;

        public PlotHelper.PlotSettingsData Settings { get; private set; }
        public bool IsPlotConfirmed { get; private set; }

        private List<ObjectId> tempManualSelectionIds = new List<ObjectId>();
        private List<DBObject> transientObjects = new List<DBObject>();
        private bool isInitializing = true;
        private System.Windows.Forms.Timer _previewDebounce;
        private Document _markerDoc;
        private Document _selectionDoc; // doc in which the current selection was made
        private Dictionary<Document, List<DBObject>> _pendingTransients = new Dictionary<Document, List<DBObject>>();

        public MainForm()
        {
            L10n.Init();
            _previewDebounce = new System.Windows.Forms.Timer { Interval = 600 };
            _previewDebounce.Tick += (s, e) => { _previewDebounce.Stop(); UpdatePreview(); };
            Application.DocumentManager.DocumentActivated += DocumentManager_DocumentActivated;
            InitializeComponent();
            // Apply icon
            try
            {
                using (var bmp = new Bitmap(32, 32))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(0, 122, 204));
                    using (var f = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var b = new SolidBrush(Color.White))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString("P", f, b, new RectangleF(0, 0, 32, 32), sf);
                    }
                    IntPtr hIcon = bmp.GetHicon();
                    this.Icon = System.Drawing.Icon.FromHandle(hIcon);
                }
            }
            catch { }
            // Apply dark mode
            ThemeManager.Apply(this, ThemeManager.IsDarkMode());
            LoadData();
            isInitializing = false;
            UpdatePreview();
        }

        private Label CreateHeader(string text, int x, int y, int width)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            lbl.ForeColor = Color.FromArgb(0, 102, 204);
            lbl.SetBounds(x, y, width, 25);
            return lbl;
        }

        private void StyleButton(Button btn)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.BackColor = Color.WhiteSmoke;
            btn.FlatAppearance.BorderColor = Color.Silver;
            btn.Cursor = Cursors.Hand;
        }

        private Bitmap GetWrenchIcon()
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.DimGray, 2f))
                {
                    g.DrawLine(p, 4, 12, 10, 6);
                    g.DrawEllipse(p, 9, 3, 5, 5);
                    g.FillEllipse(Brushes.WhiteSmoke, 10, 4, 3, 3);
                    g.DrawArc(p, 2, 10, 4, 4, 45, 180);
                }
            }
            return bmp;
        }

        private void InitializeComponent()
        {
            this.Text = L10n.T("app_title");
            this.Font = new Font("Segoe UI", 9F);
            this.BackColor = Color.White;
            this.MinimumSize = new Size(570, 440);
            this.ClientSize = new Size(570, 440);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int col1 = 15, col2 = 320;

            // ================= COL 1 =================
            int y = 10;
            this.Controls.Add(CreateHeader(L10n.T("header_printer"), col1, y, 290));
            y += 26;
            this.Controls.Add(new Label { Text = L10n.T("label_printer"), Left = col1, Top = y + 4, AutoSize = true, ForeColor = Color.DimGray });
            cbPrinters = new ComboBox { Left = col1 + 80, Top = y, Width = 210, DropDownStyle = ComboBoxStyle.DropDownList };
            cbPrinters.SelectedIndexChanged += (s, e) =>
            {
                if (cbPrinters.SelectedItem != null)
                {
                    var papers = PlotHelper.GetPaperSizes(cbPrinters.SelectedItem.ToString());
                    cbPapers.Items.Clear();
                    cbPapers.Items.AddRange(papers.ToArray());
                    if (cbPapers.Items.Count > 0) cbPapers.SelectedIndex = 0;
                }
            };
            this.Controls.Add(cbPrinters);

            y += 30;
            this.Controls.Add(new Label { Text = L10n.T("label_paper"), Left = col1, Top = y + 4, AutoSize = true, ForeColor = Color.DimGray });
            cbPapers = new ComboBox { Left = col1 + 80, Top = y, Width = 210, DropDownStyle = ComboBoxStyle.DropDownList };
            this.Controls.Add(cbPapers);

            y += 30;
            this.Controls.Add(new Label { Text = L10n.T("label_style"), Left = col1, Top = y + 4, AutoSize = true, ForeColor = Color.DimGray });
            cbStyles = new ComboBox { Left = col1 + 80, Top = y, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            Button btnEditStyle = new Button { Left = col1 + 265, Top = y - 1, Width = 25, Height = 23 };
            StyleButton(btnEditStyle);
            try
            {
                btnEditStyle.Image = GetWrenchIcon();
            }
            catch { btnEditStyle.Text = "*"; }

            btnEditStyle.Click += (s, e) =>
            {
                string styleName = cbStyles.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(styleName))
                {
                    try
                    {
                        string styleDir = "";
                        var doc = Application.DocumentManager.MdiActiveDocument;
                        if (doc != null)
                        {
                            using (doc.LockDocument())
                            {
                                styleDir = System.IO.Path.Combine((string)Application.GetSystemVariable("ROAMABLEROOTPREFIX"), @"Plotters\Plot Styles");
                            }
                        }
                        string path = Path.Combine(styleDir, styleName);
                        if (!File.Exists(path))
                        {
                            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                            string altPath = Path.Combine(roaming, @"Autodesk\AutoCAD 2021\R24.0\enu\Plotters\Plot Styles", styleName);
                            if (File.Exists(altPath)) path = altPath;
                        }

                        if (File.Exists(path))
                        {
                            string acadExe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                            string acadDir = Path.GetDirectoryName(acadExe);
                            string styExe = Path.Combine(acadDir, "styexe.exe");
                            if (File.Exists(styExe))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = styExe,
                                    Arguments = "\"" + path + "\"",
                                    UseShellExecute = false
                                });
                            }
                            else
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = path,
                                    UseShellExecute = true
                                });
                            }
                        }
                        else
                        {
                            System.Windows.Forms.MessageBox.Show("Khong tim thay file: " + path, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.Forms.MessageBox.Show("Loi khi mo Editor: " + ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    }
                }
            };
            this.Controls.Add(cbStyles);
            this.Controls.Add(btnEditStyle);

            y += 40;
            this.Controls.Add(CreateHeader(L10n.T("header_frame"), col1, y, 290));
            y += 26;
            rbBlockMode = new RadioButton { Text = L10n.T("label_block"), Left = col1, Top = y + 1, Width = 60, Checked = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            rbBlockMode.CheckedChanged += ModeChanged;
            txtBlocks = new TextBox { Left = col1 + 60, Top = y, Width = 170, ReadOnly = true, BackColor = Color.WhiteSmoke };
            btnSelectBlock = new Button { Text = L10n.T("btn_select"), Left = col1 + 235, Top = y - 1, Width = 55, Height = 25 };
            StyleButton(btnSelectBlock);
            btnSelectBlock.Click += BtnSelectBlock_Click;
            this.Controls.Add(rbBlockMode);
            this.Controls.Add(txtBlocks);
            this.Controls.Add(btnSelectBlock);

            y += 30;
            rbLayerMode = new RadioButton { Text = L10n.T("label_layer"), Left = col1, Top = y + 1, Width = 60, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            rbLayerMode.CheckedChanged += ModeChanged;
            txtLayers = new TextBox { Left = col1 + 60, Top = y, Width = 170, ReadOnly = true, BackColor = Color.WhiteSmoke };
            btnSelectLayer = new Button { Text = L10n.T("btn_select"), Left = col1 + 235, Top = y - 1, Width = 55, Height = 25 };
            StyleButton(btnSelectLayer);
            btnSelectLayer.Click += BtnSelectLayer_Click;
            this.Controls.Add(rbLayerMode);
            this.Controls.Add(txtLayers);
            this.Controls.Add(btnSelectLayer);

            y += 40;
            this.Controls.Add(CreateHeader(L10n.T("header_save"), col1, y, 290));

            y += 26;
            this.Controls.Add(new Label { Text = L10n.T("label_basename"), Left = col1, Top = y + 4, AutoSize = true, ForeColor = Color.DimGray });
            txtFileName = new TextBox { Left = col1 + 80, Top = y, Width = 210 };
            this.Controls.Add(txtFileName);

            y += 30;
            this.Controls.Add(new Label { Text = L10n.T("label_folder"), Left = col1, Top = y + 4, AutoSize = true, ForeColor = Color.DimGray });
            txtPath = new TextBox { Left = col1 + 80, Top = y, Width = 150 };
            btnBrowsePath = new Button { Text = L10n.T("btn_browse"), Left = col1 + 235, Top = y - 1, Width = 55, Height = 25 };
            StyleButton(btnBrowsePath);
            btnBrowsePath.Click += BtnBrowsePath_Click;
            this.Controls.Add(txtPath);
            this.Controls.Add(btnBrowsePath);

            y += 30;
            // Row 1
            chkMergePdf = new CheckBox { Text = L10n.T("chk_merge"), Left = col1 + 10, Top = y, Width = 90, Checked = true, ForeColor = Color.Green, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            chkConvertImage = new CheckBox { Text = "Convert to Image", Left = col1 + 120, Top = y, Width = 140, Checked = false, ForeColor = Color.FromArgb(0, 122, 204), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            
            this.Controls.Add(chkMergePdf);
            this.Controls.Add(chkConvertImage);

            y += 25;
            // Row 2
            chkOpenPdf = new CheckBox { Text = L10n.T("chk_open"), Left = col1 + 10, Top = y, Width = 120, Checked = true, ForeColor = Color.DimGray };
            
            Panel pnlImgFormat = new Panel { Left = col1 + 120, Top = y - 2, Width = 170, Height = 25 };
            rbPng = new RadioButton { Text = "PNG", Left = 0, Top = 2, Width = 50, Checked = true };
            rbJpg = new RadioButton { Text = "JPG", Left = 50, Top = 2, Width = 45 };
            Label lblDpi = new Label { Text = "DPI:", Left = 95, Top = 4, Width = 30 };
            txtDpi = new TextBox { Text = "600", Left = 125, Top = 1, Width = 40 };
            
            pnlImgFormat.Controls.Add(rbPng);
            pnlImgFormat.Controls.Add(rbJpg);
            pnlImgFormat.Controls.Add(lblDpi);
            pnlImgFormat.Controls.Add(txtDpi);

            this.Controls.Add(chkOpenPdf);
            this.Controls.Add(pnlImgFormat);

            // Mutual exclusion logic
            chkMergePdf.CheckedChanged += (s, e) => { if (chkMergePdf.Checked) chkConvertImage.Checked = false; };
            chkConvertImage.CheckedChanged += (s, e) => 
            { 
                if (chkConvertImage.Checked) chkMergePdf.Checked = false; 
                pnlImgFormat.Enabled = chkConvertImage.Checked;
            };
            pnlImgFormat.Enabled = false; // default state

            // ================= COL 2 =================
            y = 10;
            this.Controls.Add(CreateHeader(L10n.T("header_scope"), col2, y, 220));

            Panel pnlScope = new Panel { Left = col2, Top = y + 26, Width = 230, Height = 80, BackColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle };
            rbAll = new RadioButton { Text = L10n.T("rb_all"), Left = 10, Top = 5, Width = 180 };
            rbCurrent = new RadioButton { Text = L10n.T("rb_current"), Left = 10, Top = 28, Width = 120, Checked = true };
            rbSelect = new RadioButton { Text = L10n.T("rb_manual"), Left = 10, Top = 51, Width = 110 };
            btnSelectManual = new Button { Text = L10n.T("btn_select"), Left = 125, Top = 52, Width = 65, Height = 22 };
            StyleButton(btnSelectManual);
            btnSelectManual.Click += BtnSelectManual_Click;

            rbAll.CheckedChanged += TriggerPreview;
            rbCurrent.CheckedChanged += TriggerPreview;
            rbSelect.CheckedChanged += TriggerPreview;

            pnlScope.Controls.Add(rbAll);
            pnlScope.Controls.Add(rbCurrent);
            pnlScope.Controls.Add(rbSelect);
            pnlScope.Controls.Add(btnSelectManual);
            this.Controls.Add(pnlScope);

            y += 115;
            this.Controls.Add(CreateHeader(L10n.T("header_sort"), col2, y, 220));
            Panel pnlSort = new Panel { Left = col2, Top = y + 26, Width = 230, Height = 150, BackColor = Color.WhiteSmoke, BorderStyle = BorderStyle.FixedSingle };

            pnlSort.Controls.Add(new Label { Text = L10n.T("label_ord1"), Left = 10, Top = 8, AutoSize = true });
            cbOrd1 = new ComboBox { Left = 90, Top = 5, Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
            cbOrd1.Items.AddRange(new[] { L10n.T("sort_lr"), L10n.T("sort_rl"), L10n.T("sort_tb"), L10n.T("sort_bt"), L10n.T("sort_sel"), L10n.T("sort_mark") });
            cbOrd1.SelectedIndex = 0;

            pnlSort.Controls.Add(new Label { Text = L10n.T("label_ord2"), Left = 10, Top = 38, AutoSize = true });
            cbOrd2 = new ComboBox { Left = 90, Top = 35, Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
            cbOrd2.Items.AddRange(new[] { L10n.T("sort_none"), L10n.T("sort_lr"), L10n.T("sort_rl"), L10n.T("sort_tb"), L10n.T("sort_bt") });
            cbOrd2.SelectedIndex = 3; // Default: Trên -> Dưới

            pnlSort.Controls.Add(new Label { Text = L10n.T("label_anchor"), Left = 10, Top = 68, AutoSize = true });
            cbBase = new ComboBox { Left = 90, Top = 65, Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
            cbBase.Items.AddRange(new[] { L10n.T("anchor_bl"), L10n.T("anchor_br"), L10n.T("anchor_tl"), L10n.T("anchor_tr") });
            cbBase.SelectedIndex = 0;

            pnlSort.Controls.Add(new Label { Text = L10n.T("label_fuzz"), Left = 10, Top = 98, AutoSize = true });
            txtFuzz = new TextBox { Left = 90, Top = 95, Width = 125, Text = "100" };

            chkMark = new CheckBox { Text = L10n.T("chk_mark"), Left = 10, Top = 122, Width = 130 };
            chkMark.CheckedChanged += TriggerPreview;

            Button btnDeleteMarks = new Button { Text = L10n.T("btn_delete_marks"), Left = 150, Top = 122, Width = 55, Height = 23 };
            StyleButton(btnDeleteMarks);
            btnDeleteMarks.Click += (s, e) =>
            {
                chkMark.Checked = false;
                ClearPermanentMarkers();
                ClearTransientMarkers();
            };

            // Trigger preview when sorting options change to update markers immediately
            cbOrd1.SelectedIndexChanged += TriggerPreview;
            cbOrd2.SelectedIndexChanged += TriggerPreview;
            cbBase.SelectedIndexChanged += TriggerPreview;
            txtFuzz.Leave += TriggerPreview;

            pnlSort.Controls.Add(cbOrd1);
            pnlSort.Controls.Add(cbOrd2);
            pnlSort.Controls.Add(cbBase);
            pnlSort.Controls.Add(txtFuzz);
            pnlSort.Controls.Add(chkMark);
            pnlSort.Controls.Add(btnDeleteMarks);
            this.Controls.Add(pnlSort);

            // ================= BOTTOM =================
            lblCount = new Label { Text = L10n.T("lbl_ready"), Left = 15, Top = 390, Width = 250, Font = new Font("Segoe UI", 9F, FontStyle.Italic), ForeColor = Color.DimGray };
            this.Controls.Add(lblCount);

            int btnY = 380;
            btnPlot = new Button { Text = L10n.T("btn_plot"), Left = 320, Top = btnY, Width = 130, Height = 40 };
            btnPlot.BackColor = Color.FromArgb(0, 122, 204);
            btnPlot.ForeColor = Color.White;
            btnPlot.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            btnPlot.FlatStyle = FlatStyle.Flat;
            btnPlot.FlatAppearance.BorderSize = 0;
            btnPlot.Cursor = Cursors.Hand;
            btnPlot.Click += BtnPlot_Click;

            btnCancel = new Button { Text = L10n.T("btn_cancel"), Left = 460, Top = btnY, Width = 90, Height = 40 };
            StyleButton(btnCancel);
            btnCancel.Click += (s, e) => this.Close();

            this.Controls.Add(btnPlot);
            this.Controls.Add(btnCancel);

            ModeChanged(null, null);
        }

        private void ModeChanged(object sender, EventArgs e)
        {
            txtBlocks.Enabled = rbBlockMode.Checked;
            btnSelectBlock.Enabled = rbBlockMode.Checked;
            txtLayers.Enabled = rbLayerMode.Checked;
            btnSelectLayer.Enabled = rbLayerMode.Checked;
            TriggerPreview(null, null);
        }

        private void TriggerPreview(object sender, EventArgs e)
        {
            if (isInitializing) return;
            // Debounce: reset timer so rapid changes only trigger one scan
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        private void DocumentManager_DocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document == null) return;
            if (_pendingTransients.TryGetValue(e.Document, out var list))
            {
                try
                {
                    var tm = TransientManager.CurrentTransientManager;
                    foreach (var obj in list)
                    {
                        try { if (obj != null && !obj.IsDisposed) { tm.EraseTransient(obj, new IntegerCollection()); obj.Dispose(); } } catch { }
                    }
                }
                catch { }
                _pendingTransients.Remove(e.Document);
            }
        }

        private void ClearPermanentMarkers()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || doc.IsDisposed) return;
            try
            {
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId btrId in bt)
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                        if (btr.IsLayout)
                        {
                            foreach (ObjectId entId in btr)
                            {
                                try
                                {
                                    Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                                    if (ent != null && string.Equals(ent.Layer, "TPL_MARKERS", StringComparison.OrdinalIgnoreCase))
                                    {
                                        ent.UpgradeOpen();
                                        ent.Erase();
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    tr.Commit();
                }
                doc.Editor.UpdateScreen();
            }
            catch { }
        }

        private void ClearTransientMarkers()
        {
            try
            {
                if (transientObjects.Count == 0) return;
                Document currentDoc = Application.DocumentManager.MdiActiveDocument;

                if (_markerDoc != null && currentDoc != null && _markerDoc != currentDoc && !_markerDoc.IsDisposed)
                {
                    if (!_pendingTransients.ContainsKey(_markerDoc))
                        _pendingTransients[_markerDoc] = new List<DBObject>();
                    _pendingTransients[_markerDoc].AddRange(transientObjects);
                    transientObjects.Clear();
                    return;
                }

                if (currentDoc != null)
                {
                    using (DocumentLock docLock = currentDoc.LockDocument())
                    {
                        var tm = TransientManager.CurrentTransientManager;
                        foreach (var obj in transientObjects)
                        {
                            try { if (obj != null && !obj.IsDisposed) { tm.EraseTransient(obj, new IntegerCollection()); obj.Dispose(); } } catch { }
                        }
                    }
                }
                transientObjects.Clear();
            }
            catch { transientObjects.Clear(); }
        }

        private void DrawMarkersIfNeeded(List<PlotFrame> frames)
        {
            if (frames == null || frames.Count == 0) return;
            if (chkMark.Checked)
            {
                ClearTransientMarkers();
                DrawPermanentMarkers(frames);
            }
            else
            {
                ClearPermanentMarkers();
                DrawTransientMarkers(frames);
            }
        }

        private void DrawTransientMarkers(List<PlotFrame> frames)
        {
            ClearTransientMarkers();
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                using (DocumentLock docLock = doc.LockDocument())
                {
                    string curLayout = LayoutManager.Current.CurrentLayout;
                    var tm = TransientManager.CurrentTransientManager;
                    _markerDoc = doc;

                    var objs = new List<DBObject>();
                    for (int i = 0; i < frames.Count; i++)
                    {
                        var frame = frames[i];
                        if (frame.LayoutName != curLayout) continue;
                        double lenX = frame.Extents.MaxPoint.X - frame.Extents.MinPoint.X;
                        double lenY = frame.Extents.MaxPoint.Y - frame.Extents.MinPoint.Y;
                        if (lenX <= 0 || lenY <= 0) continue;

                        MText txt = new MText();
                        // Force Verdana font regardless of TextStyle
                        txt.Contents = "{\\fVerdana|b0|i0|c0|p0;" + (i + 1).ToString() + "}";
                        txt.TextHeight = Math.Min(lenX, lenY) / 5.0;
                        txt.Location = new Point3d(
                            (frame.Extents.MinPoint.X + frame.Extents.MaxPoint.X) / 2,
                            (frame.Extents.MinPoint.Y + frame.Extents.MaxPoint.Y) / 2, 0);
                        txt.Attachment = AttachmentPoint.MiddleCenter;
                        txt.ColorIndex = 1;
                        objs.Add(txt);

                        Point3d pMin = frame.Extents.MinPoint;
                        Point3d pMax = frame.Extents.MaxPoint;
                        Point3d topLeft = new Point3d(pMin.X, pMax.Y, pMin.Z);
                        Point3d bottomRight = new Point3d(pMax.X, pMin.Y, pMax.Z);
                        Line tLine = new Line(topLeft, bottomRight);
                        tLine.ColorIndex = 1;
                        objs.Add(tLine);
                    }

                    foreach (var obj in objs)
                    {
                        tm.AddTransient(obj, TransientDrawingMode.Main, 128, new IntegerCollection());
                        transientObjects.Add(obj);
                    }
                    doc.Editor.UpdateScreen();
                }
            }
            catch { ClearTransientMarkers(); }
        }

        private void DrawPermanentMarkers(List<PlotFrame> frames)
        {
            ClearPermanentMarkers();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                    if (!lt.Has("TPL_MARKERS"))
                    {
                        lt.UpgradeOpen();
                        LayerTableRecord ltr = new LayerTableRecord();
                        ltr.Name = "TPL_MARKERS";
                        ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);
                        ltr.IsPlottable = false;
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }
                    else
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt["TPL_MARKERS"], OpenMode.ForWrite);
                        ltr.IsPlottable = false;
                        ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1);
                    }

                    string curLayout = LayoutManager.Current.CurrentLayout;
                    ObjectId curBtrId = doc.Database.CurrentSpaceId;
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(curBtrId, OpenMode.ForWrite);

                    for (int i = 0; i < frames.Count; i++)
                    {
                        var frame = frames[i];
                        if (frame.LayoutName != curLayout) continue;

                        double lenX = frame.Extents.MaxPoint.X - frame.Extents.MinPoint.X;
                        double lenY = frame.Extents.MaxPoint.Y - frame.Extents.MinPoint.Y;
                        if (lenX <= 0 || lenY <= 0) continue;

                        Point3d pMin = frame.Extents.MinPoint;
                        Point3d pMax = frame.Extents.MaxPoint;

                        Point3d topLeft = new Point3d(pMin.X, pMax.Y, pMin.Z);
                        Point3d bottomRight = new Point3d(pMax.X, pMin.Y, pMax.Z);

                        Line line = new Line(topLeft, bottomRight);
                        line.Layer = "TPL_MARKERS";
                        line.ColorIndex = 1;
                        btr.AppendEntity(line);
                        tr.AddNewlyCreatedDBObject(line, true);

                        MText txt = new MText();
                        txt.Layer = "TPL_MARKERS";
                        txt.Contents = "{\\fVerdana|b0|i0|c0|p0;" + (i + 1).ToString() + "}";
                        txt.TextHeight = Math.Min(lenX, lenY) / 5.0;
                        txt.Location = new Point3d(
                            (frame.Extents.MinPoint.X + frame.Extents.MaxPoint.X) / 2,
                            (frame.Extents.MinPoint.Y + frame.Extents.MaxPoint.Y) / 2, 0);
                        txt.Attachment = AttachmentPoint.MiddleCenter;
                        txt.ColorIndex = 1;

                        btr.AppendEntity(txt);
                        tr.AddNewlyCreatedDBObject(txt, true);
                    }
                    tr.Commit();
                }
                doc.Editor.UpdateScreen();
            }
            catch { }
        }

        private PlotHelper.PlotSettingsData BuildCurrentSettings()
        {
            var data = new PlotHelper.PlotSettingsData();
            data.DeviceName = cbPrinters.SelectedItem?.ToString();
            data.PaperSize = cbPapers.SelectedItem?.ToString();
            data.PlotStyle = cbStyles.SelectedItem?.ToString() ?? "";
            data.OutputPath = txtPath.Text;

            data.FrameType = rbBlockMode.Checked ? PlotHelper.FrameType.Block : PlotHelper.FrameType.Polyline;
            string namesStr = rbBlockMode.Checked ? txtBlocks.Text : txtLayers.Text;
            data.FrameNames = namesStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();

            data.SelectionMode = rbAll.Checked ? PlotHelper.SelectionMode.AllLayouts : (rbSelect.Checked ? PlotHelper.SelectionMode.Manual : PlotHelper.SelectionMode.CurrentLayout);
            data.GroupOrder = (PlotHelper.SortOrder)cbOrd1.SelectedIndex;
            data.CrossGroupOrder = (PlotHelper.SortOrder)(cbOrd2.SelectedIndex == 0 ? 6 : cbOrd2.SelectedIndex - 1);
            data.SortBasePoint = (PlotHelper.BasePoint)cbBase.SelectedIndex;
            data.Fuzz = double.TryParse(txtFuzz.Text, out double f) ? f : 100;
            data.MarkPlotRegions = chkMark.Checked;
            data.MergePdfs = chkMergePdf.Checked;
            data.OpenPdf = chkOpenPdf.Checked;
            data.ConvertToImage = chkConvertImage.Checked;
            data.ImageFormat = rbJpg.Checked ? "JPG" : "PNG";
            data.ImageDpi = int.TryParse(txtDpi.Text, out int dpi) ? dpi : 300;
            data.BaseFileName = txtFileName.Text;
            data.ManualSelectionIds = new List<ObjectId>(tempManualSelectionIds);

            return data;
        }

        private void UpdatePreview()
        {
            // Check if user has switched to a different document since last selection
            Document activDoc = Application.DocumentManager.MdiActiveDocument;
            if (_selectionDoc != null && activDoc != null && _selectionDoc != activDoc)
            {
                // Stale selection from another document — clear everything
                tempManualSelectionIds.Clear();
                txtBlocks.Text = "";
                txtLayers.Text = "";
                _selectionDoc = null;
                ClearTransientMarkers();
                ClearPermanentMarkers();
            }

            bool hasTemplate = rbSelect.Checked
                ? tempManualSelectionIds.Count > 0
                : (rbBlockMode.Checked ? txtBlocks.Text : txtLayers.Text).Trim().Length > 0;

            if (!hasTemplate)
            {
                lblCount.Text = L10n.T("msg_no_frame");
                lblCount.ForeColor = Color.Red;
                ClearTransientMarkers();
                ClearPermanentMarkers();
            }
            else
            {
                var sm = BuildCurrentSettings();
                var frames = PlotLogic.SelectFrames(sm);
                PlotLogic.SortFrames(frames, sm);

                if (rbSelect.Checked)
                {
                    lblCount.Text = string.Format("{0}: {1}", L10n.T("rb_manual"), tempManualSelectionIds.Count);
                }
                else
                {
                    lblCount.Text = string.Format("{0}: {1}", L10n.T("header_frame"), frames.Count);
                }
                lblCount.ForeColor = frames.Count > 0 ? Color.Green : Color.Red;

                DrawMarkersIfNeeded(frames);
            }
        }

        private void BtnSelectBlock_Click(object sender, EventArgs e)
        {
            this.Hide();
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            try
            {
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = L10n.T("msg_sel_block");
                SelectionFilter filter = new SelectionFilter(new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") });
                PromptSelectionResult psr = ed.GetSelection(pso, filter);

                if (psr.Status == PromptStatus.OK)
                {
                    // Changing block template: clear previous selections and markers first
                    tempManualSelectionIds.Clear();
                    ClearTransientMarkers();
                    ClearPermanentMarkers();

                    HashSet<string> blockNames = new HashSet<string>();
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in psr.Value.GetObjectIds())
                        {
                            BlockReference br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                            string bName = br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
                            blockNames.Add(bName);
                        }
                    }
                    txtBlocks.Text = string.Join(", ", blockNames);
                    rbBlockMode.Checked = true;
                    _selectionDoc = doc;
                    UpdatePreview();
                }
            }
            finally
            {
                this.Show();
            }
        }

        private void BtnSelectLayer_Click(object sender, EventArgs e)
        {
            this.Hide();
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            try
            {
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = L10n.T("msg_sel_layer");
                SelectionFilter filter = new SelectionFilter(new TypedValue[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") });
                PromptSelectionResult psr = ed.GetSelection(pso, filter);

                if (psr.Status == PromptStatus.OK)
                {
                    // Changing layer template: clear previous selections and markers first
                    tempManualSelectionIds.Clear();
                    ClearTransientMarkers();
                    ClearPermanentMarkers();

                    HashSet<string> layerNames = new HashSet<string>();
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in psr.Value.GetObjectIds())
                        {
                            Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                            layerNames.Add(ent.Layer);
                        }
                    }
                    txtLayers.Text = string.Join(", ", layerNames);
                    rbLayerMode.Checked = true;
                    _selectionDoc = doc;
                    UpdatePreview();
                }
            }
            finally
            {
                this.Show();
            }
        }

        private void BtnSelectManual_Click(object sender, EventArgs e)
        {
            var dummySettings = BuildCurrentSettings();
            if (dummySettings.FrameNames.Count == 0)
            {
                MessageBox.Show(L10n.T("msg_need_sample"), L10n.T("warn_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.Hide();
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            try
            {
                TypedValue[] filter = PlotLogic.GetFilter(dummySettings);
                SelectionFilter selFilter = new SelectionFilter(filter);
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = L10n.T("msg_sel_frames");
                PromptSelectionResult psr = ed.GetSelection(pso, selFilter);

                if (psr.Status == PromptStatus.OK)
                {
                    tempManualSelectionIds.Clear();

                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in psr.Value.GetObjectIds())
                        {
                            if (dummySettings.FrameType == PlotHelper.FrameType.Block)
                            {
                                BlockReference br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                                string name = br.IsDynamicBlock ? ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name : br.Name;
                                if (dummySettings.FrameNames.Any(fn => string.Equals(name, fn, StringComparison.OrdinalIgnoreCase)))
                                {
                                    tempManualSelectionIds.Add(id);
                                }
                            }
                            else
                            {
                                tempManualSelectionIds.Add(id);
                            }
                        }
                    }
                }
            }
            finally
            {
                this.Show();
            }

            rbSelect.Checked = true;
            _selectionDoc = doc;
            UpdatePreview();
        }

        private void LoadData()
        {
            var printers = PlotHelper.GetPrinters();
            cbPrinters.Items.AddRange(printers.ToArray());

            var styles = PlotHelper.GetPlotStyles();
            cbStyles.Items.AddRange(styles.ToArray());

            if (Commands.LastSettings != null)
            {
                var ls = Commands.LastSettings;
                if (cbPrinters.Items.Contains(ls.DeviceName)) cbPrinters.SelectedItem = ls.DeviceName;
                if (cbPapers.Items.Contains(ls.PaperSize)) cbPapers.SelectedItem = ls.PaperSize;
                if (cbStyles.Items.Contains(ls.PlotStyle)) cbStyles.SelectedItem = ls.PlotStyle;

                txtPath.Text = ls.OutputPath;

                if (ls.FrameType == PlotHelper.FrameType.Block)
                {
                    rbBlockMode.Checked = true;
                    txtBlocks.Text = string.Join(", ", ls.FrameNames);
                }
                else
                {
                    rbLayerMode.Checked = true;
                    txtLayers.Text = string.Join(", ", ls.FrameNames);
                }

                rbAll.Checked = ls.SelectionMode == PlotHelper.SelectionMode.AllLayouts;
                rbCurrent.Checked = ls.SelectionMode == PlotHelper.SelectionMode.CurrentLayout;
                rbSelect.Checked = ls.SelectionMode == PlotHelper.SelectionMode.Manual;

                cbOrd1.SelectedIndex = (int)ls.GroupOrder;
                cbOrd2.SelectedIndex = ls.CrossGroupOrder == PlotHelper.SortOrder.None ? 0 : (int)ls.CrossGroupOrder + 1;
                cbBase.SelectedIndex = (int)ls.SortBasePoint;
                txtFuzz.Text = ls.Fuzz.ToString();
                chkMark.Checked = ls.MarkPlotRegions;
                chkMergePdf.Checked = ls.MergePdfs;
                chkOpenPdf.Checked = ls.OpenPdf;
                chkConvertImage.Checked = ls.ConvertToImage;
                if (ls.ImageFormat == "JPG") rbJpg.Checked = true; else rbPng.Checked = true;
                txtDpi.Text = ls.ImageDpi.ToString();
                txtFileName.Text = ls.BaseFileName;
            }
            else
            {
                txtFileName.Text = Path.GetFileNameWithoutExtension(Application.DocumentManager.MdiActiveDocument.Name);
                string defPrinter = "AutoCAD PDF (High Quality Print).pc3";
                string defPaper = "ISO full bleed A3 (420.00 x 297.00 MM)";
                string defStyle = "monochrome.ctb";

                int pIdx = cbPrinters.FindStringExact(defPrinter);
                if (pIdx >= 0) cbPrinters.SelectedIndex = pIdx;
                else if (cbPrinters.Items.Count > 0) cbPrinters.SelectedIndex = 0;

                int sIdx = cbStyles.FindStringExact(defStyle);
                if (sIdx >= 0) cbStyles.SelectedIndex = sIdx;
                else if (cbStyles.Items.Count > 0) cbStyles.SelectedIndex = 0;

                int ppIdx = cbPapers.FindStringExact(defPaper);
                if (ppIdx >= 0) cbPapers.SelectedIndex = ppIdx;

                txtPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }
        }

        private void BtnBrowsePath_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtPath.Text = fbd.SelectedPath;
                }
            }
        }

        private void BtnPlot_Click(object sender, EventArgs e)
        {
            Settings = BuildCurrentSettings();

            if (Settings.FrameNames.Count == 0)
            {
                MessageBox.Show(L10n.T("msg_no_frame"), L10n.T("err_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (Settings.SelectionMode == PlotHelper.SelectionMode.Manual && Settings.ManualSelectionIds.Count == 0)
            {
                MessageBox.Show(L10n.T("msg_no_manual"), L10n.T("warn_title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            IsPlotConfirmed = true;
            Commands.LastSettings = Settings;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            try
            {
                // SelectFrames uses ed.SelectAll() – must be OUTSIDE DocumentLock to avoid deadlock
                List<PlotFrame> frames = PlotLogic.SelectFrames(Settings);
                if (frames.Count == 0)
                {
                    MessageBox.Show(L10n.T("msg_no_result"), L10n.T("warn_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                PlotLogic.SortFrames(frames, Settings);

                // DocumentLock only wraps PlotAll (pure plot operations, no selection needed)
                using (DocumentLock docLock = doc.LockDocument())
                {
                    PlotLogic.PlotAll(frames, Settings);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(string.Format(L10n.T("msg_plot_error"), ex.Message), L10n.T("err_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _previewDebounce?.Stop();
            _previewDebounce?.Dispose();
            Application.DocumentManager.DocumentActivated -= DocumentManager_DocumentActivated;
            ClearTransientMarkers();
            ClearPermanentMarkers();
            base.OnFormClosed(e);
        }
    }
}
