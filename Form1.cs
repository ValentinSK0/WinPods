namespace WinPods;

public partial class Form1 : Form
{
    private readonly BluetoothAirPodsScanner scanner = new();
    private readonly AirPodsAapClient aapClient = new();
    private readonly Dictionary<string, AirPodsReading> readingsByAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer connectedRefreshTimer = new();
    private IReadOnlyList<ConnectedBluetoothDevice> connectedBluetoothDevices = Array.Empty<ConnectedBluetoothDevice>();
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
    private DateTimeOffset lastExactBatteryRefresh = DateTimeOffset.MinValue;

    public Form1()
    {
        InitializeComponent();
        scanner.ReadingReceived += ScannerOnReadingReceived;
        scanner.ScannerStatusChanged += ScannerOnScannerStatusChanged;
        connectedRefreshTimer.Interval = 5000;
        connectedRefreshTimer.Tick += connectedRefreshTimer_Tick;
        UpdateReading(null);
        UpdateListeningModeUi();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        WindowState = FormWindowState.Normal;
        Activate();

        if (!scannerStarted)
        {
            scannerStarted = true;
            scanner.Start();
            connectedRefreshTimer.Start();
            _ = RefreshConnectedDevicesAsync();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            notifyIcon.ShowBalloonTip(1800, "WinPods", "Bezim v tray. Dvojklik otvori okno.", ToolTipIcon.Info);
            return;
        }

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
            statusValueLabel.Text = e;
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
                selectedAddress = reading.Address;
                SelectDeviceInList(reading.Address);
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

        UpdateListeningModeUi();
        UpdateTray();
        MaybeRefreshExactBattery();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitBatteryLabels();
    }

    private void mainSplit_SplitterMoved(object sender, SplitterEventArgs e)
    {
        FitBatteryLabels();
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
        deviceListView.BeginUpdate();
        deviceListView.Items.Clear();

        var displayReadings = GetDisplayReadings();
        foreach (var reading in SortReadings(displayReadings))
        {
            var isConnected = IsReadingConnected(reading);
            var item = new ListViewItem(reading.Model)
            {
                Tag = reading.Address,
                ForeColor = isConnected ? Color.FromArgb(10, 92, 190) : Color.FromArgb(28, 34, 42),
                Font = isConnected ? new Font(deviceListView.Font, FontStyle.Bold) : deviceListView.Font,
            };

            item.SubItems.Add(isConnected ? "BT connected" : "");
            item.SubItems.Add(reading.Summary);
            item.SubItems.Add($"{reading.Rssi} dBm");
            item.SubItems.Add(reading.SeenAt.ToString("HH:mm:ss"));
            item.SubItems.Add(reading.Address);
            deviceListView.Items.Add(item);
        }

        UpdateColumnHeaders();
        deviceListView.EndUpdate();

        if (selected is not null)
        {
            SelectDeviceInList(selected);
        }
        else if (deviceListView.Items.Count > 0)
        {
            deviceListView.Items[0].Selected = true;
        }

        UpdateDeviceHint();
    }

    private void SelectDeviceInList(string address)
    {
        foreach (ListViewItem item in deviceListView.Items)
        {
            if (item.Tag is string itemAddress &&
                itemAddress.Equals(address, StringComparison.OrdinalIgnoreCase))
            {
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
                return;
            }
        }
    }

    private void UpdateDeviceHint()
    {
        var count = GetDisplayReadings().Count;
        var connected = connectedBluetoothDevices.Count(IsAppleAudioDevice);
        deviceHintLabel.Text = count == 0
            ? "Open AirPods case near PC."
            : $"{count} AirPods/Beats device(s) found. {connected} Windows BT connected.";
    }

    private void UpdateTray()
    {
        var oldIcon = notifyIcon.Icon;
        notifyIcon.Text = latestReading is null
            ? "WinPods - cakam na AirPods"
            : $"WinPods - {latestReading.Summary}";
        notifyIcon.Icon = TrayIconFactory.Create(latestReading is not null);
        oldIcon?.Dispose();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void refreshMenuItem_Click(object sender, EventArgs e)
        => RefreshScan();

    private void refreshButton_Click(object sender, EventArgs e)
        => RefreshScan();

    private void RefreshScan()
    {
        statusValueLabel.Text = "Refreshing";
        scanner.Stop();
        scanner.Start();
        _ = RefreshConnectedDevicesAsync();
    }

    private void deviceListView_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (deviceListView.SelectedItems.Count == 0 ||
            deviceListView.SelectedItems[0].Tag is not string address ||
            !readingsByAddress.TryGetValue(address, out var reading))
        {
            return;
        }

        selectedAddress = address;
        UpdateReading(reading);
        UpdateListeningModeUi();
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

        return groups.Select(MergeReadings).ToList();
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
        button.BackColor = active ? Color.FromArgb(10, 92, 190) : idleColor;
        button.ForeColor = active ? Color.White : Color.FromArgb(28, 34, 42);
        button.FlatAppearance.BorderColor = active ? Color.FromArgb(10, 92, 190) : Color.FromArgb(205, 213, 224);
    }

    private static string DisplayMode(AirPodsListeningMode mode) =>
        mode switch
        {
            AirPodsListeningMode.Transparency => "Transparency",
            AirPodsListeningMode.Adaptive => "Adaptive",
            AirPodsListeningMode.NoiseCancellation => "Noise Cancellation",
            _ => mode.ToString(),
        };

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
        return sortColumn switch
        {
            DeviceSortColumn.Name => ApplySort(readings, static r => r.Model),
            DeviceSortColumn.Connected => ApplySort(readings, r => IsReadingConnected(r)),
            DeviceSortColumn.Battery => ApplySort(readings, static r => r.BestBattery ?? -1),
            DeviceSortColumn.Signal => ApplySort(readings, static r => r.Rssi),
            DeviceSortColumn.Seen => ApplySort(readings, static r => r.SeenAt),
            DeviceSortColumn.Address => ApplySort(readings, static r => r.Address),
            _ => readings,
        };
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

        RefreshDeviceList();
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
}
