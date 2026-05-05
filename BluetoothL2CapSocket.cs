using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WinPods;

public sealed class BluetoothL2CapSocket : IDisposable
{
    private static readonly Guid AirPodsAapServiceId = Guid.Parse("74ec2172-0bad-4d01-8f77-997b2be0722a");
    private const int AfBth = 32;
    private const int SockStream = 1;
    private const int SockSeqPacket = 5;
    private const int BthProtoL2Cap = 256;
    private const int SolSocket = 0xffff;
    private const int SoSendTimeout = 0x1005;
    private const int SoReceiveTimeout = 0x1006;
    private const uint BtPortAny = 0xffffffff;
    private static readonly object WinsockLock = new();
    private static bool winsockStarted;

    private nint socketHandle;

    private BluetoothL2CapSocket(nint socketHandle)
    {
        this.socketHandle = socketHandle;
    }

    public static async Task<BluetoothL2CapSocket> ConnectAsync(string address, int psm, TimeSpan timeout)
    {
        EnsureWinsockStarted();

        var addresses = new[]
        {
            new AddressAttempt("normal", ParseAddress(address)),
            new AddressAttempt("reversed", ReverseAddress(ParseAddress(address))),
        }.DistinctBy(static attempt => attempt.Address).ToArray();

        var endpoints = new[]
        {
            new EndpointAttempt("AAP UUID only", AirPodsAapServiceId, 0),
            new EndpointAttempt("AAP UUID + BT_PORT_ANY", AirPodsAapServiceId, BtPortAny),
            new EndpointAttempt("PSM only", Guid.Empty, (uint)psm),
        };

        var failures = new List<string>();

        foreach (var addressAttempt in addresses)
        {
            foreach (var endpointAttempt in endpoints)
            {
                foreach (var socketType in new[] { SockStream, SockSeqPacket })
                {
                    var socketTypeName = socketType == SockStream ? "STREAM" : "SEQPACKET";
                    var handle = socket(AfBth, socketType, BthProtoL2Cap);
                    if (handle == new nint(-1))
                    {
                        var error = WSAGetLastError();
                        failures.Add($"{addressAttempt.Name}/{endpointAttempt.Name}/{socketTypeName}: socket failed WSA {error} ({new Win32Exception(error).Message})");
                        continue;
                    }

                    try
                    {
                        SetTimeout(handle, SoSendTimeout, timeout);
                        SetTimeout(handle, SoReceiveTimeout, timeout);
                        await ConnectHandleAsync(handle, addressAttempt.Address, endpointAttempt.ServiceClassId, endpointAttempt.Port, timeout);
                        return new BluetoothL2CapSocket(handle);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{addressAttempt.Name}/{endpointAttempt.Name}/{socketTypeName}: {ex.Message}");
                        closesocket(handle);
                    }
                }
            }
        }

        var failureText = string.Join(Environment.NewLine + " - ", failures);
        if (LooksLikeWindowsBluetoothStackBlock(failures))
        {
            throw new InvalidOperationException(
                "Bluetooth L2CAP connect failed. Windows refused AirPods AAP L2CAP from this app. " +
                "If AirPods are connected and this still happens, Windows likely needs an AAP driver such as MagicAAP for noise-control commands. " +
                Environment.NewLine + "Tried:" + Environment.NewLine + " - " + failureText);
        }

        throw new InvalidOperationException(
            "Bluetooth L2CAP connect failed." +
            Environment.NewLine + "Tried:" + Environment.NewLine + " - " + failureText);
    }

    public void Send(byte[] packet)
    {
        ObjectDisposedException.ThrowIf(socketHandle == 0, this);

        var sent = send(socketHandle, packet, packet.Length, 0);
        if (sent == -1)
        {
            throw new Win32Exception(WSAGetLastError(), "Bluetooth L2CAP send failed");
        }

        if (sent != packet.Length)
        {
            throw new IOException($"Bluetooth L2CAP send incomplete: {sent}/{packet.Length} bytes.");
        }
    }

