using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GDocsDrop
{
    internal class QuickToastForm : Form
    {
        private static readonly Color BgWindow = Color.FromArgb(15, 17, 23);
        private static readonly Color BgButton = Color.FromArgb(33, 38, 45);
        private static readonly Color BorderHairline = Color.FromArgb(48, 54, 61);
        private static readonly Color GoogleBlue = Color.FromArgb(66, 133, 244);
        private static readonly Color GoogleGreen = Color.FromArgb(15, 157, 88);
        private static readonly Color GoogleYellow = Color.FromArgb(244, 180, 0);
        private static readonly Color ErrorRed = Color.FromArgb(248, 81, 73);
        private static readonly Color TextPrimary = Color.FromArgb(240, 246, 252);
        private static readonly Color TextMuted = Color.FromArgb(139, 148, 158);

        private readonly string[] files;
        private Label lblBadge;
        private Label lblFileName;
        private Label lblStatus;
        private Button btnCloseX;
        private Button btnRetry;
        private Button btnOpenFolder;
        private Button btnErrorClose;
        private DoubleBufferedPanel progressPanel;

        private int progressState = 1;
        private int animTick;
        private System.Windows.Forms.Timer animTimer;
        private System.Windows.Forms.Timer autoCloseTimer;

        public QuickToastForm(string[] inputFiles)
        {
            files = inputFiles ?? new string[0];
            InitializeUi();
            PositionBottomRight();
        }

        private void InitializeUi()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.ClientSize = new Size(400, 88);
            this.BackColor = BgWindow;
            this.ForeColor = TextPrimary;
            this.Font = new Font("Segoe UI", 9f, FontStyle.Regular);

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

            this.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen borderPen = new Pen(BorderHairline, 1f))
                {
                    e.Graphics.DrawRectangle(borderPen, 0, 0, this.ClientSize.Width - 1, this.ClientSize.Height - 1);
                }
            };

            string firstFile = files.Length > 0 ? files[0] : string.Empty;
            string ext = Path.GetExtension(firstFile).TrimStart('.').ToUpperInvariant();
            if (string.IsNullOrEmpty(ext))
            {
                ext = "DOC";
            }
            if (ext.Length > 4)
            {
                ext = ext.Substring(0, 4);
            }

            string badgeType = DriveSync.GetBadgeText(Path.GetExtension(firstFile));
            Color badgeColor = GoogleBlue;
            if (badgeType == "XLS") badgeColor = GoogleGreen;
            else if (badgeType == "PPT") badgeColor = GoogleYellow;

            lblBadge = new Label();
            lblBadge.Text = ext;
            lblBadge.Location = new Point(14, 16);
            lblBadge.Size = new Size(46, 22);
            lblBadge.TextAlign = ContentAlignment.MiddleCenter;
            lblBadge.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            lblBadge.ForeColor = badgeColor;
            lblBadge.BackColor = Color.FromArgb(24, 32, 46);
            this.Controls.Add(lblBadge);

            lblFileName = new Label();
            lblFileName.Location = new Point(68, 15);
            lblFileName.Size = new Size(292, 22);
            lblFileName.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblFileName.ForeColor = TextPrimary;
            lblFileName.AutoEllipsis = true;
            lblFileName.Text = files.Length > 1
                ? string.Format("{0} (+{1} more)", Path.GetFileName(firstFile), files.Length - 1)
                : Path.GetFileName(firstFile);
            this.Controls.Add(lblFileName);

            btnCloseX = new Button();
            btnCloseX.Text = "\u00D7";
            btnCloseX.Location = new Point(368, 8);
            btnCloseX.Size = new Size(24, 24);
            btnCloseX.FlatStyle = FlatStyle.Flat;
            btnCloseX.FlatAppearance.BorderSize = 0;
            btnCloseX.BackColor = BgWindow;
            btnCloseX.ForeColor = TextMuted;
            btnCloseX.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnCloseX.Cursor = Cursors.Hand;
            btnCloseX.Click += delegate { this.Close(); };
            this.Controls.Add(btnCloseX);

            lblStatus = new Label();
            lblStatus.Location = new Point(14, 44);
            lblStatus.Size = new Size(372, 20);
            lblStatus.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            lblStatus.ForeColor = TextMuted;
            lblStatus.AutoEllipsis = true;
            lblStatus.Text = "Syncing to Google Drive...";
            this.Controls.Add(lblStatus);

            btnRetry = CreateActionButton("Retry", new Point(14, 78));
            btnRetry.Click += delegate
            {
                HideErrorActions();
                StartSyncWorker();
            };

            btnOpenFolder = CreateActionButton("Open Folder", new Point(100, 78));
            btnOpenFolder.Size = new Size(96, 26);
            btnOpenFolder.Click += delegate
            {
                DriveStatusInfo st = DriveSync.GetDriveStatus();
                if (st.IsAvailable)
                {
                    if (!Directory.Exists(st.TargetFolder))
                    {
                        Directory.CreateDirectory(st.TargetFolder);
                    }
                    ProcessStartInfo psi = new ProcessStartInfo(st.TargetFolder);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
            };

            btnErrorClose = CreateActionButton("Close", new Point(204, 78));
            btnErrorClose.Click += delegate { this.Close(); };

            progressPanel = new DoubleBufferedPanel();
            progressPanel.Location = new Point(1, this.ClientSize.Height - 5);
            progressPanel.Size = new Size(this.ClientSize.Width - 2, 4);
            progressPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            progressPanel.Paint += OnProgressPaint;
            this.Controls.Add(progressPanel);

            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 40;
            animTimer.Tick += delegate
            {
                animTick = (animTick + 1) % 100;
                progressPanel.Invalidate();
            };
            animTimer.Start();

            autoCloseTimer = new System.Windows.Forms.Timer();
            autoCloseTimer.Interval = 1500;
            autoCloseTimer.Tick += delegate
            {
                autoCloseTimer.Stop();
                this.Close();
            };

            this.Shown += delegate { StartSyncWorker(); };
        }

        private Button CreateActionButton(string text, Point loc)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Location = loc;
            btn.Size = new Size(78, 26);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = BorderHairline;
            btn.FlatAppearance.BorderSize = 1;
            btn.BackColor = BgButton;
            btn.ForeColor = TextPrimary;
            btn.Font = new Font("Segoe UI", 8.2f, FontStyle.Regular);
            btn.Cursor = Cursors.Hand;
            btn.Visible = false;
            this.Controls.Add(btn);
            return btn;
        }

        private void PositionBottomRight()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(wa.Right - this.Width - 20, wa.Bottom - this.Height - 20);
        }

        private void ShowErrorActions(string message)
        {
            progressState = 3;
            animTimer.Stop();
            lblStatus.ForeColor = ErrorRed;
            lblStatus.Text = message;
            this.ClientSize = new Size(400, 120);
            PositionBottomRight();
            btnRetry.Visible = true;
            btnOpenFolder.Visible = true;
            btnErrorClose.Visible = true;
            progressPanel.Invalidate();
        }

        private void HideErrorActions()
        {
            btnRetry.Visible = false;
            btnOpenFolder.Visible = false;
            btnErrorClose.Visible = false;
            this.ClientSize = new Size(400, 88);
            PositionBottomRight();
            progressState = 1;
            lblStatus.ForeColor = TextMuted;
            lblStatus.Text = "Retrying sync...";
            animTimer.Start();
            progressPanel.Invalidate();
        }

        private void OnProgressPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(26, 30, 38));
            if (progressState == 1)
            {
                int w = Math.Max(45, progressPanel.Width / 3);
                int maxTravel = progressPanel.Width + w;
                int x = (int)((animTick / 100.0) * maxTravel) - w;
                using (SolidBrush br = new SolidBrush(GoogleBlue))
                {
                    g.FillRectangle(br, x, 0, w, progressPanel.Height);
                }
            }
            else if (progressState == 2)
            {
                using (SolidBrush br = new SolidBrush(GoogleGreen))
                {
                    g.FillRectangle(br, 0, 0, progressPanel.Width, progressPanel.Height);
                }
            }
            else if (progressState == 3)
            {
                using (SolidBrush br = new SolidBrush(ErrorRed))
                {
                    g.FillRectangle(br, 0, 0, progressPanel.Width, progressPanel.Height);
                }
            }
        }

        private void StartSyncWorker()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                int opened = 0;
                string errorMsg = null;

                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    string name = Path.GetFileName(file);
                    UpdateStatus(files.Length > 1
                        ? string.Format("({0}/{1}) Syncing {2}...", i + 1, files.Length, name)
                        : "Syncing to Google Drive...");

                    SyncResult res = DriveSync.OpenInGoogleDocs(file, false, delegate(string msg)
                    {
                        UpdateStatus(files.Length > 1
                            ? string.Format("({0}/{1}) {2}", i + 1, files.Length, msg)
                            : msg);
                    });

                    if (res.Success)
                    {
                        UpdateStatus("Opening in Google Docs...");
                        try
                        {
                            DriveSync.OpenUrlInBrowser(res.Url);
                            opened++;
                        }
                        catch (Exception ex)
                        {
                            errorMsg = "Failed to launch browser: " + ex.Message;
                            break;
                        }
                    }
                    else
                    {
                        errorMsg = res.ErrorMessage;
                        break;
                    }
                }

                try
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            if (errorMsg == null && opened > 0)
                            {
                                progressState = 2;
                                animTimer.Stop();
                                lblStatus.ForeColor = GoogleGreen;
                                lblStatus.Text = opened == 1
                                    ? "Opened in Google Docs"
                                    : string.Format("Opened {0} files in Google Docs", opened);
                                progressPanel.Invalidate();
                                autoCloseTimer.Start();
                            }
                            else
                            {
                                ShowErrorActions(errorMsg ?? "Could not open document.");
                            }
                        });
                    }
                }
                catch
                {
                }
            });
        }

        private void UpdateStatus(string msg)
        {
            try
            {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        lblStatus.Text = msg;
                    });
                }
            }
            catch
            {
            }
        }
    }
}
