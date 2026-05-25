using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WinPods;

public sealed class MagicAapDriverConnection : IDisposable
{
    private const string AapDeviceInterfaceGuid = "{74ec2172-0bad-4d01-8f77-997b2be0722a}";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;

    private readonly SafeFileHandle handle;
    private readonly FileStream stream;

    private MagicAapDriverConnection(string devicePath, SafeFileHandle handle)
    {
        DevicePath = devicePath;
        this.handle = handle;
        stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 1024, isAsync: true);
    }

    public string DevicePath { get; }

    public static MagicAapDriverConnection Open(string bluetoothAddress)
    {
        var normalizedAddress = NormalizeAddress(bluetoothAddress);
        var devicePath = FindDevicePath(normalizedAddress)
            ?? throw new InvalidOperationException(
                "MagicAAP driver device path not found for this AirPods address. Reconnect AirPods or reinstall MagicAAP driver.");

        var handle = CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            nint.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            nint.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"MagicAAP CreateFile failed for {devicePath}");
        }

        return new MagicAapDriverConnection(devicePath, handle);
    }

    public async Task SendAsync(byte[] packet, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(packet, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task<byte[]?> TryReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var buffer = new byte[1024];
        try
        {
            var read = await stream.ReadAsync(buffer, timeoutCts.Token);
            return read <= 0 ? null : buffer[..read];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Dispose()
    {
        stream.Dispose();
        handle.Dispose();
    }

    private static string? FindDevicePath(string normalizedAddress)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Control\DeviceClasses\{AapDeviceInterfaceGuid}");
        if (key is null)
        {
            return null;
        }

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName);
            var deviceInstance = subKey?.GetValue("DeviceInstance") as string;
            if (deviceInstance is null ||
                !NormalizeAddress(deviceInstance).Contains(normalizedAddress, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return ToDevicePath(subKeyName);
        }

        return null;
    }

    private static string ToDevicePath(string registryDevicePath)
    {
        return registryDevicePath.StartsWith("##?#", StringComparison.Ordinal)
            ? @"\\?\" + registryDevicePath[4..]
            : registryDevicePath;
    }

    private static string NormalizeAddress(string value)
    {
        return new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);
}
