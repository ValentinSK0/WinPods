using System.Globalization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WinPods;

public static class ConnectedBluetoothProvider
{
    public static async Task<IReadOnlyList<ConnectedBluetoothDevice>> GetConnectedDevicesAsync()
    {
        var selector = BluetoothDevice.GetDeviceSelector();
        var devices = await DeviceInformation.FindAllAsync(selector);
        var connected = new List<ConnectedBluetoothDevice>();

        foreach (var deviceInfo in devices)
        {
            BluetoothDevice? device = null;

            try
            {
                device = await BluetoothDevice.FromIdAsync(deviceInfo.Id);
                if (device?.ConnectionStatus != BluetoothConnectionStatus.Connected)
                {
                    continue;
                }

                connected.Add(new ConnectedBluetoothDevice(
                    string.IsNullOrWhiteSpace(device.Name) ? deviceInfo.Name : device.Name,
                    FormatAddress(device.BluetoothAddress)));
            }
            catch
            {
                // Windows may deny details for some paired devices. Skip those.
            }
            finally
            {
                device?.Dispose();
            }
        }

        return connected;
    }

    private static string FormatAddress(ulong address)
    {
        Span<byte> bytes = stackalloc byte[6];
        for (var i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(address & 0xff);
            address >>= 8;
        }

        return string.Join(":", bytes.ToArray().Select(static b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }
}
