using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GDocsDrop
{
    internal class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }
    }

    internal class DropWindow : Form
    {
        private static readonly Color BgWindow = Color.FromArgb(15, 17, 23);
        private static readonly Color BgCard = Color.FromArgb(22, 27, 34);
        private static readonly Color BgButton = Color.FromArgb(33, 38, 45);
        private static readonly Color BgHover = Color.FromArgb(28, 36, 48);
        private static readonly Color BorderHairline = Color.FromArgb(48, 54, 61);
        private static readonly Color GoogleBlue = Color.FromArgb(66, 133, 244);
        private static readonly Color GoogleGreen = Color.FromArgb(15, 157, 88);
        private static readonly Color GoogleYellow = Color.FromArgb(244, 180, 0);
        private static readonly Color AmberWarn = Color.FromArgb(245, 158, 11);
        private static readonly Color ErrorRed = Color.FromArgb(248, 81, 73);
        private static readonly Color TextPrimary = Color.FromArgb(240, 246, 252);
        private static readonly Color TextMuted = Color.FromArgb(139, 148, 158);

        private Button btnPinTop;
        private Label lblDrivePill;
        private DoubleBufferedPanel dropZonePanel;
        private Panel statusPanel;
        private Label lblStatus;
        private DoubleBufferedPanel progressBarPanel;
        private LinkLabel lnkStartDrive;
        private LinkLabel lnkOpenFolder;
        private ListBox recentList;
        private Button btnAssocToggle;
        private Button btnDriveFolder;

        private bool isDragHover;
        private bool isMouseHover;
        private bool isSuccessGlow;
        private bool isBusy;
        private int progressState;
        private int animTick;
        private System.Windows.Forms.Timer animTimer;
        private System.Windows.Forms.Timer driveCheckTimer;
        private List<HistoryEntry> historyItems;
        private DriveStatusInfo currentDriveStatus;
        private int hoveredListIndex = -1;

        public DropWindow()
        {
            historyItems = new List<HistoryEntry>();
            InitializeUi();
            RefreshDriveStatus();
            RefreshAssocButton();
            ReloadHistory();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyDarkTitleBar(this.Handle);
        }

        private void InitializeUi()
        {
            this.Text = "GDocsDrop";
            this.ClientSize = new Size(460, 520);
            this.MinimumSize = new Size(420, 460);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = BgWindow;
            this.ForeColor = TextPrimary;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.AllowDrop = true;
            this.KeyPreview = true;

            try
            {
                string iconPath = FileAssociations.ResolveIconPath("docs.ico");
                if (File.Exists(iconPath))
                {
                    if (iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        this.Icon = new Icon(iconPath);
                    }
                    else
                    {
                        this.Icon = Icon.ExtractAssociatedIcon(iconPath);
                    }
                }
            }
            catch
            {
            }

            this.DragEnter += OnFormDragEnter;
            this.DragLeave += OnFormDragLeave;
            this.DragDrop += OnFormDragDrop;

            Label lblTitle = new Label();
            lblTitle.Text = "GDocsDrop";
            lblTitle.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            lblTitle.ForeColor = TextPrimary;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(18, 14);
            this.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "Drop documents to open straight in Google Docs";
            lblSub.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            lblSub.ForeColor = TextMuted;
            lblSub.AutoSize = true;
            lblSub.Location = new Point(20, 42);
            this.Controls.Add(lblSub);

            btnPinTop = CreateDarkButton("Pin on Top", new Size(92, 28));
            btnPinTop.Location = new Point(this.ClientSize.Width - 110, 16);
            btnPinTop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnPinTop.Click += delegate
            {
                this.TopMost = !this.TopMost;
                btnPinTop.Text = this.TopMost ? "Pinned" : "Pin on Top";
                btnPinTop.BackColor = this.TopMost ? Color.FromArgb(30, 58, 108) : BgButton;
                btnPinTop.FlatAppearance.BorderColor = this.TopMost ? GoogleBlue : BorderHairline;
            };
            this.Controls.Add(btnPinTop);

            lblDrivePill = new Label();
            lblDrivePill.Location = new Point(20, 68);
            lblDrivePill.Size = new Size(this.ClientSize.Width - 40, 24);
            lblDrivePill.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblDrivePill.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            lblDrivePill.TextAlign = ContentAlignment.MiddleLeft;
            lblDrivePill.Padding = new Padding(8, 0, 8, 0);
            lblDrivePill.BackColor = BgCard;
            lblDrivePill.Cursor = Cursors.Hand;
            lblDrivePill.Click += delegate
            {
                if (currentDriveStatus != null && (!currentDriveStatus.IsAvailable || !currentDriveStatus.IsDriveFsRunning))
                {
                    SetStatus("Starting Google Drive for Desktop...", 1);
                    DriveSync.LaunchGoogleDriveIfInstalled();
                    StartDrivePollTimer();
                }
                else
                {
                    RefreshDriveStatus();
                }
            };
            this.Controls.Add(lblDrivePill);

            dropZonePanel = new DoubleBufferedPanel();
            dropZonePanel.Location = new Point(20, 102);
            dropZonePanel.Size = new Size(this.ClientSize.Width - 40, 146);
            dropZonePanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            dropZonePanel.AllowDrop = true;
            dropZonePanel.Cursor = Cursors.Hand;
            dropZonePanel.Paint += OnDropZonePaint;
            dropZonePanel.DragEnter += OnFormDragEnter;
            dropZonePanel.DragLeave += OnFormDragLeave;
            dropZonePanel.DragDrop += OnFormDragDrop;
            dropZonePanel.MouseEnter += delegate { isMouseHover = true; dropZonePanel.Invalidate(); };
            dropZonePanel.MouseLeave += delegate { isMouseHover = false; dropZonePanel.Invalidate(); };
            dropZonePanel.Click += OnDropZoneClick;
            this.Controls.Add(dropZonePanel);

            statusPanel = new Panel();
            statusPanel.Location = new Point(20, 258);
            statusPanel.Size = new Size(this.ClientSize.Width - 40, 44);
            statusPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusPanel.BackColor = BgCard;

            lblStatus = new Label();
            lblStatus.Location = new Point(10, 6);
            lblStatus.Size = new Size(statusPanel.Width - 20, 20);
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblStatus.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            lblStatus.ForeColor = TextMuted;
            lblStatus.Text = "Ready — drop a .docx, .xlsx, .pptx, or PDF file";
            lblStatus.AutoEllipsis = true;
            statusPanel.Controls.Add(lblStatus);

            lnkStartDrive = new LinkLabel();
            lnkStartDrive.Text = "Start Drive";
            lnkStartDrive.LinkColor = GoogleBlue;
            lnkStartDrive.ActiveLinkColor = TextPrimary;
            lnkStartDrive.AutoSize = true;
            lnkStartDrive.Location = new Point(statusPanel.Width - 155, 7);
            lnkStartDrive.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lnkStartDrive.Visible = false;
            lnkStartDrive.Click += delegate
            {
                DriveSync.LaunchGoogleDriveIfInstalled();
                SetStatus("Starting Google Drive for Desktop...", 1);
                StartDrivePollTimer();
            };
            statusPanel.Controls.Add(lnkStartDrive);

            lnkOpenFolder = new LinkLabel();
            lnkOpenFolder.Text = "Open Folder";
            lnkOpenFolder.LinkColor = GoogleBlue;
            lnkOpenFolder.ActiveLinkColor = TextPrimary;
            lnkOpenFolder.AutoSize = true;
            lnkOpenFolder.Location = new Point(statusPanel.Width - 82, 7);
            lnkOpenFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lnkOpenFolder.Visible = false;
            lnkOpenFolder.Click += delegate { OpenTargetDriveFolder(); };
            statusPanel.Controls.Add(lnkOpenFolder);

            progressBarPanel = new DoubleBufferedPanel();
            progressBarPanel.Location = new Point(10, 32);
            progressBarPanel.Size = new Size(statusPanel.Width - 20, 4);
            progressBarPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressBarPanel.Paint += OnProgressBarPaint;
            statusPanel.Controls.Add(progressBarPanel);

            this.Controls.Add(statusPanel);

            Label lblRecent = new Label();
            lblRecent.Text = "RECENT OPENED (CLICK TO REOPEN)";
            lblRecent.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            lblRecent.ForeColor = TextMuted;
            lblRecent.AutoSize = true;
            lblRecent.Location = new Point(20, 312);
            this.Controls.Add(lblRecent);

            recentList = new ListBox();
            recentList.Location = new Point(20, 330);
            recentList.Size = new Size(this.ClientSize.Width - 40, this.ClientSize.Height - 386);
            recentList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            recentList.BackColor = BgCard;
            recentList.ForeColor = TextPrimary;
            recentList.BorderStyle = BorderStyle.FixedSingle;
            recentList.DrawMode = DrawMode.OwnerDrawFixed;
            recentList.ItemHeight = 36;
            recentList.IntegralHeight = false;
            recentList.Cursor = Cursors.Hand;
            recentList.DrawItem += OnRecentDrawItem;
            recentList.MouseMove += OnRecentMouseMove;
            recentList.MouseLeave += delegate
            {
                if (hoveredListIndex != -1)
                {
                    hoveredListIndex = -1;
                    recentList.Invalidate();
                }
            };
            recentList.MouseClick += OnRecentMouseClick;
            this.Controls.Add(recentList);

            btnAssocToggle = CreateDarkButton("File Icons & Double-Click: ON", new Size(220, 32));
            btnAssocToggle.Location = new Point(20, this.ClientSize.Height - 44);
            btnAssocToggle.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnAssocToggle.Click += OnAssocToggleClick;
            this.Controls.Add(btnAssocToggle);

            btnDriveFolder = CreateDarkButton("Drive Folder", new Size(124, 32));
            btnDriveFolder.Location = new Point(this.ClientSize.Width - 144, this.ClientSize.Height - 44);
            btnDriveFolder.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnDriveFolder.Click += delegate { OpenTargetDriveFolder(); };
            this.Controls.Add(btnDriveFolder);

            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 40;
            animTimer.Tick += delegate
            {
                animTick = (animTick + 1) % 100;
                progressBarPanel.Invalidate();
            };

            driveCheckTimer = new System.Windows.Forms.Timer();
            driveCheckTimer.Interval = 2000;
            driveCheckTimer.Tick += delegate
            {
                RefreshDriveStatus();
                if (currentDriveStatus != null && currentDriveStatus.IsAvailable && currentDriveStatus.IsDriveFsRunning)
                {
                    driveCheckTimer.Stop();
                    SetStatus("Google Drive is ready.", 2);
                }
            };
        }

        private Button CreateDarkButton(string text, Size size)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Size = size;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = BorderHairline;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 49, 58);
            btn.BackColor = BgButton;
            btn.ForeColor = TextPrimary;
            btn.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        private void StartDrivePollTimer()
        {
            if (!driveCheckTimer.Enabled)
            {
                driveCheckTimer.Start();
            }
        }

        private void RefreshDriveStatus()
        {
            currentDriveStatus = DriveSync.GetDriveStatus();
            if (currentDriveStatus.IsAvailable && currentDriveStatus.IsDriveFsRunning)
            {
                string emailPart = string.IsNullOrEmpty(currentDriveStatus.AccountEmail)
                    ? string.Empty
                    : string.Format("  ({0})", currentDriveStatus.AccountEmail);
                lblDrivePill.Text = string.Format("\u25CF  {0}{1}", currentDriveStatus.MyDriveRoot, emailPart);
                lblDrivePill.ForeColor = GoogleGreen;
                lnkStartDrive.Visible = false;
            }
            else if (currentDriveStatus.IsAvailable && !currentDriveStatus.IsDriveFsRunning)
            {
                lblDrivePill.Text = "\u25CF  Google Drive paused/offline — Click to start";
                lblDrivePill.ForeColor = AmberWarn;
                lnkStartDrive.Visible = true;
            }
            else
            {
                lblDrivePill.Text = "\u25CF  G:\\My Drive not detected — Click to start Google Drive";
                lblDrivePill.ForeColor = AmberWarn;
                lnkStartDrive.Visible = true;
            }
        }

        private void RefreshAssocButton()
        {
            bool registered = FileAssociations.IsRegistered();
            btnAssocToggle.Text = registered ? "File Icons & Double-Click: ON" : "File Icons & Double-Click: OFF";
            btnAssocToggle.FlatAppearance.BorderColor = registered ? GoogleGreen : BorderHairline;
            btnAssocToggle.BackColor = registered ? Color.FromArgb(18, 42, 32) : BgButton;
        }

        private void ReloadHistory()
        {
            historyItems = DriveSync.LoadHistory();
            recentList.Items.Clear();
            int count = Math.Min(historyItems.Count, 12);
            for (int i = 0; i < count; i++)
            {
                recentList.Items.Add(historyItems[i]);
            }
        }

        private void OnDropZonePaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(1, 1, dropZonePanel.Width - 3, dropZonePanel.Height - 3);
            Color fill = (isDragHover || isMouseHover) ? BgHover : BgCard;
            Color border = isDragHover ? GoogleBlue : (isSuccessGlow ? GoogleGreen : (isMouseHover ? Color.FromArgb(88, 106, 130) : BorderHairline));

            using (GraphicsPath path = CreateRoundedRect(rect, 12))
            {
                using (SolidBrush br = new SolidBrush(fill))
                {
                    g.FillPath(br, path);
                }
                using (Pen pen = new Pen(border, isDragHover ? 2f : 1.5f))
                {
                    pen.DashStyle = isDragHover ? DashStyle.Solid : DashStyle.Dash;
                    g.DrawPath(pen, path);
                }
            }

            int cx = dropZonePanel.Width / 2;
            int iconY = 24;
            Rectangle docRect = new Rectangle(cx - 18, iconY, 36, 44);
            using (GraphicsPath docPath = CreateRoundedRect(docRect, 5))
            using (SolidBrush docBrush = new SolidBrush(isDragHover ? GoogleBlue : Color.FromArgb(40, 78, 145)))
            {
                g.FillPath(docBrush, docPath);
            }
            using (Pen linePen = new Pen(Color.FromArgb(220, 235, 255), 2f))
            {
                g.DrawLine(linePen, cx - 9, iconY + 15, cx + 9, iconY + 15);
                g.DrawLine(linePen, cx - 9, iconY + 23, cx + 9, iconY + 23);
                g.DrawLine(linePen, cx - 9, iconY + 31, cx + 3, iconY + 31);
            }

            string mainText = isDragHover
                ? "Release to open in Google Docs"
                : "Drop .docx, .xlsx, .pptx, or docs here";
            string subText = isBusy
                ? "Processing file..."
                : "or click anywhere in this box to choose files";

            using (Font mainFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            using (Font subFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            using (SolidBrush mainBrush = new SolidBrush(TextPrimary))
            using (SolidBrush subBrush = new SolidBrush(TextMuted))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                g.DrawString(mainText, mainFont, mainBrush, new RectangleF(12, 78, dropZonePanel.Width - 24, 24), sf);
                g.DrawString(subText, subFont, subBrush, new RectangleF(12, 104, dropZonePanel.Width - 24, 20), sf);
            }
        }

        private void OnProgressBarPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(30, 35, 44));
            if (progressState == 1)
            {
                int w = Math.Max(40, progressBarPanel.Width / 3);
                int maxTravel = progressBarPanel.Width + w;
                int x = (int)((animTick / 100.0) * maxTravel) - w;
                using (SolidBrush br = new SolidBrush(GoogleBlue))
                {
                    g.FillRectangle(br, x, 0, w, progressBarPanel.Height);
                }
            }
            else if (progressState == 2)
            {
                using (SolidBrush br = new SolidBrush(GoogleGreen))
                {
                    g.FillRectangle(br, 0, 0, progressBarPanel.Width, progressBarPanel.Height);
                }
            }
            else if (progressState == 3)
            {
                using (SolidBrush br = new SolidBrush(ErrorRed))
                {
                    g.FillRectangle(br, 0, 0, progressBarPanel.Width, progressBarPanel.Height);
                }
            }
        }

        private void OnRecentDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= recentList.Items.Count)
            {
                return;
            }
            HistoryEntry item = (HistoryEntry)recentList.Items[e.Index];
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool isHover = (e.Index == hoveredListIndex);
            Color bg = isHover ? BgHover : BgCard;
            using (SolidBrush bgBr = new SolidBrush(bg))
            {
                g.FillRectangle(bgBr, e.Bounds);
            }

            string badge = DriveSync.GetBadgeText(item.Extension);
            Color badgeColor = GoogleBlue;
            if (badge == "XLS") badgeColor = GoogleGreen;
            else if (badge == "PPT") badgeColor = GoogleYellow;
            else if (badge == "DRV") badgeColor = TextMuted;

            Rectangle badgeRect = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + 8, 36, 20);
            using (GraphicsPath bp = CreateRoundedRect(badgeRect, 4))
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(38, badgeColor)))
            using (Pen bpen = new Pen(badgeColor, 1f))
            {
                g.FillPath(bb, bp);
                g.DrawPath(bpen, bp);
            }

            using (Font bf = new Font("Segoe UI", 7.2f, FontStyle.Bold))
            using (SolidBrush btb = new SolidBrush(badgeColor))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(badge, bf, btb, badgeRect, sf);
            }

            string timeStr = FormatRelativeTime(item.LastOpenedUtc);
            int timeWidth = 68;
            Rectangle nameRect = new Rectangle(e.Bounds.Left + 54, e.Bounds.Top + 6, e.Bounds.Width - 66 - timeWidth, 24);
            Rectangle timeRect = new Rectangle(e.Bounds.Right - timeWidth - 10, e.Bounds.Top + 6, timeWidth, 24);

            using (Font nf = new Font("Segoe UI", 9f, FontStyle.Regular))
            using (SolidBrush nb = new SolidBrush(TextPrimary))
            using (Font tf = new Font("Segoe UI", 8f, FontStyle.Regular))
            using (SolidBrush tb = new SolidBrush(TextMuted))
            {
                StringFormat nsf = new StringFormat();
                nsf.Trimming = StringTrimming.EllipsisCharacter;
                nsf.FormatFlags = StringFormatFlags.NoWrap;
                nsf.LineAlignment = StringAlignment.Center;
                g.DrawString(item.FileName ?? string.Empty, nf, nb, nameRect, nsf);

                StringFormat tsf = new StringFormat();
                tsf.Alignment = StringAlignment.Far;
                tsf.LineAlignment = StringAlignment.Center;
                g.DrawString(timeStr, tf, tb, timeRect, tsf);
            }

            using (Pen sep = new Pen(Color.FromArgb(34, 39, 46), 1f))
            {
                g.DrawLine(sep, e.Bounds.Left + 10, e.Bounds.Bottom - 1, e.Bounds.Right - 10, e.Bounds.Bottom - 1);
            }
        }

        private void OnRecentMouseMove(object sender, MouseEventArgs e)
        {
            int idx = recentList.IndexFromPoint(e.Location);
            if (idx != hoveredListIndex)
            {
                hoveredListIndex = idx;
                recentList.Invalidate();
            }
        }

        private void OnRecentMouseClick(object sender, MouseEventArgs e)
        {
            int idx = recentList.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= recentList.Items.Count)
            {
                return;
            }
            HistoryEntry item = (HistoryEntry)recentList.Items[idx];
            if (!string.IsNullOrEmpty(item.Url))
            {
                try
                {
                    DriveSync.OpenUrlInBrowser(item.Url);
                    SetStatus("Reopened " + item.FileName, 2);
                }
                catch (Exception ex)
                {
                    SetStatus("Could not open browser: " + ex.Message, 3);
                }
            }
        }

        private void OnFormDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                isDragHover = true;
                dropZonePanel.Invalidate();
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void OnFormDragLeave(object sender, EventArgs e)
        {
            isDragHover = false;
            dropZonePanel.Invalidate();
        }

        private void OnFormDragDrop(object sender, DragEventArgs e)
        {
            isDragHover = false;
            dropZonePanel.Invalidate();
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                ProcessFilesAsync(files);
            }
        }

        private void OnDropZoneClick(object sender, EventArgs e)
        {
            if (isBusy)
            {
                return;
            }
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Choose Document to Open in Google Docs";
                dlg.Multiselect = true;
                dlg.Filter = "Office & Documents (*.docx;*.doc;*.xlsx;*.xls;*.pptx;*.ppt;*.odt;*.ods;*.odp;*.rtf;*.pdf)|*.docx;*.doc;*.docm;*.dotx;*.odt;*.rtf;*.xlsx;*.xls;*.xlsm;*.xltx;*.ods;*.pptx;*.ppt;*.pptm;*.potx;*.odp;*.pdf|All Files (*.*)|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.FileNames.Length > 0)
                {
                    ProcessFilesAsync(dlg.FileNames);
                }
            }
        }

        private void OnAssocToggleClick(object sender, EventArgs e)
        {
            try
            {
                if (FileAssociations.IsRegistered())
                {
                    FileAssociations.UnregisterAll();
                    SetStatus("Removed Windows file associations & icons.", 0);
                }
                else
                {
                    FileAssociations.RegisterAll();
                    SetStatus("Registered .docx, .xlsx, .pptx icons & double-click!", 2);
                }
                RefreshAssocButton();
            }
            catch (Exception ex)
            {
                SetStatus("Association update failed: " + ex.Message, 3);
            }
        }

        private void OpenTargetDriveFolder()
        {
            DriveStatusInfo st = DriveSync.GetDriveStatus();
            if (!st.IsAvailable)
            {
                SetStatus("Google Drive (G:\\My Drive) is not mounted yet.", 3);
                return;
            }
            try
            {
                if (!Directory.Exists(st.TargetFolder))
                {
                    Directory.CreateDirectory(st.TargetFolder);
                }
                ProcessStartInfo psi = new ProcessStartInfo(st.TargetFolder);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                SetStatus("Could not open folder: " + ex.Message, 3);
            }
        }

        private void ProcessFilesAsync(string[] files)
        {
            if (isBusy)
            {
                SetStatus("Still syncing the previous drop — please wait a second.", 0);
                return;
            }

            isBusy = true;
            lnkOpenFolder.Visible = false;
            lnkStartDrive.Visible = false;
            dropZonePanel.Invalidate();

            ThreadPool.QueueUserWorkItem(delegate
            {
                int opened = 0;
                string lastError = null;
                string lastFileName = null;

                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    lastFileName = Path.GetFileName(file);
                    UpdateStatusFromWorker(string.Format("Processing {0} ({1}/{2})...", lastFileName, i + 1, files.Length), 1);

                    SyncResult res = DriveSync.OpenInGoogleDocs(file, false, delegate(string msg)
                    {
                        UpdateStatusFromWorker(string.Format("{0}: {1}", lastFileName, msg), 1);
                    });

                    if (res.Success)
                    {
                        opened++;
                        try
                        {
                            DriveSync.OpenUrlInBrowser(res.Url);
                        }
                        catch (Exception ex)
                        {
                            lastError = "Browser open failed: " + ex.Message;
                        }
                    }
                    else
                    {
                        lastError = res.ErrorMessage;
                    }
                }

                try
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        isBusy = false;
                        RefreshDriveStatus();
                        ReloadHistory();

                        if (lastError == null && opened > 0)
                        {
                            isSuccessGlow = true;
                            string msg = opened == 1
                                ? string.Format("Opened {0} in Google Docs!", lastFileName)
                                : string.Format("Opened {0} files in Google Docs!", opened);
                            SetStatus(msg, 2);

                            System.Windows.Forms.Timer glowTimer = new System.Windows.Forms.Timer();
                            glowTimer.Interval = 1800;
                            glowTimer.Tick += delegate
                            {
                                glowTimer.Stop();
                                glowTimer.Dispose();
                                isSuccessGlow = false;
                                dropZonePanel.Invalidate();
                            };
                            glowTimer.Start();
                        }
                        else
                        {
                            SetStatus(lastError ?? "No files were opened.", 3);
                            lnkOpenFolder.Visible = true;
                            if (currentDriveStatus == null || !currentDriveStatus.IsDriveFsRunning)
                            {
                                lnkStartDrive.Visible = true;
                            }
                        }

                        dropZonePanel.Invalidate();
                    });
                }
                catch
                {
                }
            });
        }

        private void UpdateStatusFromWorker(string text, int state)
        {
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        SetStatus(text, state);
                    });
                }
            }
            catch
            {
            }
        }

        private void SetStatus(string text, int state)
        {
            progressState = state;
            lblStatus.Text = text;
            if (state == 3)
            {
                lblStatus.ForeColor = ErrorRed;
                animTimer.Stop();
            }
            else if (state == 2)
            {
                lblStatus.ForeColor = GoogleGreen;
                animTimer.Stop();
            }
            else if (state == 1)
            {
                lblStatus.ForeColor = TextPrimary;
                if (!animTimer.Enabled)
                {
                    animTimer.Start();
                }
            }
            else
            {
                lblStatus.ForeColor = TextMuted;
                animTimer.Stop();
            }
            progressBarPanel.Invalidate();
        }

        private static string FormatRelativeTime(string utcIso)
        {
            if (string.IsNullOrEmpty(utcIso))
            {
                return string.Empty;
            }
            DateTime dt;
            if (!DateTime.TryParse(utcIso, out dt))
            {
                return string.Empty;
            }
            TimeSpan span = DateTime.UtcNow - dt.ToUniversalTime();
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return string.Format("{0}m ago", (int)span.TotalMinutes);
            if (span.TotalHours < 24) return string.Format("{0}h ago", (int)span.TotalHours);
            return dt.ToLocalTime().ToString("MMM d");
        }

        private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
