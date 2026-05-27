namespace WinPods;

public sealed record SystemHealthSnapshot(
    DateTimeOffset CheckedAt,
    IReadOnlyList<SystemHealthItem> Items)
{
    public SystemHealthStatus OverallStatus =>
        Items.Any(static item => item.Status == SystemHealthStatus.Error)
            ? SystemHealthStatus.Error
            : Items.Any(static item => item.Status == SystemHealthStatus.Warning)
                ? SystemHealthStatus.Warning
                : SystemHealthStatus.Ok;

    public string Summary
    {
        get
        {
            var errors = Items.Count(static item => item.Status == SystemHealthStatus.Error);
            var warnings = Items.Count(static item => item.Status == SystemHealthStatus.Warning);
            return (errors, warnings) switch
            {
                (0, 0) => "System ready",
                (> 0, _) => $"{errors} issue{Plural(errors)} found",
                _ => $"{warnings} warning{Plural(warnings)} found",
            };
        }
    }

    public static SystemHealthSnapshot Checking() =>
        new(
            DateTimeOffset.Now,
            new[]
            {
                new SystemHealthItem("System health", SystemHealthStatus.Warning, "Checking setup...", "WinPods is checking driver, Bluetooth, audio, and local settings."),
            });

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
