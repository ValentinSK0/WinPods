namespace WinPods;

public sealed class AirPodsAapClient
{
    private const int AirPodsAapPsm = 0x1001;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PacketDelay = TimeSpan.FromMilliseconds(120);

    public async Task<AirPodsAapResult> SetListeningModeAsync(string bluetoothAddress, AirPodsListeningMode mode)
    {
        Exception? magicAapFailure = null;
        try
        {
            using var driver = MagicAapDriverConnection.Open(bluetoothAddress);
            return await SetListeningModeWithMagicAapAsync(driver, mode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            magicAapFailure = ex;
        }

        try
        {
            using var socket = await BluetoothL2CapSocket.ConnectAsync(bluetoothAddress, AirPodsAapPsm, ConnectTimeout);

            socket.Send(AirPodsAapPackets.Handshake);
            await Task.Delay(PacketDelay);
            socket.Send(AirPodsAapPackets.SetFeatureFlags);
            await Task.Delay(PacketDelay);
            socket.Send(AirPodsAapPackets.RequestNotifications);
            await Task.Delay(PacketDelay);

            socket.Send(AirPodsAapPackets.ListeningMode(mode));

            var response = await ReadListeningModeResponseAsync(socket, mode);
            return new AirPodsAapResult(mode, response is null ? null : AirPodsAapPackets.TryParseListeningMode(response), response);
        }
        catch (Exception socketFailure)
        {
            throw new InvalidOperationException(
                "AirPods mode switch failed via MagicAAP driver and Windows L2CAP fallback." +
                Environment.NewLine + Environment.NewLine +
                "MagicAAP driver error:" + Environment.NewLine + magicAapFailure +
                Environment.NewLine + Environment.NewLine +
                "Windows L2CAP error:" + Environment.NewLine + socketFailure,
                socketFailure);
        }
    }

    public async Task<AirPodsBatterySnapshot?> GetBatteryAsync(string bluetoothAddress)
    {
        using var driver = MagicAapDriverConnection.Open(bluetoothAddress);

        await driver.SendAsync(AirPodsAapPackets.Handshake, CancellationToken.None);
        await Task.Delay(PacketDelay);
        await driver.SendAsync(AirPodsAapPackets.SetFeatureFlags, CancellationToken.None);
        await Task.Delay(PacketDelay);
        await driver.SendAsync(AirPodsAapPackets.RequestNotifications, CancellationToken.None);

        var endAt = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < endAt)
        {
            var packet = await driver.TryReceiveAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);
            if (packet is null)
            {
                continue;
            }

            var battery = AirPodsAapPackets.TryParseBattery(packet);
            if (battery is not null)
            {
                return battery;
            }
        }

        return null;
    }

    private static async Task<AirPodsAapResult> SetListeningModeWithMagicAapAsync(
        MagicAapDriverConnection driver,
        AirPodsListeningMode mode,
        CancellationToken cancellationToken)
    {
        await driver.SendAsync(AirPodsAapPackets.Handshake, cancellationToken);
        await Task.Delay(PacketDelay, cancellationToken);
        await driver.SendAsync(AirPodsAapPackets.SetFeatureFlags, cancellationToken);
        await Task.Delay(PacketDelay, cancellationToken);
        await driver.SendAsync(AirPodsAapPackets.RequestNotifications, cancellationToken);
        await Task.Delay(PacketDelay, cancellationToken);

        await driver.SendAsync(AirPodsAapPackets.ListeningMode(mode), cancellationToken);

        var response = await ReadListeningModeResponseAsync(driver, mode, cancellationToken);
        return new AirPodsAapResult(mode, response is null ? null : AirPodsAapPackets.TryParseListeningMode(response), response);
    }

    private static async Task<byte[]?> ReadListeningModeResponseAsync(BluetoothL2CapSocket socket, AirPodsListeningMode requestedMode)
    {
        var endAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < endAt)
        {
            var packet = await Task.Run(socket.TryReceive);
            if (packet is null)
            {
                return null;
            }

            var parsedMode = AirPodsAapPackets.TryParseListeningMode(packet);
            if (parsedMode == requestedMode)
            {
                return packet;
            }

        }

        return null;
    }

    private static async Task<byte[]?> ReadListeningModeResponseAsync(
        MagicAapDriverConnection driver,
        AirPodsListeningMode requestedMode,
        CancellationToken cancellationToken)
    {
        var endAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < endAt)
        {
            var packet = await driver.TryReceiveAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            if (packet is null)
            {
                continue;
            }

            var parsedMode = AirPodsAapPackets.TryParseListeningMode(packet);
            if (parsedMode == requestedMode)
            {
                return packet;
            }

        }

        return null;
    }
}