    public byte[]? TryReceive()
    {
        ObjectDisposedException.ThrowIf(socketHandle == 0, this);

        var buffer = new byte[1024];
        var read = recv(socketHandle, buffer, buffer.Length, 0);
        if (read <= 0)
        {
            return null;
        }

        return buffer[..read];
    }

    public void Dispose()
    {
        if (socketHandle == 0)
        {
            return;
        }

        closesocket(socketHandle);
        socketHandle = 0;
    }

    private static void EnsureWinsockStarted()
    {
        lock (WinsockLock)
        {
            if (winsockStarted)
            {
                return;
            }

            var result = WSAStartup(0x0202, out _);
            if (result != 0)
            {
                throw new Win32Exception(result, "WSAStartup failed");
            }

            winsockStarted = true;
        }
    }

    private static async Task ConnectHandleAsync(nint handle, ulong btAddress, Guid serviceClassId, uint port, TimeSpan timeout)
    {
        var endpoint = new SockAddrBth
        {
            addressFamily = (ushort)AfBth,
            btAddr = btAddress,
            serviceClassId = serviceClassId,
            port = port,
        };

        var connectTask = Task.Run(() =>
        {
            var result = connect(handle, ref endpoint, Marshal.SizeOf<SockAddrBth>());
            if (result == -1)
            {
                var error = WSAGetLastError();
                throw new Win32Exception(error, $"connect failed WSA {error} ({new Win32Exception(error).Message})");
            }
        });

        var completed = await Task.WhenAny(connectTask, Task.Delay(timeout));
        if (completed != connectTask)
        {
            closesocket(handle);
            throw new TimeoutException($"Bluetooth L2CAP connect timed out after {timeout.TotalSeconds:0.#}s.");
        }

        await connectTask;
    }

    private static void SetTimeout(nint handle, int option, TimeSpan timeout)
    {
        var value = Math.Max(1, (int)timeout.TotalMilliseconds);
        setsockopt(handle, SolSocket, option, ref value, sizeof(int));
    }

    private static ulong ParseAddress(string address)
    {
        var hex = address.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (hex.Length != 12 || !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"Invalid Bluetooth address: {address}");
        }

        return value;
    }

    private static ulong ReverseAddress(ulong address)
    {
        var reversed = 0UL;
        for (var i = 0; i < 6; i++)
        {
            reversed <<= 8;
            reversed |= address & 0xff;
            address >>= 8;
        }

        return reversed;
    }

    private static bool LooksLikeWindowsBluetoothStackBlock(IEnumerable<string> failures)
    {
        var items = failures.ToArray();
        return items.Any(static failure => failure.Contains("WSA 10050", StringComparison.Ordinal)) &&
            items.Any(static failure => failure.Contains("WSA 10044", StringComparison.Ordinal));
    }

    private sealed record AddressAttempt(string Name, ulong Address);

    private sealed record EndpointAttempt(string Name, Guid ServiceClassId, uint Port);

    [StructLayout(LayoutKind.Sequential)]
    private struct SockAddrBth
    {
        public ushort addressFamily;
        public ulong btAddr;
        public Guid serviceClassId;
        public uint port;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct WsaData
    {
        public ushort wVersion;
        public ushort wHighVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        public string szDescription;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
        public string szSystemStatus;
        public ushort iMaxSockets;
        public ushort iMaxUdpDg;
        public nint lpVendorInfo;
    }

    [DllImport("ws2_32.dll")]
    private static extern int WSAStartup(ushort wVersionRequested, out WsaData lpWsaData);

    [DllImport("ws2_32.dll")]
    private static extern int WSAGetLastError();

    [DllImport("ws2_32.dll")]
    private static extern nint socket(int af, int type, int protocol);

    [DllImport("ws2_32.dll")]
    private static extern int connect(nint s, ref SockAddrBth name, int namelen);

    [DllImport("ws2_32.dll")]
    private static extern int send(nint s, byte[] buf, int len, int flags);

    [DllImport("ws2_32.dll")]
    private static extern int recv(nint s, byte[] buf, int len, int flags);

    [DllImport("ws2_32.dll")]
    private static extern int setsockopt(nint s, int level, int optname, ref int optval, int optlen);

    [DllImport("ws2_32.dll")]
    private static extern int closesocket(nint s);
}
