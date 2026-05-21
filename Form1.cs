using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinPods;

public partial class Form1 : Form
{
    private readonly BluetoothAirPodsScanner scanner = new();
    private readonly AirPodsAapClient aapClient = new();
    private readonly CallQualityGuard callQualityGuard = new();
    private readonly WinPodsSettings settings = WinPodsSettings.Load();
    private readonly Dictionary<string, AirPodsReading> readingsByAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer connectedRefreshTimer = new();
    private readonly System.Windows.Forms.Timer callQualityGuardTimer = new();
    private readonly System.Windows.Forms.Timer autoScanPauseTimer = new();
    private IReadOnlyList<ConnectedBluetoothDevice> connectedBluetoothDevices = Array.Empty<ConnectedBluetoothDevice>();
    private CallQualityGuardSnapshot callQualitySnapshot = CallQualityGuardSnapshot.Disabled();
    private AirPodsReading? latestReading;
    private AirPodsListeningMode? currentListeningMode;
    private string? selectedAddress;
    private DeviceSortColumn sortColumn = DeviceSortColumn.Seen;
    private SortOrder sortOrder = SortOrder.Descending;
    private bool exiting;
    private bool scannerStarted;
    private bool connectedRefreshRunning;
    private bool listeningModeBusy;
    private bool exactBatteryRefreshRunning;
    private bool scanAutoPausedInTray;
    private bool callQualityFixRunning;
    private bool callQualityCheckRunning;
    private bool syncingCallQualityEnabled;
    private bool syncingPinnedOnly;
    private bool syncingTheme;
    private DateTimeOffset lastExactBatteryRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset lastCallQualityNotification = DateTimeOffset.MinValue;
    private DateTimeOffset? enteredTrayAt;
    private string? lastCallQualityNotificationKey;
    private bool refreshingDeviceCards;

    public Form1()
    {
        InitializeComponent();
        scanner.ReadingReceived += ScannerOnReadingReceived;
        scanner.ScannerStatusChanged += ScannerOnScannerStatusChanged;
        connectedRefreshTimer.Interval = 5000;
        connectedRefreshTimer.Tick += connectedRefreshTimer_Tick;
        callQualityGuardTimer.Interval = 3000;
        callQualityGuardTimer.Tick += callQualityGuardTimer_Tick;
        autoScanPauseTimer.Interval = 5000;
        autoScanPauseTimer.Tick += autoScanPauseTimer_Tick;
        RestoreDeviceSort();
        RestoreWindowLayout();
        SyncCallQualityEnabledUi();
        SyncPinnedOnlyUi();
        SyncThemeUi();
        UpdateReading(null);
        UpdateScanControls();
        UpdateListeningModeUi();
        UpdateSortButtonText();
        ApplyTheme();
        QueueCallQualityGuardRefresh();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (settings.WindowMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }
        Activate();

        if (!scannerStarted)
        {
            scannerStarted = true;
            StartScan();
            connectedRefreshTimer.Start();
            callQualityGuardTimer.Start();
            autoScanPauseTimer.Start();
            _ = RefreshConnectedDevicesAsync();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            SaveWindowLayout();
            e.Cancel = true;
            Hide();
            enteredTrayAt = DateTimeOffset.Now;
            notifyIcon.ShowBalloonTip(1800, "WinPods", "Bezim v tray. Dvojklik otvori okno.", ToolTipIcon.Info);
            return;
        }

        SaveWindowLayout();
        base.OnFormClosing(e);
    }

