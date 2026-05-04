namespace WinPods;

public partial class Form1 : Form
{
    private readonly BluetoothAirPodsScanner scanner = new();
    private readonly Dictionary<string, AirPodsReading> readingsByAddress = new(StringComparer.OrdinalIgnoreCase);
    private AirPodsReading? latestReading;
    private string? selectedAddress;
    private bool exiting;
    private bool scannerStarted;

    public Form1()
    {
        InitializeComponent();
        scanner.ReadingReceived += ScannerOnReadingReceived;
        scanner.ScannerStatusChanged += ScannerOnScannerStatusChanged;
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
            var item = new ListViewItem(reading.Model)
            {
                Tag = reading.Address,
            };

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
        deviceHintLabel.Text = count == 0
            ? "Open AirPods case near PC."
            : $"{count} AirPods/Beats device(s) found. Select yours from left list.";
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

}
