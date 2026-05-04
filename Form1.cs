namespace WinPods;

public partial class Form1 : Form
{
    private readonly BluetoothAirPodsScanner scanner = new();
    private readonly Dictionary<string, AirPodsReading> readingsByAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer connectedRefreshTimer = new();
    private IReadOnlyList<ConnectedBluetoothDevice> connectedBluetoothDevices = Array.Empty<ConnectedBluetoothDevice>();
    private AirPodsReading? latestReading;
    private string? selectedAddress;
    private bool exiting;
    private bool scannerStarted;
    private bool connectedRefreshRunning;

    public Form1()
    {
        InitializeComponent();
        scanner.ReadingReceived += ScannerOnReadingReceived;
        scanner.ScannerStatusChanged += ScannerOnScannerStatusChanged;
        connectedRefreshTimer.Interval = 5000;
        connectedRefreshTimer.Tick += connectedRefreshTimer_Tick;
        UpdateReading(null);
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
            signalValueLabel.Text = $"{latestReading.Rssi} dBm, {latestReading.SeenAt:HH:mm:ss}";
            addressValueLabel.Text = latestReading.Address;
            connectedValueLabel.Text = GetConnectedText(latestReading);
            rawValueBox.Text = latestReading.RawManufacturerData;
        }

        UpdateTray();
    }

    private void RefreshDeviceList()
    {
        var selected = selectedAddress;
        deviceListView.BeginUpdate();
        deviceListView.Items.Clear();

        foreach (var reading in readingsByAddress.Values.OrderByDescending(static r => r.SeenAt))
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
        var count = readingsByAddress.Count;
        var connected = connectedBluetoothDevices.Count(IsAppleAudioDevice);
        deviceHintLabel.Text = count == 0
            ? "Open AirPods case near PC."
            : $"{count} AirPods/Beats device(s) found. {connected} Windows BT connected.";
    }

    private void UpdateTray()
    {
        var battery = latestReading?.BestBattery;
        var oldIcon = notifyIcon.Icon;
        notifyIcon.Text = latestReading is null
            ? "WinPods - cakam na AirPods"
            : $"WinPods - {latestReading.Summary}";
        notifyIcon.Icon = TrayIconFactory.Create(battery, latestReading is not null);
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
}