    private void ScannerOnScannerStatusChanged(object? sender, string e)
    {
        if (IsDisposed)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            statusValueLabel.Text = scanAutoPausedInTray && e.Equals("Stopped", StringComparison.OrdinalIgnoreCase)
                ? "Scan auto-paused in tray"
                : e;
            UpdateScanControls();
            UpdateTray();
        });
    }

    private void ScannerOnReadingReceived(object? sender, AirPodsReading reading)
    {
        if (IsDisposed)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            readingsByAddress[reading.Address] = reading;
            RefreshDeviceList();

            if (selectedAddress is null)
            {
                selectedAddress = SelectInitialAddress(reading);
                SelectDeviceInList(selectedAddress);
            }

            if (reading.Address.Equals(selectedAddress, StringComparison.OrdinalIgnoreCase))
            {
                UpdateReading(reading);
            }
            else
            {
                UpdateDeviceHint();
            }
        });
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (!IsHandleCreated || !InvokeRequired)
        {
            action();
            return;
        }

        BeginInvoke(action);
    }

    private void UpdateReading(AirPodsReading? reading)
    {
        latestReading = reading ?? latestReading;

        if (latestReading is null)
        {
            modelValueLabel.Text = "-";
            leftBatteryLabel.Text = "-";
            rightBatteryLabel.Text = "-";
            caseBatteryLabel.Text = "-";
            signalValueLabel.Text = "-";
            addressValueLabel.Text = "-";
            connectedValueLabel.Text = "-";
            rawValueBox.Text = "Cakam na AirPods BLE packet. Otvor case pri PC.";
        }
        else
        {
            modelValueLabel.Text = $"{latestReading.Model} ({latestReading.Color})";
            leftBatteryLabel.Text = AirPodsReading.FormatBattery(latestReading.LeftBattery, latestReading.LeftCharging);
            rightBatteryLabel.Text = AirPodsReading.FormatBattery(latestReading.RightBattery, latestReading.RightCharging);
            caseBatteryLabel.Text = AirPodsReading.FormatBattery(latestReading.CaseBattery, latestReading.CaseCharging);
            FitBatteryLabels();
            signalValueLabel.Text = $"{latestReading.Rssi} dBm, {latestReading.SeenAt:HH:mm:ss}";
            addressValueLabel.Text = latestReading.Address;
            connectedValueLabel.Text = GetConnectedText(latestReading);
            if (!rawValueBox.Text.StartsWith("AAP ", StringComparison.Ordinal))
            {
                rawValueBox.Text = "BLE data received. Raw advertisement hidden.";
            }
        }

        UpdatePinDeviceButton();
        UpdateListeningModeUi();
        UpdateTray();
        MaybeRefreshExactBattery();
    }

    private bool IsDarkTheme => settings.DarkThemeEnabled;

    private Color AppBack => IsDarkTheme ? Color.FromArgb(10, 10, 10) : Color.White;

    private Color PanelBack => IsDarkTheme ? Color.FromArgb(17, 17, 17) : Color.FromArgb(247, 248, 250);

    private Color SurfaceBack => IsDarkTheme ? Color.FromArgb(24, 24, 24) : Color.White;

    private Color SoftBack => IsDarkTheme ? Color.FromArgb(32, 32, 32) : Color.FromArgb(246, 248, 250);

    private Color TextMain => IsDarkTheme ? Color.FromArgb(245, 245, 245) : Color.FromArgb(28, 34, 42);

    private Color TextMuted => IsDarkTheme ? Color.FromArgb(178, 178, 178) : Color.FromArgb(80, 88, 98);

    private Color TextFaint => IsDarkTheme ? Color.FromArgb(145, 145, 145) : Color.FromArgb(100, 108, 120);

    private Color BorderColor => IsDarkTheme ? Color.FromArgb(74, 74, 74) : Color.FromArgb(205, 213, 224);

    private Color AccentColor => Color.FromArgb(10, 92, 190);

    private Color AccentText => Color.White;

    private Color GoodColor => IsDarkTheme ? Color.FromArgb(230, 230, 230) : Color.FromArgb(36, 120, 90);

    private Color DangerColor => IsDarkTheme ? Color.FromArgb(210, 88, 88) : Color.FromArgb(160, 55, 55);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitBatteryLabels();
        RefreshRoundedButtonRegions();
    }

    private void mainSplit_SplitterMoved(object sender, SplitterEventArgs e)
    {
        FitBatteryLabels();
        SaveWindowLayout();
    }

    private void FitBatteryLabels()
    {
        FitBatteryLabel(leftBatteryLabel);
        FitBatteryLabel(rightBatteryLabel);
        FitBatteryLabel(caseBatteryLabel);
    }

    private static void FitBatteryLabel(Label label)
    {
        const float maxSize = 17f;
        const float minSize = 9f;

        var text = string.IsNullOrWhiteSpace(label.Text) ? "-" : label.Text;
        var available = Math.Max(24, label.ClientSize.Width - 4);

        for (var size = maxSize; size >= minSize; size -= 0.5f)
        {
            using var testFont = new Font(label.Font.FontFamily, size, FontStyle.Bold);
            var measured = TextRenderer.MeasureText(text, testFont, Size.Empty, TextFormatFlags.NoPadding);
            if (measured.Width <= available)
            {
                label.Font = new Font(label.Font.FontFamily, size, FontStyle.Bold);
                return;
            }
        }

        label.Font = new Font(label.Font.FontFamily, minSize, FontStyle.Bold);
    }

    private void RefreshDeviceList()
    {
        var selected = selectedAddress;
        if (refreshingDeviceCards)
        {
            return;
        }

        refreshingDeviceCards = true;
        SetRedraw(deviceCardsPanel, false);
        deviceCardsPanel.SuspendLayout();

        try
        {
            deviceCardsPanel.Controls.Clear();

            var displayReadings = GetDisplayReadings();
            foreach (var reading in SortReadings(displayReadings))
            {
                deviceCardsPanel.Controls.Add(CreateDeviceCard(reading));
            }

            UpdateSortButtonText();
        }
        finally
        {
            deviceCardsPanel.ResumeLayout();
            SetRedraw(deviceCardsPanel, true);
            deviceCardsPanel.Invalidate(true);
            refreshingDeviceCards = false;
        }

        if (selected is not null)
        {
            SelectDeviceInList(selected);
        }
        else if (deviceCardsPanel.Controls.Count > 0 &&
            deviceCardsPanel.Controls[0].Tag is AirPodsReading firstReading)
        {
            selectedAddress = firstReading.Address;
            UpdateReading(firstReading);
        }

        UpdateDeviceHint();
    }

    private void SelectDeviceInList(string address)
    {
        AirPodsReading? fallback = null;
        foreach (Control control in deviceCardsPanel.Controls)
        {
            if (control.Tag is not AirPodsReading reading)
            {
                continue;
            }

            var selected = reading.Address.Equals(address, StringComparison.OrdinalIgnoreCase);
            control.BackColor = selected ? AccentColor : BorderColor;
            if (control is ModernPanel panel)
            {
                panel.BorderColor = selected ? AccentColor : BorderColor;
                panel.FillColor = selected ? AccentColor : BorderColor;
                panel.FillColor2 = selected ? AccentColor : BorderColor;
                panel.Invalidate();
            }

            control.Padding = selected ? new Padding(3) : new Padding(1);

            if (selected)
            {
                fallback = reading;
            }
        }

        if (fallback is not null)
        {
            UpdateReading(fallback);
        }
        else if (settings.ShowOnlyPinnedAirPods && selectedAddress is not null)
        {
            selectedAddress = GetDisplayReadings().FirstOrDefault()?.Address;
            if (selectedAddress is not null)
            {
                SelectDeviceInList(selectedAddress);
            }
        }
    }

    private Control CreateDeviceCard(AirPodsReading reading)
    {
        var connected = IsReadingConnected(reading);
        var pinned = IsPinned(reading);
        var selected = selectedAddress is not null &&
            reading.Address.Equals(selectedAddress, StringComparison.OrdinalIgnoreCase);
        const int cardWidth = 260;
        var card = new ModernPanel
        {
            Width = cardWidth,
            Height = 312,
            Margin = new Padding(0, 0, 14, 14),
            BackColor = selected ? AccentColor : BorderColor,
            BorderColor = selected ? AccentColor : BorderColor,
            FillColor = selected ? AccentColor : BorderColor,
            FillColor2 = selected ? AccentColor : BorderColor,
            Padding = selected ? new Padding(3) : new Padding(1),
            Radius = 28,
            Tag = reading,
            Cursor = Cursors.Hand,
        };

        var inner = new ModernPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceBack,
            BorderColor = Color.Transparent,
            DrawBorder = false,
            FillColor = SurfaceBack,
            FillColor2 = IsDarkTheme ? Color.FromArgb(30, 30, 30) : Color.FromArgb(252, 253, 255),
            Radius = 24,
            Cursor = Cursors.Hand,
        };
        card.Controls.Add(inner);

        var visual = new AirPodsVisualPanel
        {
            Dock = DockStyle.Top,
            Battery = reading.BestBattery ?? 0,
            Connected = connected,
        };
        inner.Controls.Add(visual);

        var model = MakeCardLabel(reading.Model, 12.5F, FontStyle.Bold, TextMain, 14, 132, cardWidth - 30, 24);
        inner.Controls.Add(model);
        if (pinned)
        {
            var pin = MakeCardLabel("PINNED", 7.5F, FontStyle.Bold, Color.White, cardWidth - 78, 134, 58, 18);
            pin.BackColor = GoodColor;
            pin.ForeColor = AccentText;
            pin.TextAlign = ContentAlignment.MiddleCenter;
            inner.Controls.Add(pin);
        }

        var subTitle = MakeCardLabel(ModelGeneration(reading), 8.5F, FontStyle.Bold, TextFaint, 14, 154, cardWidth - 30, 20);
        inner.Controls.Add(subTitle);

        AddInfoRow(inner, "Bluetooth", connected ? "Connected" : "Not connected", connected, 182, cardWidth);
        AddBatteryBoxes(inner, reading, 224, cardWidth);
        AddInfoRow(inner, "Signal", SignalText(reading.Rssi), reading.Rssi >= -65, 284, cardWidth);
        AddInfoRow(inner, "Seen", SeenText(reading.SeenAt), true, 306, cardWidth);
        var address = MakeCardLabel(reading.Address, 7.5F, FontStyle.Bold, TextFaint, 96, 306, 145, 20);
        address.TextAlign = ContentAlignment.MiddleRight;
        inner.Controls.Add(address);

        void SelectCard(object? _, EventArgs __)
        {
            selectedAddress = reading.Address;
            UpdateReading(reading);
            RefreshDeviceList();
            UpdateListeningModeUi();
        }

        AttachClickRecursive(card, SelectCard);
        return card;
    }

    private static Label MakeCardLabel(string text, float size, FontStyle style, Color color, int x, int y, int width, int height)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            Location = new Point(x, y),
            Size = new Size(width, height),
            AutoEllipsis = true,
            BackColor = Color.Transparent,
        };
    }

    private void AddInfoRow(Control parent, string label, string value, bool greenDot, int y, int width)
    {
        var left = MakeCardLabel(label, 8.5F, FontStyle.Bold, TextFaint, 14, y, 96, 20);
        var right = MakeCardLabel(value, 8.5F, FontStyle.Bold, TextMain, width - 132, y, 114, 20);
        right.TextAlign = ContentAlignment.MiddleRight;
        parent.Controls.Add(left);
        parent.Controls.Add(right);

        if (greenDot)
        {
            var dot = new Panel
            {
                BackColor = Color.FromArgb(47, 230, 102),
                Location = new Point(width - 142, y + 7),
                Size = new Size(7, 7),
            };
            parent.Controls.Add(dot);
        }
    }

    private void AddBatteryBoxes(Control parent, AirPodsReading reading, int y, int width)
    {
        var title = MakeCardLabel("BATTERY", 7.5F, FontStyle.Bold, TextFaint, 14, y - 18, 100, 16);
        parent.Controls.Add(title);
        var boxWidth = (width - 52) / 3;
        AddBatteryBox(parent, "Left", AirPodsReading.FormatBattery(reading.LeftBattery, reading.LeftCharging), 14, y, boxWidth);
        AddBatteryBox(parent, "Right", AirPodsReading.FormatBattery(reading.RightBattery, reading.RightCharging), 26 + boxWidth, y, boxWidth);
        AddBatteryBox(parent, "Case", AirPodsReading.FormatBattery(reading.CaseBattery, reading.CaseCharging), 38 + boxWidth * 2, y, boxWidth);
    }

    private void AddBatteryBox(Control parent, string label, string value, int x, int y, int width)
    {
        var box = new ModernPanel
        {
            BackColor = SoftBack,
            BorderColor = BorderColor,
            FillColor = SoftBack,
            FillColor2 = IsDarkTheme ? Color.FromArgb(38, 38, 38) : Color.White,
            Location = new Point(x, y),
            Radius = 12,
            Size = new Size(width, 44),
        };
        var titleLabel = MakeCardLabel(label, 7.5F, FontStyle.Bold, TextMuted, 0, 5, width, 16);
        titleLabel.TextAlign = ContentAlignment.MiddleCenter;
        box.Controls.Add(titleLabel);
        var battery = MakeCardLabel(value, 10F, FontStyle.Bold, TextMain, 0, 20, width, 20);
        battery.TextAlign = ContentAlignment.MiddleCenter;
        box.Controls.Add(battery);
        parent.Controls.Add(box);
    }

    private static void AttachClickRecursive(Control control, EventHandler handler)
    {
        control.Click += handler;
        foreach (Control child in control.Controls)
        {
            child.Cursor = Cursors.Hand;
            AttachClickRecursive(child, handler);
        }
    }

    private void UpdateDeviceHint()
    {
        if (!scanner.IsRunning)
        {
            deviceHintLabel.Text = "Scan paused. Start scan to find nearby AirPods.";
            return;
        }

        var count = GetDisplayReadings().Count;
        var pinnedCount = settings.PinnedAirPodsAddresses.Count;
        var connected = connectedBluetoothDevices.Count(IsAppleAudioDevice);
        deviceHintLabel.Text = settings.ShowOnlyPinnedAirPods && pinnedCount > 0
            ? $"{count} pinned AirPods shown. {connected} Windows BT connected."
            : settings.ShowOnlyPinnedAirPods
                ? "No pinned AirPods yet. Select AirPods and pin them."
                : count == 0
            ? "Open AirPods case near PC."
            : $"{count} AirPods/Beats device(s) found. {connected} Windows BT connected.";
    }

    private void UpdateTray()
    {
        var oldIcon = notifyIcon.Icon;
        var callState = callQualitySnapshot.Severity switch
        {
            CallQualitySeverity.Danger => "call risk",
            CallQualitySeverity.Warning => "guard warn",
            CallQualitySeverity.Good => "guard ok",
            _ => null,
        };
        var deviceState = latestReading is null ? "cakam na AirPods" : latestReading.Summary;
        var parts = new[] { ScanStateText().ToLowerInvariant(), callState, deviceState }
            .Where(static part => !string.IsNullOrWhiteSpace(part));
        SetTrayText($"WinPods - {string.Join(", ", parts)}");
        notifyIcon.Icon = TrayIconFactory.Create(latestReading is not null);
        oldIcon?.Dispose();
    }

    private void SetTrayText(string text)
    {
        notifyIcon.Text = text.Length <= 63 ? text : $"{text[..60]}...";
    }

    private void ShowWindow()
    {
        Show();
        enteredTrayAt = null;
        if (!scanner.IsRunning)
        {
            StartScan();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = settings.WindowMaximized ? FormWindowState.Maximized : FormWindowState.Normal;
        }

        Activate();
    }

    private void refreshMenuItem_Click(object sender, EventArgs e)
        => ToggleScan();

    private async void trayTransparencyMenuItem_Click(object sender, EventArgs e)
        => await SetListeningModeAsync(AirPodsListeningMode.Transparency);

    private async void trayAdaptiveMenuItem_Click(object sender, EventArgs e)
        => await SetListeningModeAsync(AirPodsListeningMode.Adaptive);

    private async void trayNoiseCancelMenuItem_Click(object sender, EventArgs e)
        => await SetListeningModeAsync(AirPodsListeningMode.NoiseCancellation);

    private void trayCallQualityEnabledMenuItem_Click(object sender, EventArgs e)
        => SetCallQualityGuardEnabled(trayCallQualityEnabledMenuItem.Checked);

    private async void trayApplyCallQualityFixMenuItem_Click(object sender, EventArgs e)
        => await ApplyCallQualityFixAsync();

    private void trayOpenSoundSettingsMenuItem_Click(object sender, EventArgs e)
        => OpenSoundSettings();

    private void refreshButton_Click(object sender, EventArgs e)
        => ToggleScan();

    private void ToggleScan()
    {
        if (scanner.IsRunning)
        {
            StopScan();
            return;
        }

        StartScan();
    }

    private void StartScan()
    {
        scanAutoPausedInTray = false;
        statusValueLabel.Text = "Starting scan";
        scanner.Start();
        UpdateScanControls();
        _ = RefreshConnectedDevicesAsync();
    }

    private void StopScan()
    {
        statusValueLabel.Text = "Stopping scan";
        scanner.Stop();
        UpdateScanControls();
    }

    private void UpdateScanControls()
    {
        var running = scanner.IsRunning;
        refreshButton.Text = running ? "Stop scan" : "Start scan";
        refreshButton.BackColor = running
            ? DangerColor
            : IsDarkTheme ? Color.FromArgb(245, 245, 245) : GoodColor;
        refreshButton.ForeColor = running
            ? Color.White
            : IsDarkTheme ? Color.FromArgb(12, 12, 12) : Color.White;
        PrepareRoundedButton(refreshButton);
        refreshMenuItem.Text = running ? "Stop scan" : "Start scan";
        UpdateDeviceHint();
    }

    private string ScanStateText() => scanner.IsRunning ? "Scanning" : "Scan paused";

    private void autoScanPauseTimer_Tick(object? sender, EventArgs e)
        => MaybeAutoPauseScan();

    private void MaybeAutoPauseScan()
    {
        if (!scanner.IsRunning || Visible || enteredTrayAt is null)
        {
            return;
        }

        if (DateTimeOffset.Now - enteredTrayAt.Value < TimeSpan.FromSeconds(30))
        {
            return;
        }

        if (connectedBluetoothDevices.Any(IsAppleAudioDevice))
        {
            scanAutoPausedInTray = true;
            StopScan();
        }
    }

    private void pinnedOnlyCheckBox_CheckedChanged(object sender, EventArgs e)
    {
        if (syncingPinnedOnly)
        {
            return;
        }

        settings.ShowOnlyPinnedAirPods = pinnedOnlyCheckBox.Checked;
        settings.Save();
        RefreshDeviceList();
    }

    private void themeToggleSwitch_CheckedChanged(object sender, EventArgs e)
    {
        if (syncingTheme)
        {
            return;
        }

        settings.DarkThemeEnabled = themeToggleSwitch.Checked;
        settings.Save();
        ApplyTheme();
        RefreshDeviceList();
        UpdateScanControls();
        UpdateListeningModeUi();
        UpdatePinDeviceButton();
        ApplyCallQualitySnapshot(callQualitySnapshot);
    }

    private void pinDeviceButton_Click(object sender, EventArgs e)
    {
        if (latestReading is null)
        {
            return;
        }

        TogglePinned(latestReading);
        RefreshDeviceList();
        UpdatePinDeviceButton();
    }

    private void callQualityGuardTimer_Tick(object? sender, EventArgs e)
        => QueueCallQualityGuardRefresh();

    private void callQualityGuardCheckBox_CheckedChanged(object sender, EventArgs e)
    {
        if (!syncingCallQualityEnabled)
        {
            SetCallQualityGuardEnabled(callQualityGuardCheckBox.Checked);
        }
    }

    private async void callQualityFixButton_Click(object sender, EventArgs e)
        => await ApplyCallQualityFixAsync();

    private void callQualitySettingsButton_Click(object sender, EventArgs e)
        => OpenSoundSettings();

    private void SetCallQualityGuardEnabled(bool enabled)
    {
        settings.CallQualityGuardEnabled = enabled;
        settings.Save();
        SyncCallQualityEnabledUi();
        QueueCallQualityGuardRefresh();
    }

    private void SyncCallQualityEnabledUi()
    {
        syncingCallQualityEnabled = true;
        callQualityGuardCheckBox.Checked = settings.CallQualityGuardEnabled;
        trayCallQualityEnabledMenuItem.Checked = settings.CallQualityGuardEnabled;
        syncingCallQualityEnabled = false;
    }

    private void QueueCallQualityGuardRefresh()
    {
        if (callQualityFixRunning || callQualityCheckRunning || IsDisposed)
        {
            return;
        }

        _ = RefreshCallQualityGuardAsync();
    }

    private async Task RefreshCallQualityGuardAsync()
    {
        if (callQualityFixRunning || callQualityCheckRunning || IsDisposed)
        {
            return;
        }

        callQualityCheckRunning = true;
        try
        {
            var enabled = settings.CallQualityGuardEnabled;
            var snapshot = await Task.Run(() => callQualityGuard.Inspect(enabled));
            RunOnUiThread(() => ApplyCallQualitySnapshot(snapshot));
        }
        finally
        {
            callQualityCheckRunning = false;
        }
    }

    private async Task ApplyCallQualityFixAsync()
    {
        if (callQualityFixRunning || !settings.CallQualityGuardEnabled)
        {
            return;
        }

        callQualityFixRunning = true;
        callQualityStatusLabel.Text = "Applying recommended route...";
        callQualityFixButton.Enabled = false;
        trayApplyCallQualityFixMenuItem.Enabled = false;

        try
        {
            var snapshot = await Task.Run(callQualityGuard.ApplyRecommendedRoute);
            ApplyCallQualitySnapshot(snapshot);
            rawValueBox.Text =
                $"Call Quality Guard route fix.\r\n" +
                $"Output: {snapshot.RecommendedRender?.Name ?? "none"}\r\n" +
                $"Mic: {snapshot.RecommendedCapture?.Name ?? "none"}\r\n" +
                $"Status: {snapshot.Status}\r\n" +
                snapshot.Detail;
        }
        catch (Exception ex)
        {
            ApplyCallQualitySnapshot(CallQualityGuardSnapshot.Error(
                "Route fix failed",
                ex.Message,
                null,
                null,
                callQualitySnapshot.CurrentRender,
                callQualitySnapshot.CurrentCapture));
            rawValueBox.Text = ex.ToString();
        }
        finally
        {
            callQualityFixRunning = false;
            QueueCallQualityGuardRefresh();
        }
    }

    private void ApplyCallQualitySnapshot(CallQualityGuardSnapshot snapshot)
    {
        callQualitySnapshot = snapshot;
        callQualityStatusLabel.Text = snapshot.Status;
        callQualityDetailLabel.Text = snapshot.Detail;
        callQualityFixButton.Enabled = settings.CallQualityGuardEnabled && !callQualityFixRunning && snapshot.CanApplyFix;
        trayApplyCallQualityFixMenuItem.Enabled = callQualityFixButton.Enabled;
        trayCallQualityGuardMenuItem.Text = snapshot.Severity switch
        {
            CallQualitySeverity.Danger => "Call Quality Guard: Risk",
            CallQualitySeverity.Warning => "Call Quality Guard: Warning",
            CallQualitySeverity.Good => "Call Quality Guard: OK",
            CallQualitySeverity.Disabled => "Call Quality Guard: Off",
            CallQualitySeverity.Error => "Call Quality Guard: Error",
            _ => "Call Quality Guard",
        };

        var (panelColor, statusColor) = snapshot.Severity switch
        {
            CallQualitySeverity.Danger => (IsDarkTheme ? Color.FromArgb(42, 24, 24) : Color.FromArgb(255, 241, 241), IsDarkTheme ? Color.FromArgb(255, 148, 148) : Color.FromArgb(170, 45, 45)),
            CallQualitySeverity.Warning => (IsDarkTheme ? Color.FromArgb(44, 35, 18) : Color.FromArgb(255, 248, 226), IsDarkTheme ? Color.FromArgb(255, 204, 102) : Color.FromArgb(150, 92, 0)),
            CallQualitySeverity.Good => (IsDarkTheme ? Color.FromArgb(26, 36, 30) : Color.FromArgb(241, 250, 246), GoodColor),
            CallQualitySeverity.Disabled => (SoftBack, TextMuted),
            CallQualitySeverity.Error => (IsDarkTheme ? Color.FromArgb(42, 24, 24) : Color.FromArgb(255, 241, 241), IsDarkTheme ? Color.FromArgb(255, 148, 148) : Color.FromArgb(170, 45, 45)),
            _ => (IsDarkTheme ? Color.FromArgb(24, 32, 42) : Color.FromArgb(241, 247, 252), AccentColor),
        };
        callQualityPanel.BackColor = panelColor;
        callQualityPanel.FillColor = panelColor;
        callQualityPanel.FillColor2 = IsDarkTheme
            ? ControlPaint.Light(panelColor, 0.08f)
            : ControlPaint.Light(panelColor, 0.35f);
        callQualityPanel.BorderColor = BorderColor;
        callQualityPanel.Invalidate();
        callQualityStatusLabel.ForeColor = statusColor;
        callQualityTitleLabel.ForeColor = TextMain;
        callQualityDetailLabel.ForeColor = TextMuted;
        ApplyCheckBoxTheme(callQualityGuardCheckBox, callQualityPanel.BackColor);

        MaybeShowCallQualityNotification(snapshot);
        UpdateTray();
    }

    private void MaybeShowCallQualityNotification(CallQualityGuardSnapshot snapshot)
    {
        if (!settings.CallQualityNotificationsEnabled || !snapshot.ShouldNotify)
        {
            if (snapshot.Severity is CallQualitySeverity.Good or CallQualitySeverity.Neutral)
            {
                lastCallQualityNotificationKey = null;
            }

            return;
        }

        var key = $"{snapshot.Severity}:{snapshot.CurrentRender?.Id}:{snapshot.CurrentCapture?.Id}";
        if (key == lastCallQualityNotificationKey &&
            DateTimeOffset.Now - lastCallQualityNotification < TimeSpan.FromMinutes(2))
        {
            return;
        }

        lastCallQualityNotificationKey = key;
        lastCallQualityNotification = DateTimeOffset.Now;
        notifyIcon.ShowBalloonTip(
            4500,
            "AirPods call audio risk",
            "Hands-Free mode can make sound low quality. Use Fix route or select laptop mic.",
            ToolTipIcon.Warning);
    }

    private static void OpenSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("mmsys.cpl") { UseShellExecute = true });
        }
    }

    private void SyncPinnedOnlyUi()
    {
        syncingPinnedOnly = true;
        pinnedOnlyCheckBox.Checked = settings.ShowOnlyPinnedAirPods;
        syncingPinnedOnly = false;
    }

    private void SyncThemeUi()
    {
        syncingTheme = true;
        themeToggleSwitch.Checked = settings.DarkThemeEnabled;
        syncingTheme = false;
    }

    private void ApplyTheme()
    {
        BackColor = AppBack;
        rootLayout.BackColor = AppBack;
        headerPanel.BackColor = AppBack;
        titleLabel.ForeColor = TextMain;
        subtitleLabel.ForeColor = TextMuted;
        themeLabel.ForeColor = TextMuted;

        themeToggleSwitch.TrackOn = Color.FromArgb(245, 245, 245);
        themeToggleSwitch.TrackOff = Color.White;
        themeToggleSwitch.KnobOn = Color.FromArgb(15, 15, 15);
        themeToggleSwitch.KnobOff = Color.FromArgb(20, 20, 20);
        themeToggleSwitch.BorderColor = BorderColor;
        themeToggleSwitch.Invalidate();

        devicesPanel.BackColor = PanelBack;
        deviceCardsPanel.BackColor = PanelBack;
        detailPanel.BackColor = SurfaceBack;
        detailContentPanel.BackColor = SurfaceBack;
        rawValueBox.BackColor = IsDarkTheme ? Color.FromArgb(14, 14, 14) : Color.FromArgb(250, 251, 252);
        rawValueBox.ForeColor = TextMain;

        foreach (var label in AllLabels(this))
        {
            if (label == titleLabel)
            {
                label.ForeColor = TextMain;
            }
            else if (label == connectedValueLabel)
            {
                label.ForeColor = AccentColor;
            }
            else if (label.Font.Bold)
            {
                label.ForeColor = label.Font.Size >= 12 ? TextMain : TextMuted;
            }
            else
            {
                label.ForeColor = TextMuted;
            }
        }

        ApplyButtonTheme(sortButton, false);
        ApplyButtonTheme(pinDeviceButton, false);
        ApplyButtonTheme(callQualitySettingsButton, false);
        ApplyButtonTheme(callQualityFixButton, true);
        foreach (var button in new[] { refreshButton, sortButton, pinDeviceButton, callQualityFixButton, callQualitySettingsButton, transparencyButton, adaptiveButton, noiseCancelButton })
        {
            ApplyRoundedRegion(button, 14);
        }

        pinnedOnlyCheckBox.ForeColor = TextMuted;
        ApplyCheckBoxTheme(pinnedOnlyCheckBox, PanelBack);
        callQualityGuardCheckBox.ForeColor = TextMuted;
        ApplyCheckBoxTheme(callQualityGuardCheckBox, callQualityPanel.BackColor);

        leftBatteryPanel.BackColor = IsDarkTheme ? Color.FromArgb(28, 28, 28) : Color.FromArgb(241, 250, 246);
        ApplyModernPanelTheme(leftBatteryPanel, IsDarkTheme ? Color.FromArgb(28, 28, 28) : Color.FromArgb(245, 252, 249), IsDarkTheme ? Color.FromArgb(36, 36, 36) : Color.FromArgb(235, 247, 241));
        rightBatteryPanel.BackColor = IsDarkTheme ? Color.FromArgb(32, 32, 32) : Color.FromArgb(241, 247, 252);
        ApplyModernPanelTheme(rightBatteryPanel, IsDarkTheme ? Color.FromArgb(30, 30, 32) : Color.FromArgb(246, 250, 253), IsDarkTheme ? Color.FromArgb(38, 38, 40) : Color.FromArgb(235, 244, 251));
        caseBatteryPanel.BackColor = IsDarkTheme ? Color.FromArgb(36, 36, 36) : Color.FromArgb(252, 248, 240);
        ApplyModernPanelTheme(caseBatteryPanel, IsDarkTheme ? Color.FromArgb(34, 34, 34) : Color.FromArgb(253, 250, 245), IsDarkTheme ? Color.FromArgb(42, 42, 42) : Color.FromArgb(249, 242, 229));
    }

    private void ApplyButtonTheme(Button button, bool primary)
    {
        button.BackColor = primary ? AccentColor : SurfaceBack;
        button.ForeColor = primary ? AccentText : TextMain;
        PrepareRoundedButton(button);
    }

    private void PrepareRoundedButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.BorderColor = button.BackColor;
        button.UseVisualStyleBackColor = false;
        ApplyRoundedRegion(button, Math.Min(14, Math.Max(8, button.Height / 2)));
    }

    private void RefreshRoundedButtonRegions()
    {
        foreach (var button in new[] { refreshButton, sortButton, pinDeviceButton, callQualityFixButton, callQualitySettingsButton, transparencyButton, adaptiveButton, noiseCancelButton })
        {
            PrepareRoundedButton(button);
        }
    }

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0)
        {
            return;
        }

        using var path = CreateRoundedRectanglePath(new Rectangle(0, 0, control.Width, control.Height), radius);
        control.Region?.Dispose();
        control.Region = new Region(path);
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void ApplyCheckBoxTheme(CheckBox checkBox, Color backColor)
    {
        checkBox.BackColor = backColor;
        checkBox.ForeColor = TextMuted;
        checkBox.FlatStyle = FlatStyle.Flat;
        checkBox.FlatAppearance.BorderColor = BorderColor;
        checkBox.FlatAppearance.CheckedBackColor = AccentColor;
        checkBox.FlatAppearance.MouseDownBackColor = SoftBack;
        checkBox.FlatAppearance.MouseOverBackColor = SoftBack;
        checkBox.UseVisualStyleBackColor = false;
    }

    private void ApplyModernPanelTheme(ModernPanel panel, Color fill, Color fill2)
    {
        panel.FillColor = fill;
        panel.FillColor2 = fill2;
        panel.BorderColor = BorderColor;
        panel.Invalidate();
    }

    private static IEnumerable<Label> AllLabels(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label label)
            {
                yield return label;
            }

            foreach (var nested in AllLabels(child))
            {
                yield return nested;
            }
        }
    }

    private string SelectInitialAddress(AirPodsReading fallback)
    {
        var pinned = readingsByAddress.Values
            .Where(IsPinned)
            .OrderByDescending(IsReadingConnected)
            .ThenByDescending(static reading => reading.Rssi)
            .ThenByDescending(static reading => reading.SeenAt)
            .FirstOrDefault();

        return pinned?.Address ?? fallback.Address;
    }

    private void TogglePinned(AirPodsReading reading)
    {
        var addresses = GetRelatedReadings(reading)
            .Select(static item => NormalizeAddress(item.Address))
            .Append(NormalizeAddress(reading.Address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pinned = settings.PinnedAirPodsAddresses;
        var shouldUnpin = addresses.Any(address =>
            pinned.Any(item => item.Equals(address, StringComparison.OrdinalIgnoreCase)));
        if (shouldUnpin)
        {
            settings.PinnedAirPodsAddresses = pinned
                .Where(item => !addresses.Any(address => item.Equals(address, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        else
        {
            pinned.AddRange(addresses);
            settings.PinnedAirPodsAddresses = pinned
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        settings.Save();
    }

    private bool IsPinned(AirPodsReading reading)
    {
        var address = NormalizeAddress(reading.Address);
        if (settings.PinnedAirPodsAddresses.Any(item =>
            item.Equals(address, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return GetRelatedReadings(reading).Any(related =>
            settings.PinnedAirPodsAddresses.Any(item =>
                item.Equals(NormalizeAddress(related.Address), StringComparison.OrdinalIgnoreCase)));
    }

    private IEnumerable<AirPodsReading> GetRelatedReadings(AirPodsReading reading)
    {
        return readingsByAddress.Values.Where(candidate =>
            candidate.Address.Equals(reading.Address, StringComparison.OrdinalIgnoreCase) ||
            ShouldMergeReadings(candidate, reading));
    }

    private static string NormalizeAddress(string address) =>
        address.Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();

    private void UpdatePinDeviceButton()
    {
        if (latestReading is null)
        {
            pinDeviceButton.Enabled = false;
            pinDeviceButton.Text = "Pin as mine";
            pinDeviceButton.BackColor = IsDarkTheme ? Color.FromArgb(38, 38, 38) : Color.FromArgb(238, 240, 244);
            pinDeviceButton.ForeColor = TextFaint;
            PrepareRoundedButton(pinDeviceButton);
            return;
        }

        var pinned = IsPinned(latestReading);
        pinDeviceButton.Enabled = true;
        pinDeviceButton.Text = pinned ? "Unpin mine" : "Pin as mine";
        pinDeviceButton.BackColor = pinned
            ? IsDarkTheme ? Color.FromArgb(245, 245, 245) : GoodColor
            : SurfaceBack;
        pinDeviceButton.ForeColor = pinned
            ? IsDarkTheme ? Color.FromArgb(12, 12, 12) : Color.White
            : TextMain;
        PrepareRoundedButton(pinDeviceButton);
    }

    private void RestoreWindowLayout()
    {
        if (settings.WindowLeft.HasValue &&
            settings.WindowTop.HasValue &&
            settings.WindowWidth.HasValue &&
            settings.WindowHeight.HasValue)
        {
            var bounds = new Rectangle(
                settings.WindowLeft.Value,
                settings.WindowTop.Value,
                Math.Max(MinimumSize.Width, settings.WindowWidth.Value),
                Math.Max(MinimumSize.Height, settings.WindowHeight.Value));

            if (IsVisibleOnAnyScreen(bounds))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
        }

        RestoreSplitterDistance();
    }

    private void RestoreSplitterDistance()
    {
        if (!settings.MainSplitterDistance.HasValue)
        {
            return;
        }

        var min = mainSplit.Panel1MinSize;
        var max = Math.Max(min, mainSplit.Width - mainSplit.Panel2MinSize - mainSplit.SplitterWidth);
        mainSplit.SplitterDistance = Math.Clamp(settings.MainSplitterDistance.Value, min, max);
    }

    private void SaveWindowLayout()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width >= MinimumSize.Width && bounds.Height >= MinimumSize.Height)
        {
            settings.WindowLeft = bounds.Left;
            settings.WindowTop = bounds.Top;
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
        }

        settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        settings.MainSplitterDistance = mainSplit.SplitterDistance;
        settings.Save();
    }

    private static bool IsVisibleOnAnyScreen(Rectangle bounds)
    {
        return Screen.AllScreens.Any(screen =>
        {
            var intersection = Rectangle.Intersect(screen.WorkingArea, bounds);
            return intersection.Width >= 120 && intersection.Height >= 80;
        });
    }

    private void deviceListView_SelectedIndexChanged(object sender, EventArgs e)
    {
    }

    private void sortButton_Click(object sender, EventArgs e)
    {
        sortMenu.Show(sortButton, new Point(0, sortButton.Height));
    }

    private void sortSignalStrongItem_Click(object sender, EventArgs e) => SetDeviceSort(DeviceSortColumn.Signal, SortOrder.Descending);

    private void sortSignalWeakItem_Click(object sender, EventArgs e) => SetDeviceSort(DeviceSortColumn.Signal, SortOrder.Ascending);

    private void sortBatteryItem_Click(object sender, EventArgs e) => SetDeviceSort(DeviceSortColumn.Battery, SortOrder.Descending);

    private void sortConnectedItem_Click(object sender, EventArgs e) => SetDeviceSort(DeviceSortColumn.Connected, SortOrder.Descending);

    private void sortSeenItem_Click(object sender, EventArgs e) => SetDeviceSort(DeviceSortColumn.Seen, SortOrder.Descending);

    private void sortNameItem_Click(object sender, EventArgs e) => SetDeviceSort(DeviceSortColumn.Name, SortOrder.Ascending);

    private void SetDeviceSort(DeviceSortColumn column, SortOrder order)
    {
        sortColumn = column;
        sortOrder = order;
        SaveDeviceSort();
        RefreshDeviceList();
    }

    private void UpdateSortButtonText()
    {
        sortSignalStrongItem.Checked = sortColumn == DeviceSortColumn.Signal && sortOrder == SortOrder.Descending;
        sortSignalWeakItem.Checked = sortColumn == DeviceSortColumn.Signal && sortOrder == SortOrder.Ascending;
        sortBatteryItem.Checked = sortColumn == DeviceSortColumn.Battery;
        sortConnectedItem.Checked = sortColumn == DeviceSortColumn.Connected;
        sortSeenItem.Checked = sortColumn == DeviceSortColumn.Seen;
        sortNameItem.Checked = sortColumn == DeviceSortColumn.Name;

        sortButton.Text = sortColumn switch
        {
            DeviceSortColumn.Signal => sortOrder == SortOrder.Descending ? "Sort: Signal down" : "Sort: Signal up",
            DeviceSortColumn.Battery => "Sort: Battery",
            DeviceSortColumn.Connected => "Sort: Connected",
            DeviceSortColumn.Seen => "Sort: Last seen",
            DeviceSortColumn.Name => "Sort: Name",
            _ => "Sort / filter",
        };
    }

    private void openMenuItem_Click(object sender, EventArgs e) => ShowWindow();

    private void exitMenuItem_Click(object sender, EventArgs e)
    {
        exiting = true;
        Close();
    }

    private void notifyIcon_DoubleClick(object sender, EventArgs e) => ShowWindow();

    private void connectedRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _ = RefreshConnectedDevicesAsync();
    }

    private async Task RefreshConnectedDevicesAsync()
    {
        if (connectedRefreshRunning)
        {
            return;
        }

        connectedRefreshRunning = true;

        try
        {
            var devices = await ConnectedBluetoothProvider.GetConnectedDevicesAsync();
            RunOnUiThread(() =>
            {
                connectedBluetoothDevices = devices;
                RefreshDeviceList();

                if (latestReading is not null)
                {
                    connectedValueLabel.Text = GetConnectedText(latestReading);
                }

                UpdateListeningModeUi();
            });
        }
        finally
        {
            connectedRefreshRunning = false;
        }
    }

    private bool IsReadingConnected(AirPodsReading reading)
    {
        if (connectedBluetoothDevices.Any(device => device.Address.Equals(reading.Address, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fallback = GetFallbackConnectedAddress();
        return fallback is not null && fallback.Equals(reading.Address, StringComparison.OrdinalIgnoreCase);
    }

    private string? GetFallbackConnectedAddress()
    {
        var appleDevices = connectedBluetoothDevices.Where(IsAppleAudioDevice).ToList();
        if (appleDevices.Count == 0 || readingsByAddress.Count == 0)
        {
            return null;
        }

        return readingsByAddress.Values
            .Where(reading => appleDevices.Any(device => IsLikelySameDevice(reading, device)))
            .OrderByDescending(static reading => reading.Rssi)
            .ThenByDescending(static reading => reading.SeenAt)
            .Select(static reading => reading.Address)
            .FirstOrDefault();
    }

    private string GetConnectedText(AirPodsReading reading)
    {
        var exact = connectedBluetoothDevices.FirstOrDefault(device =>
            device.Address.Equals(reading.Address, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return $"BT connected: {exact.Name}";
        }

        if (IsReadingConnected(reading))
        {
            var name = connectedBluetoothDevices.FirstOrDefault(IsAppleAudioDevice)?.Name;
            return string.IsNullOrWhiteSpace(name) ? "BT connected" : $"BT connected: {name}";
        }

        return "Not connected in Windows";
    }

    private ConnectedBluetoothDevice? GetTargetControlDevice()
    {
        if (latestReading is not null)
        {
            var exact = connectedBluetoothDevices.FirstOrDefault(device =>
                device.Address.Equals(latestReading.Address, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            var likely = connectedBluetoothDevices
                .Where(IsAppleAudioDevice)
                .FirstOrDefault(device => IsLikelySameDevice(latestReading, device));
            if (likely is not null)
            {
                return likely;
            }
        }

        return connectedBluetoothDevices.FirstOrDefault(IsAppleAudioDevice);
    }

    private List<AirPodsReading> GetDisplayReadings()
    {
        var readings = readingsByAddress.Values
            .OrderByDescending(IsReadingConnected)
            .ThenByDescending(static reading => reading.Rssi)
            .ThenByDescending(static reading => reading.SeenAt)
            .ToList();

        var groups = new List<List<AirPodsReading>>();
        foreach (var reading in readings)
        {
            var group = groups.FirstOrDefault(existing => existing.Any(item => ShouldMergeReadings(item, reading)));
            if (group is null)
            {
                groups.Add([reading]);
            }
            else
            {
                group.Add(reading);
            }
        }

        var merged = groups.Select(MergeReadings).ToList();
        if (settings.ShowOnlyPinnedAirPods && settings.PinnedAirPodsAddresses.Count > 0)
        {
            merged = merged.Where(IsPinned).ToList();
        }

        return merged;
    }

    private bool ShouldMergeReadings(AirPodsReading a, AirPodsReading b)
    {
        if (!a.Model.Equals(b.Model, StringComparison.OrdinalIgnoreCase) ||
            !a.Color.Equals(b.Color, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var seenDiff = (a.SeenAt - b.SeenAt).Duration();
        var signalDiff = Math.Abs(a.Rssi - b.Rssi);
        var anyConnected = IsReadingConnected(a) || IsReadingConnected(b);

        return seenDiff <= TimeSpan.FromMinutes(2) && (signalDiff <= 35 || anyConnected);
    }

    private AirPodsReading MergeReadings(List<AirPodsReading> group)
    {
        var primary = group
            .OrderByDescending(IsReadingConnected)
            .ThenByDescending(IsPinned)
            .ThenByDescending(static reading => reading.Rssi)
            .ThenByDescending(static reading => reading.SeenAt)
            .First();

        return primary with
        {
            Rssi = group.Max(static reading => reading.Rssi),
            LeftBattery = BestBatteryValue(group.Select(static reading => reading.LeftBattery)),
            RightBattery = BestBatteryValue(group.Select(static reading => reading.RightBattery)),
            CaseBattery = BestBatteryValue(group.Select(static reading => reading.CaseBattery)),
            LeftCharging = BestChargingValue(group.Select(static reading => reading.LeftCharging)),
            RightCharging = BestChargingValue(group.Select(static reading => reading.RightCharging)),
            CaseCharging = BestChargingValue(group.Select(static reading => reading.CaseCharging)),
            SeenAt = group.Max(static reading => reading.SeenAt),
        };
    }

    private static int? BestBatteryValue(IEnumerable<int?> values)
    {
        var known = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToList();
        return known.Count == 0 ? null : known.Max();
    }

    private static bool? BestChargingValue(IEnumerable<bool?> values)
    {
        if (values.Any(static value => value == true))
        {
            return true;
        }

        return values.Any(static value => value == false) ? false : null;
    }

    private async void transparencyButton_Click(object sender, EventArgs e)
        => await SetListeningModeAsync(AirPodsListeningMode.Transparency);

    private async void adaptiveButton_Click(object sender, EventArgs e)
        => await SetListeningModeAsync(AirPodsListeningMode.Adaptive);

    private async void noiseCancelButton_Click(object sender, EventArgs e)
        => await SetListeningModeAsync(AirPodsListeningMode.NoiseCancellation);

    private async Task SetListeningModeAsync(AirPodsListeningMode mode)
    {
        if (listeningModeBusy)
        {
            return;
        }

        var target = GetTargetControlDevice();
        if (target is null)
        {
            listeningModeStatusLabel.Text = "AirPods not connected in Windows Bluetooth.";
            UpdateListeningModeUi();
            return;
        }

        listeningModeBusy = true;
        listeningModeStatusLabel.Text = $"Sending {DisplayMode(mode)} to {target.Name}...";
        rawValueBox.Text = $"AAP target: {target.Name} ({target.Address})\r\nMode: {DisplayMode(mode)}\r\nConnecting...";
        UpdateListeningModeUi();

        try
        {
            var result = await aapClient.SetListeningModeAsync(target.Address, mode);
            currentListeningMode = result.ConfirmedMode ?? mode;
            listeningModeStatusLabel.Text = result.ConfirmedMode is null
                ? $"Sent {DisplayMode(mode)}. No confirmation packet received."
                : $"Mode confirmed: {DisplayMode(currentListeningMode.Value)}.";
            rawValueBox.Text = result.ResponsePacket is null
                ? $"AAP sent {DisplayMode(mode)} to {target.Name} ({target.Address}).\r\nNo confirmation packet received."
                : $"AAP sent {DisplayMode(mode)} to {target.Name} ({target.Address}).\r\nResponse: {Convert.ToHexString(result.ResponsePacket)}";
        }
        catch (Exception ex)
        {
            listeningModeStatusLabel.Text = "Mode switch failed. Full error below.";
            rawValueBox.Text = ex.ToString();
        }
        finally
        {
            listeningModeBusy = false;
            UpdateListeningModeUi();
        }
    }

    private void MaybeRefreshExactBattery()
    {
        if (exactBatteryRefreshRunning ||
            latestReading is null ||
            !IsReadingConnected(latestReading) ||
            DateTimeOffset.Now - lastExactBatteryRefresh < TimeSpan.FromSeconds(15))
        {
            return;
        }

        var target = GetTargetControlDevice();
        if (target is null)
        {
            return;
        }

        exactBatteryRefreshRunning = true;
        lastExactBatteryRefresh = DateTimeOffset.Now;
        _ = RefreshExactBatteryAsync(target);
    }

    private async Task RefreshExactBatteryAsync(ConnectedBluetoothDevice target)
    {
        try
        {
            var battery = await aapClient.GetBatteryAsync(target.Address);
            if (battery is null)
            {
                return;
            }

            RunOnUiThread(() => ApplyExactBattery(target.Address, battery));
        }
        catch
        {
            // BLE 10% battery remains as fallback when AAP battery notification is unavailable.
        }
        finally
        {
            exactBatteryRefreshRunning = false;
        }
    }

    private void ApplyExactBattery(string targetAddress, AirPodsBatterySnapshot battery)
    {
        if (latestReading is null)
        {
            return;
        }

        var updated = latestReading with
        {
            LeftBattery = battery.LeftBattery ?? latestReading.LeftBattery,
            RightBattery = battery.RightBattery ?? latestReading.RightBattery,
            CaseBattery = battery.CaseBattery ?? latestReading.CaseBattery,
            LeftCharging = battery.LeftCharging ?? latestReading.LeftCharging,
            RightCharging = battery.RightCharging ?? latestReading.RightCharging,
            CaseCharging = battery.CaseCharging ?? latestReading.CaseCharging,
        };

        readingsByAddress[updated.Address] = updated;
        latestReading = updated;

        leftBatteryLabel.Text = AirPodsReading.FormatBattery(updated.LeftBattery, updated.LeftCharging);
        rightBatteryLabel.Text = AirPodsReading.FormatBattery(updated.RightBattery, updated.RightCharging);
        caseBatteryLabel.Text = AirPodsReading.FormatBattery(updated.CaseBattery, updated.CaseCharging);
        FitBatteryLabels();
        RefreshDeviceList();

        if (!rawValueBox.Text.StartsWith("AAP sent", StringComparison.Ordinal))
        {
            rawValueBox.Text = $"AAP exact battery from {targetAddress}.\r\nLeft: {leftBatteryLabel.Text}\r\nRight: {rightBatteryLabel.Text}\r\nCase: {caseBatteryLabel.Text}";
        }

        UpdateTray();
    }

    private void UpdateListeningModeUi()
    {
        var canSend = !listeningModeBusy && GetTargetControlDevice() is not null;
        transparencyButton.Enabled = canSend;
        adaptiveButton.Enabled = canSend;
        noiseCancelButton.Enabled = canSend;
        trayListeningModeMenuItem.Enabled = canSend;
        trayTransparencyMenuItem.Enabled = canSend;
        trayAdaptiveMenuItem.Enabled = canSend;
        trayNoiseCancelMenuItem.Enabled = canSend;
        trayTransparencyMenuItem.Checked = currentListeningMode == AirPodsListeningMode.Transparency;
        trayAdaptiveMenuItem.Checked = currentListeningMode == AirPodsListeningMode.Adaptive;
        trayNoiseCancelMenuItem.Checked = currentListeningMode == AirPodsListeningMode.NoiseCancellation;

        StyleModeButton(transparencyButton, AirPodsListeningMode.Transparency, Color.FromArgb(241, 250, 246));
        StyleModeButton(adaptiveButton, AirPodsListeningMode.Adaptive, Color.FromArgb(241, 247, 252));
        StyleModeButton(noiseCancelButton, AirPodsListeningMode.NoiseCancellation, Color.FromArgb(248, 245, 255));

        if (!canSend && !listeningModeBusy)
        {
            listeningModeStatusLabel.Text = "Connect AirPods in Windows Bluetooth to control mode.";
        }
    }

    private void StyleModeButton(Button button, AirPodsListeningMode mode, Color idleColor)
    {
        var active = currentListeningMode == mode;
        button.BackColor = active ? AccentColor : IsDarkTheme ? SoftBack : idleColor;
        button.ForeColor = active ? AccentText : TextMain;
        PrepareRoundedButton(button);
    }

    private static string DisplayMode(AirPodsListeningMode mode) =>
        mode switch
        {
            AirPodsListeningMode.Transparency => "Transparency",
            AirPodsListeningMode.Adaptive => "Adaptive",
            AirPodsListeningMode.NoiseCancellation => "Noise Cancellation",
            _ => mode.ToString(),
        };

    private static string ModelGeneration(AirPodsReading reading)
    {
        var model = reading.Model.ToLowerInvariant();
        if (model.Contains("pro 2"))
        {
            return "2nd generation";
        }

        if (model.Contains("pro"))
        {
            return "Pro generation";
        }

        if (model.Contains("3"))
        {
            return "3rd generation";
        }

        if (model.Contains("2"))
        {
            return "2nd generation";
        }

        return "Apple audio";
    }

    private static string SignalText(short rssi) =>
        rssi switch
        {
            >= -55 => "Strong",
            >= -70 => "Good",
            >= -85 => "Weak",
            _ => "Poor",
        };

    private static string SeenText(DateTimeOffset seenAt)
    {
        var age = DateTimeOffset.Now - seenAt;
        if (age < TimeSpan.FromSeconds(10))
        {
            return "Just now";
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return $"{(int)age.TotalSeconds}s ago";
        }

        return seenAt.ToString("HH:mm:ss");
    }

    private static bool IsAppleAudioDevice(ConnectedBluetoothDevice device)
    {
        var name = device.Name.ToLowerInvariant();
        return name.Contains("airpods") || name.Contains("air pods") || name.Contains("beats");
    }

    private static bool IsLikelySameDevice(AirPodsReading reading, ConnectedBluetoothDevice device)
    {
        var name = device.Name.ToLowerInvariant();
        var model = reading.Model.ToLowerInvariant();

        if (name.Contains("beats") && model.Contains("beats"))
        {
            return true;
        }

        if (!name.Contains("airpods") && !name.Contains("air pods"))
        {
            return false;
        }

        if (name.Contains("max"))
        {
            return model.Contains("max");
        }

        if (name.Contains("pro"))
        {
            return model.Contains("pro");
        }

        return model.Contains("airpods");
    }

    private IEnumerable<AirPodsReading> SortReadings(IEnumerable<AirPodsReading> readings)
    {
        var sorted = sortColumn switch
        {
            DeviceSortColumn.Name => ApplySort(readings, static r => r.Model),
            DeviceSortColumn.Connected => ApplySort(readings, r => IsReadingConnected(r)),
            DeviceSortColumn.Battery => ApplySort(readings, static r => r.BestBattery ?? -1),
            DeviceSortColumn.Signal => ApplySort(readings, static r => r.Rssi),
            DeviceSortColumn.Seen => ApplySort(readings, static r => r.SeenAt),
            DeviceSortColumn.Address => ApplySort(readings, static r => r.Address),
            _ => readings,
        };

        return sorted.OrderByDescending(IsPinned);
    }

    private IEnumerable<AirPodsReading> ApplySort<TKey>(IEnumerable<AirPodsReading> readings, Func<AirPodsReading, TKey> keySelector)
    {
        return sortOrder == SortOrder.Ascending
            ? readings.OrderBy(keySelector).ThenBy(static r => r.Address)
            : readings.OrderByDescending(keySelector).ThenBy(static r => r.Address);
    }

    private void deviceListView_ColumnClick(object sender, ColumnClickEventArgs e)
    {
        var clicked = (DeviceSortColumn)e.Column;
        if (sortColumn == clicked)
        {
            sortOrder = sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
        }
        else
        {
            sortColumn = clicked;
            sortOrder = clicked is DeviceSortColumn.Name or DeviceSortColumn.Address
                ? SortOrder.Ascending
                : SortOrder.Descending;
        }

        SaveDeviceSort();
        RefreshDeviceList();
    }

    private void RestoreDeviceSort()
    {
        if (Enum.TryParse<DeviceSortColumn>(settings.DeviceSortColumn, out var savedColumn))
        {
            sortColumn = savedColumn;
        }

        if (Enum.TryParse<SortOrder>(settings.DeviceSortOrder, out var savedOrder) &&
            savedOrder is SortOrder.Ascending or SortOrder.Descending)
        {
            sortOrder = savedOrder;
        }
    }

    private void SaveDeviceSort()
    {
        settings.DeviceSortColumn = sortColumn.ToString();
        settings.DeviceSortOrder = sortOrder.ToString();
        settings.Save();
    }

    private void UpdateColumnHeaders()
    {
        deviceNameColumn.Text = HeaderText("Name", DeviceSortColumn.Name);
        deviceConnectedColumn.Text = HeaderText("Windows", DeviceSortColumn.Connected);
        deviceBatteryColumn.Text = HeaderText("Battery", DeviceSortColumn.Battery);
        deviceSignalColumn.Text = HeaderText("Signal", DeviceSortColumn.Signal);
        deviceSeenColumn.Text = HeaderText("Seen", DeviceSortColumn.Seen);
        deviceAddressColumn.Text = HeaderText("Address", DeviceSortColumn.Address);
    }

    private string HeaderText(string text, DeviceSortColumn column)
    {
        if (sortColumn != column)
        {
            return text;
        }

        return sortOrder == SortOrder.Ascending ? $"{text} ^" : $"{text} v";
    }

    private enum DeviceSortColumn
    {
        Name = 0,
        Connected = 1,
        Battery = 2,
        Signal = 3,
        Seen = 4,
        Address = 5,
    }

    private static void SetRedraw(Control control, bool enabled)
    {
        if (control.IsHandleCreated)
        {
            SendMessage(control.Handle, 0x000B, enabled ? 1 : 0, 0);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, int wParam, int lParam);
}
