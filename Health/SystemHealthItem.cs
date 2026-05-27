namespace WinPods;

public sealed record SystemHealthItem(
    string Name,
    SystemHealthStatus Status,
    string Summary,
    string Detail);
