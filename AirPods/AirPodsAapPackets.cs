namespace WinPods;

public static class AirPodsAapPackets
{
    public static readonly byte[] Handshake =
    [
        0x00, 0x00, 0x04, 0x00, 0x01, 0x00, 0x02, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    public static readonly byte[] SetFeatureFlags =
    [
        0x04, 0x00, 0x04, 0x00, 0x4d, 0x00, 0xd7, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    public static readonly byte[] RequestNotifications =
    [
        0x04, 0x00, 0x04, 0x00, 0x0f, 0x00, 0xff, 0xff, 0xff, 0xff,
    ];

    public static byte[] ListeningMode(AirPodsListeningMode mode) =>
    [
        0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x0d, (byte)mode, 0x00, 0x00, 0x00,
    ];

    public static AirPodsListeningMode? TryParseListeningMode(byte[] packet)
    {
        for (var i = 0; i <= packet.Length - 8; i++)
        {
            if (packet[i] == 0x04 &&
                packet[i + 1] == 0x00 &&
                packet[i + 2] == 0x04 &&
                packet[i + 3] == 0x00 &&
                packet[i + 4] == 0x09 &&
                packet[i + 5] == 0x00 &&
                packet[i + 6] == 0x0d &&
                Enum.IsDefined(typeof(AirPodsListeningMode), packet[i + 7]))
            {
                return (AirPodsListeningMode)packet[i + 7];
            }
        }

        return null;
    }

    public static AirPodsBatterySnapshot? TryParseBattery(byte[] packet)
    {
        for (var i = 0; i <= packet.Length - 7; i++)
        {
            if (packet[i] != 0x04 ||
                packet[i + 1] != 0x00 ||
                packet[i + 2] != 0x04 ||
                packet[i + 3] != 0x00 ||
                packet[i + 4] != 0x04 ||
                packet[i + 5] != 0x00)
            {
                continue;
            }

            var count = packet[i + 6];
            var offset = i + 7;
            int? left = null;
            int? right = null;
            int? caseBattery = null;
            bool? leftCharging = null;
            bool? rightCharging = null;
            bool? caseCharging = null;

            for (var part = 0; part < count && offset + 4 < packet.Length; part++, offset += 5)
            {
                var component = packet[offset];
                var level = Math.Clamp(packet[offset + 2], (byte)0, (byte)100);
                var charging = ParseCharging(packet[offset + 3]);

                switch (component)
                {
                    case 0x04:
                        left = level;
                        leftCharging = charging;
                        break;
                    case 0x02:
                        right = level;
                        rightCharging = charging;
                        break;
                    case 0x08:
                        caseBattery = level;
                        caseCharging = charging;
                        break;
                }
            }

            if (left is not null || right is not null || caseBattery is not null)
            {
                return new AirPodsBatterySnapshot(left, right, caseBattery, leftCharging, rightCharging, caseCharging);
            }
        }

        return null;
    }

    private static bool? ParseCharging(byte status) =>
        status switch
        {
            0x01 => true,
            0x02 => false,
            0x04 => false,
            _ => null,
        };
}
