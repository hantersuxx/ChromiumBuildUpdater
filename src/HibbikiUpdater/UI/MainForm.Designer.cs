#nullable enable

using System.Drawing;
using System.Windows.Forms;

namespace HibbikiUpdater.UI;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    private ComboBox _cmbFlavor = null!;
    private Label _lblInstalledVer = null!;
    private Label _lblLatestVer = null!;
    private Label _lblReleaseDate = null!;
    private Label _lblSourceEndpoint = null!;
    private Label _lblLastChecked = null!;
    private Label _lblStatus = null!;
    private ProgressBar _progressBar = null!;
    private Button _btnManualUpdate = null!;
    private Button _btnCheckNow = null!;
    private LinkLabel _lnkPayPal = null!;
    private LinkLabel _lnkEthereum = null!;
    private LinkLabel _lnkBitcoin = null!;
    private CheckBox _chkAutoUpdate = null!;
    private CheckBox _chkNotifications = null!;
    private CheckBox _chkMinimizeOnClose = null!;
    private CheckBox _chkStartWithWindows = null!;
    private CheckBox _chkDeferIfRunning = null!;
    private CheckBox _chkPreferFallbackApi = null!;
    private NumericUpDown _numIntervalHours = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        Text = "Chromium Tray Updater";
        ClientSize = new Size(760, 790);
        MinimumSize = new Size(700, 730);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.5F);
        SizeGripStyle = SizeGripStyle.Show;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 7,
            RowStyles =
            {
                new RowStyle(SizeType.Percent, 9),
                new RowStyle(SizeType.Percent, 26),
                new RowStyle(SizeType.Percent, 34),
                new RowStyle(SizeType.Percent, 8),
                new RowStyle(SizeType.Percent, 4),
                new RowStyle(SizeType.Percent, 8),
                new RowStyle(SizeType.Percent, 11)
            }
        };

        var grpFlavor = new GroupBox
        {
            Text = "Chromium distribution",
            Dock = DockStyle.Fill,
            Height = 68,
            Padding = new Padding(10)
        };

        _cmbFlavor = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbFlavor.Items.AddRange(new object[]
        {
            "Hibbiki Chromium (Google Sync + Widevine + HEVC)",
            "Ungoogled-Chromium (Maximum Privacy / No Google Services)",
            "Thorium Browser (AVX2 Compiler Optimizations)"
        });
        grpFlavor.Controls.Add(_cmbFlavor);

        var grpInfo = new GroupBox
        {
            Text = "Version and release details",
            Dock = DockStyle.Fill,
            Height = 145,
            Padding = new Padding(10)
        };

        var infoLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            RowStyles =
            {
                new RowStyle(SizeType.Percent, 20),
                new RowStyle(SizeType.Percent, 20),
                new RowStyle(SizeType.Percent, 20),
                new RowStyle(SizeType.Percent, 20),
                new RowStyle(SizeType.Percent, 20)
            }
        };
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        infoLayout.Controls.Add(new Label { Text = "Installed version:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _lblInstalledVer = new Label { Text = "Checking...", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Anchor = AnchorStyles.Left };
        infoLayout.Controls.Add(_lblInstalledVer, 1, 0);

        infoLayout.Controls.Add(new Label { Text = "Latest version:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _lblLatestVer = new Label { Text = "Unknown", Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(0, 102, 204), AutoSize = true, Anchor = AnchorStyles.Left };
        infoLayout.Controls.Add(_lblLatestVer, 1, 1);

        infoLayout.Controls.Add(new Label { Text = "Release date:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _lblReleaseDate = new Label { Text = "Unknown", AutoSize = true, Anchor = AnchorStyles.Left };
        infoLayout.Controls.Add(_lblReleaseDate, 1, 2);

        infoLayout.Controls.Add(new Label { Text = "Release source:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        _lblSourceEndpoint = new Label { Text = "GitHub Releases (Primary)", AutoSize = true, ForeColor = Color.DarkGreen, Anchor = AnchorStyles.Left };
        infoLayout.Controls.Add(_lblSourceEndpoint, 1, 3);

        infoLayout.Controls.Add(new Label { Text = "Last checked:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        _lblLastChecked = new Label { Text = "Never", AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left };
        infoLayout.Controls.Add(_lblLastChecked, 1, 4);

        grpInfo.Controls.Add(infoLayout);

        var grpSettings = new GroupBox
        {
            Text = "Updater preferences",
            Dock = DockStyle.Fill,
            Height = 210,
            Padding = new Padding(10)
        };

        var settingsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(4, 6, 4, 4),
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 50),
                new ColumnStyle(SizeType.Percent, 50)
            },
            RowStyles =
            {
                new RowStyle(SizeType.Percent, 25),
                new RowStyle(SizeType.Percent, 25),
                new RowStyle(SizeType.Percent, 25),
                new RowStyle(SizeType.Percent, 25)
            }
        };

        _chkAutoUpdate = new CheckBox { Text = "Install updates automatically", AutoSize = true, Anchor = AnchorStyles.Left };
        _chkNotifications = new CheckBox { Text = "Show tray notifications", AutoSize = true, Anchor = AnchorStyles.Left };
        _chkMinimizeOnClose = new CheckBox { Text = "Minimize to tray on close", AutoSize = true, Anchor = AnchorStyles.Left };
        _chkStartWithWindows = new CheckBox { Text = "Start with Windows", AutoSize = true, Anchor = AnchorStyles.Left };
        _chkDeferIfRunning = new CheckBox { Text = "Wait while the browser is running", AutoSize = true, Anchor = AnchorStyles.Left };
        _chkPreferFallbackApi = new CheckBox { Text = "Prefer alternate release source", AutoSize = true, Anchor = AnchorStyles.Left };

        var intervalPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = false,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 0)
        };
        intervalPanel.Controls.Add(new Label { Text = "Background check interval:", AutoSize = true, Padding = new Padding(0, 4, 4, 0) });
        _numIntervalHours = new NumericUpDown { Minimum = 1, Maximum = 72, Value = 6, Width = 60 };
        intervalPanel.Controls.Add(_numIntervalHours);

        settingsLayout.Controls.Add(_chkAutoUpdate, 0, 0);
        settingsLayout.Controls.Add(_chkNotifications, 1, 0);
        settingsLayout.Controls.Add(_chkMinimizeOnClose, 0, 1);
        settingsLayout.Controls.Add(_chkStartWithWindows, 1, 1);
        settingsLayout.Controls.Add(_chkDeferIfRunning, 0, 2);
        settingsLayout.Controls.Add(_chkPreferFallbackApi, 1, 2);
        settingsLayout.Controls.Add(intervalPanel, 0, 3);
        settingsLayout.SetColumnSpan(intervalPanel, 2);
        grpSettings.Controls.Add(settingsLayout);

        var supportPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 8, 4, 0)
        };
        supportPanel.Controls.Add(new Label
        {
            Text = "Enjoying the app? Support development (optional):",
            AutoSize = true,
            Margin = new Padding(0, 5, 10, 0)
        });
        _lnkPayPal = new LinkLabel { Text = "PayPal", AutoSize = true, Margin = new Padding(0, 5, 14, 0) };
        _lnkEthereum = new LinkLabel { Text = "Copy ETH address", AutoSize = true, Margin = new Padding(0, 5, 14, 0) };
        _lnkBitcoin = new LinkLabel { Text = "Copy BTC address", AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
        supportPanel.Controls.AddRange(new Control[] { _lnkPayPal, _lnkEthereum, _lnkBitcoin });

        _lblStatus = new Label
        {
            Text = "Ready.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 2, 4, 2)
        };

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 20,
            Style = ProgressBarStyle.Continuous
        };

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 42,
            Padding = new Padding(0, 4, 0, 0)
        };

        _btnManualUpdate = new Button
        {
            Text = "Install update",
            Width = 155,
            Height = 32,
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };

        _btnCheckNow = new Button
        {
            Text = "Check Only",
            Width = 115,
            Height = 32
        };

        btnPanel.Controls.Add(_btnManualUpdate);
        btnPanel.Controls.Add(_btnCheckNow);

        mainLayout.Controls.Add(grpFlavor);
        mainLayout.Controls.Add(grpInfo);
        mainLayout.Controls.Add(grpSettings);
        mainLayout.Controls.Add(supportPanel);
        mainLayout.Controls.Add(_lblStatus);
        mainLayout.Controls.Add(_progressBar);
        mainLayout.Controls.Add(btnPanel);

        Controls.Add(mainLayout);
    }
}
