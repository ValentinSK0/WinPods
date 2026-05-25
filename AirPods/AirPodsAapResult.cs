namespace WinPods;

public sealed record AirPodsAapResult(
    AirPodsListeningMode RequestedMode,
    AirPodsListeningMode? ConfirmedMode,
    byte[]? ResponsePacket);
