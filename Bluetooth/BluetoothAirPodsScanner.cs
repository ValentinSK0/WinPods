using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace WinPods;

public sealed class BluetoothAirPodsScanner : IDisposable
{
    private readonly BluetoothLEAdvertisementWatcher watcher = new()
    {
        ScanningMode = BluetoothLEScanningMode.Active,
    };

    public event EventHandler<AirPodsReading>? ReadingReceived;
    public event EventHandler<string>? ScannerStatusChanged;

    public bool IsRunning => watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started;

    public BluetoothAirPodsScanner()
    {
        watcher.Received += OnReceived;
        watcher.Stopped += OnStopped;
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
            watcher.Start();
            ScannerStatusChanged?.Invoke(this, "Scanning");
        }
        catch (Exception ex)
        {
            ScannerStatusChanged?.Invoke(this, $"Scan failed: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Created)
        {
            watcher.Stop();
        }
    }

    public void Dispose()
    {
        watcher.Received -= OnReceived;
        watcher.Stopped -= OnStopped;
        Stop();
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        ScannerStatusChanged?.Invoke(this, args.Error == BluetoothError.Success ? "Stopped" : $"Stopped: {args.Error}");
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var data in args.Advertisement.ManufacturerData)
        {
            var payload = ReadBytes(data.Data);
            var reading = AirPodsBatteryDecoder.TryDecode(data.CompanyId, payload, args.BluetoothAddress, args.RawSignalStrengthInDBm, DateTimeOffset.Now);
            if (reading is not null)
            {
                ReadingReceived?.Invoke(this, reading);
            }
        }
    }

    private static byte[] ReadBytes(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
