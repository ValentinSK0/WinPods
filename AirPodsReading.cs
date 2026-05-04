namespace WinPods;

public sealed record AirPodsReading(
    string Address,
    short Rssi,
    string Model,
    string Color,
    int? LeftBattery,
    int? RightBattery,
    int? CaseBattery,
    bool? LeftCharging,
    bool? RightCharging,
    bool? CaseCharging,
    DateTimeOffset SeenAt,
    string RawManufacturerData)
{
    public int? BestBattery => new[] { LeftBattery, RightBattery, CaseBattery }.Where(static v => v.HasValue).Min();

    public string Summary =>
        $"L {FormatBattery(LeftBattery, LeftCharging)}  R {FormatBattery(RightBattery, RightCharging)}  Case {FormatBattery(CaseBattery, CaseCharging)}";

    public static string FormatBattery(int? battery, bool? charging)
    {
        if (battery is null)
        {
            return "-";
        }

        return charging == true ? $"{battery}% charging" : $"{battery}%";
    }
}
