using Microsoft.Win32;
using System.Diagnostics;

namespace WinPods;

public sealed class SystemHealthChecker
{
    private const string AapDeviceInterfaceGuid = "{74ec2172-0bad-4d01-8f77-997b2be0722a}";

    private readonly AudioEndpointInspector audioEndpointInspector = new();

    public async Task<SystemHealthSnapshot> CheckAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<SystemHealthItem>
        {
            CheckMagicAapDriver(),
            await CheckBluetoothAsync(cancellationToken),
            CheckAudioEndpoints(),
            CheckSettingsStorage(),
        };

        return new SystemHealthSnapshot(DateTimeOffset.Now, items);
    }

    private static SystemHealthItem CheckMagicAapDriver()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Control\DeviceClasses\{AapDeviceInterfaceGuid}");
            var driverPackageInstalled = IsMagicAapDriverPackageInstalled();

            if (key is null && !driverPackageInstalled)
            {
                return new SystemHealthItem(
                    "MagicAAP driver",
                    SystemHealthStatus.Error,
                    "Missing",
                    "MagicAAP is required for exact battery values and AirPods listening mode control.");
            }

            var deviceCount = key?.GetSubKeyNames().Length ?? 0;
            return deviceCount > 0
                ? new SystemHealthItem(
                    "MagicAAP driver",
                    SystemHealthStatus.Ok,
                    "Installed",
                    $"MagicAAP device interface is registered ({deviceCount} device interface entr{(deviceCount == 1 ? "y" : "ies")}).")
                : new SystemHealthItem(
                    "MagicAAP driver",
                    SystemHealthStatus.Warning,
                    "Installed, AirPods not attached",
                    "MagicAAP driver package is installed, but no AirPods device interface is active. Reconnect AirPods if controls fail.");
        }
        catch (Exception ex)
        {
            return new SystemHealthItem(
                "MagicAAP driver",
                SystemHealthStatus.Warning,
                "Cannot verify",
                $"Driver registry check failed: {ex.Message}");
        }
    }

    private static bool IsMagicAapDriverPackageInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = "/enum-drivers",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output.Contains("magicaap", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Maslov", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("MagicAAP", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<SystemHealthItem> CheckBluetoothAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var devices = await ConnectedBluetoothProvider.GetConnectedDevicesAsync();
            var appleDevices = devices
                .Where(static device => AudioEndpointClassifier.IsAppleAudio(device.Name))
                .ToList();

            if (appleDevices.Count > 0)
            {
                return new SystemHealthItem(
                    "Bluetooth",
                    SystemHealthStatus.Ok,
                    "AirPods connected",
                    $"Connected Apple audio device: {appleDevices[0].Name} ({appleDevices[0].Address}).");
            }

            return devices.Count > 0
                ? new SystemHealthItem(
                    "Bluetooth",
                    SystemHealthStatus.Warning,
                    "No AirPods connected",
                    $"Bluetooth is responding. Connected devices: {devices.Count}. Connect AirPods for control and exact battery.")
                : new SystemHealthItem(
                    "Bluetooth",
                    SystemHealthStatus.Warning,
                    "No connected devices",
                    "Bluetooth is responding, but no connected devices were found.");
        }
        catch (Exception ex)
        {
            return new SystemHealthItem(
                "Bluetooth",
                SystemHealthStatus.Error,
                "Cannot query Bluetooth",
                $"Windows Bluetooth device query failed: {ex.Message}");
        }
    }

    private SystemHealthItem CheckAudioEndpoints()
    {
        try
        {
            var inventory = audioEndpointInspector.Inspect();
            var hasRender = inventory.RenderEndpoints.Count > 0;
            var hasCapture = inventory.CaptureEndpoints.Count > 0;
            var hasAppleStereo = inventory.RenderEndpoints.Any(static endpoint => endpoint.IsStereoOutput);
            var hasSafeMic = inventory.CaptureEndpoints.Any(static endpoint => endpoint.IsSafeMicrophone);

            if (hasRender && hasCapture && hasAppleStereo && hasSafeMic)
            {
                return new SystemHealthItem(
                    "Audio route",
                    SystemHealthStatus.Ok,
                    "Ready",
                    "AirPods stereo output and a safe non-AirPods microphone are available.");
            }

            if (!hasRender || !hasCapture)
            {
                return new SystemHealthItem(
                    "Audio route",
                    SystemHealthStatus.Error,
                    "Missing endpoint",
                    "Windows did not report both playback and recording endpoints.");
            }

            return new SystemHealthItem(
                "Audio route",
                SystemHealthStatus.Warning,
                "Limited",
                "Call Quality Guard works best with AirPods stereo output and a non-AirPods microphone.");
        }
        catch (Exception ex)
        {
            return new SystemHealthItem(
                "Audio route",
                SystemHealthStatus.Warning,
                "Cannot inspect",
                $"Windows audio endpoint inspection failed: {ex.Message}");
        }
    }

    private static SystemHealthItem CheckSettingsStorage()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinPods",
                ".health-write-test");
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, DateTimeOffset.Now.ToString("O"));
            File.Delete(path);

            return new SystemHealthItem(
                "Local settings",
                SystemHealthStatus.Ok,
                "Writable",
                "WinPods can save theme, layout, pinned AirPods, and guard settings.");
        }
        catch (Exception ex)
        {
            return new SystemHealthItem(
                "Local settings",
                SystemHealthStatus.Error,
                "Cannot write",
                $"Settings folder is not writable: {ex.Message}");
        }
    }
}
