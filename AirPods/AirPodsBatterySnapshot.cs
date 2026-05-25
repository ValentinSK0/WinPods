namespace WinPods;

public sealed record AirPodsBatterySnapshot(
    int? LeftBattery,
    int? RightBattery,
    int? CaseBattery,
    bool? LeftCharging,
    bool? RightCharging,
    bool? CaseCharging);
