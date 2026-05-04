using System.Globalization;

namespace WinPods;

public static class AirPodsBatteryDecoder
{
    private static readonly Dictionary<string, string> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0220"] = "AirPods 1",
        ["0f20"] = "AirPods 2",
        ["1320"] = "AirPods 3",
        ["0e20"] = "AirPods Pro",
        ["1420"] = "AirPods Pro 2 Lightning",
        ["2420"] = "AirPods Pro 2 USB-C",
        ["0a20"] = "AirPods Max",
        ["0320"] = "Powerbeats 3",
        ["0520"] = "BeatsX",
        ["0620"] = "Beats Solo 3",
    };

    private static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["00"] = "white",
        ["01"] = "black",
        ["02"] = "red",
        ["03"] = "blue",
        ["04"] = "pink",
        ["05"] = "gray",
        ["06"] = "silver",
        ["07"] = "gold",
        ["08"] = "rose gold",
        ["09"] = "space gray",
        ["0a"] = "dark blue",
        ["0b"] = "light blue",
        ["0c"] = "yellow",
        ["11"] = "green",
    };

    public static AirPodsReading? TryDecode(ushort companyId, byte[] payload, ulong bluetoothAddress, short rssi, DateTimeOffset seenAt)
    {
        if (companyId != 0x004c || payload.Length < 27)
        {
            return null;
        }

        var bytes = new byte[payload.Length + 2];
        bytes[0] = 0x4c;
        bytes[1] = 0x00;
        Array.Copy(payload, 0, bytes, 2, payload.Length);

        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        if (hex.Length != 58 || !hex.StartsWith("4c00071901", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var version = Slice(hex, 10, 4);
        var colorCode = Slice(hex, 22, 2);
        var swapped = BitAt(hex, 14, 1);
        var isMax = version.Equals("0a20", StringComparison.OrdinalIgnoreCase);

        int? right = swapped ? BatteryAt(hex, 16) : isMax ? null : BatteryAt(hex, 17);
        int? left = swapped ? isMax ? null : BatteryAt(hex, 17) : BatteryAt(hex, 16);
        int? caseBattery = isMax ? null : BatteryAt(hex, 19);

        bool? rightCharging = swapped ? BitAt(hex, 18, 1) : isMax ? null : BitAt(hex, 18, 0);
        bool? leftCharging = swapped ? isMax ? null : BitAt(hex, 18, 0) : BitAt(hex, 18, 1);
        bool? caseCharging = isMax ? null : BitAt(hex, 18, 2);

        return new AirPodsReading(
            FormatAddress(bluetoothAddress),
            rssi,
            Models.GetValueOrDefault(version, $"Apple/Beats {version}"),
            Colors.GetValueOrDefault(colorCode, $"color {colorCode}"),
            left,
            right,
            caseBattery,
            leftCharging,
            rightCharging,
            caseCharging,
            seenAt,
            hex);
    }

    private static int? BatteryAt(string hex, int index)
    {
        var value = NibbleAt(hex, index);
        return value is >= 0 and <= 10 ? Math.Min(value * 10, 100) : null;
    }

    private static bool BitAt(string hex, int index, int bit) => (NibbleAt(hex, index) & (1 << bit)) != 0;

    private static int NibbleAt(string hex, int index) =>
        index >= 0 && index < hex.Length
            ? int.Parse(hex.AsSpan(index, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : 0;

    private static string Slice(string hex, int start, int length) =>
        start + length <= hex.Length ? hex.Substring(start, length) : string.Empty;

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
