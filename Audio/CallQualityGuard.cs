namespace WinPods;

public sealed class CallQualityGuard
{
    private readonly AudioEndpointInspector inspector = new();
    private readonly AudioEndpointSwitcher switcher = new();

    public CallQualityGuardSnapshot Inspect(bool enabled)
    {
        if (!enabled)
        {
            return CallQualityGuardSnapshot.Disabled();
        }

        try
        {
            return Analyze(inspector.Inspect());
        }
        catch (Exception ex)
        {
            return CallQualityGuardSnapshot.Error(
                "Audio check failed",
                ex.Message,
                null,
                null,
                null,
                null);
        }
    }

    public CallQualityGuardSnapshot ApplyRecommendedRoute()
    {
        var snapshot = Analyze(inspector.Inspect());
        if (!snapshot.CanApplyFix ||
            snapshot.RecommendedRender is null ||
            snapshot.RecommendedCapture is null)
        {
            return snapshot with
            {
                Severity = CallQualitySeverity.Warning,
                Status = "Manual setup needed",
                Detail = "Need active AirPods stereo output and non-AirPods mic."
            };
        }

        switcher.SetDefaultRender(snapshot.RecommendedRender);
        switcher.SetDefaultCapture(snapshot.RecommendedCapture);

        return Analyze(inspector.Inspect()) with
        {
            Status = "Recommended route applied"
        };
    }

    private static CallQualityGuardSnapshot Analyze(AudioEndpointInventory inventory)
    {
        var defaultRender = inventory.DefaultCommunicationRender ?? inventory.DefaultMultimediaRender;
        var defaultCapture = inventory.DefaultCommunicationCapture ?? inventory.DefaultMultimediaCapture;
        var riskyRender = defaultRender is { IsAppleAudio: true, IsHandsFree: true };
        var riskyCapture = defaultCapture is { IsAppleAudio: true, IsHandsFree: true };
        var handsFreeAvailable = inventory.RenderEndpoints.Concat(inventory.CaptureEndpoints)
            .Any(static endpoint => endpoint.IsAppleAudio && endpoint.IsHandsFree);
        var airPodsStereo = SelectBestAirPodsStereo(inventory);
        var safeMic = SelectBestSafeMicrophone(inventory);
        var canApplyFix = airPodsStereo is not null && safeMic is not null && (riskyRender || riskyCapture || handsFreeAvailable);

        if (riskyRender || riskyCapture)
        {
            return new CallQualityGuardSnapshot(
                DateTimeOffset.Now,
                CallQualitySeverity.Danger,
                "Hands-Free mode active",
                BuildRiskDetail(defaultRender, defaultCapture, airPodsStereo, safeMic),
                canApplyFix,
                airPodsStereo,
                safeMic,
                defaultRender,
                defaultCapture);
        }

        if (defaultCapture is { IsAppleAudio: true })
        {
            return new CallQualityGuardSnapshot(
                DateTimeOffset.Now,
                CallQualitySeverity.Warning,
                "AirPods mic selected",
                BuildRiskDetail(defaultRender, defaultCapture, airPodsStereo, safeMic),
                airPodsStereo is not null && safeMic is not null,
                airPodsStereo,
                safeMic,
                defaultRender,
                defaultCapture);
        }

        if (airPodsStereo is not null && safeMic is not null)
        {
            var detail = $"Good route: {airPodsStereo.Name} output + {safeMic.Name} mic.";
            return new CallQualityGuardSnapshot(
                DateTimeOffset.Now,
                CallQualitySeverity.Good,
                "Call audio protected",
                detail,
                false,
                airPodsStereo,
                safeMic,
                defaultRender,
                defaultCapture);
        }

        if (handsFreeAvailable)
        {
            return new CallQualityGuardSnapshot(
                DateTimeOffset.Now,
                CallQualitySeverity.Warning,
                "Hands-Free endpoint available",
                BuildRiskDetail(defaultRender, defaultCapture, airPodsStereo, safeMic),
                canApplyFix,
                airPodsStereo,
                safeMic,
                defaultRender,
                defaultCapture);
        }

        return new CallQualityGuardSnapshot(
            DateTimeOffset.Now,
            CallQualitySeverity.Neutral,
            "No AirPods call risk",
            $"Output: {FormatEndpoint(defaultRender)}. Mic: {FormatEndpoint(defaultCapture)}.",
            false,
            airPodsStereo,
            safeMic,
            defaultRender,
            defaultCapture);
    }

    private static AudioEndpointInfo? SelectBestAirPodsStereo(AudioEndpointInventory inventory)
    {
        return inventory.RenderEndpoints
            .Where(static endpoint => endpoint.IsStereoOutput)
            .OrderByDescending(static endpoint => endpoint.IsDefaultMultimedia)
            .ThenByDescending(static endpoint => endpoint.IsDefaultCommunications)
            .ThenBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static AudioEndpointInfo? SelectBestSafeMicrophone(AudioEndpointInventory inventory)
    {
        return inventory.CaptureEndpoints
            .Where(static endpoint => endpoint.IsSafeMicrophone)
            .OrderByDescending(static endpoint => endpoint.IsDefaultCommunications)
            .ThenByDescending(static endpoint => endpoint.IsDefaultMultimedia)
            .ThenBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string BuildRiskDetail(
        AudioEndpointInfo? currentRender,
        AudioEndpointInfo? currentCapture,
        AudioEndpointInfo? recommendedRender,
        AudioEndpointInfo? recommendedCapture)
    {
        var output = recommendedRender is null
            ? "No AirPods stereo output found"
            : $"Set output to {recommendedRender.Name}";
        var mic = recommendedCapture is null
            ? "No safe non-AirPods mic found"
            : $"set mic to {recommendedCapture.Name}";

        return $"Current output: {FormatEndpoint(currentRender)}. Current mic: {FormatEndpoint(currentCapture)}. Recommended: {output}; {mic}.";
    }

    private static string FormatEndpoint(AudioEndpointInfo? endpoint)
        => endpoint?.Name ?? "none";
}

public enum CallQualitySeverity
{
    Disabled,
    Neutral,
    Good,
    Warning,
    Danger,
    Error,
}

public sealed record CallQualityGuardSnapshot(
    DateTimeOffset CheckedAt,
    CallQualitySeverity Severity,
    string Status,
    string Detail,
    bool CanApplyFix,
    AudioEndpointInfo? RecommendedRender,
    AudioEndpointInfo? RecommendedCapture,
    AudioEndpointInfo? CurrentRender,
    AudioEndpointInfo? CurrentCapture)
{
    public bool ShouldNotify => Severity is CallQualitySeverity.Danger or CallQualitySeverity.Warning;

    public static CallQualityGuardSnapshot Disabled() =>
        new(
            DateTimeOffset.Now,
            CallQualitySeverity.Disabled,
            "Call Quality Guard off",
            "Enable guard to detect AirPods Hands-Free call audio.",
            false,
            null,
            null,
            null,
            null);

    public static CallQualityGuardSnapshot Error(
        string status,
        string detail,
        AudioEndpointInfo? recommendedRender,
        AudioEndpointInfo? recommendedCapture,
        AudioEndpointInfo? currentRender,
        AudioEndpointInfo? currentCapture) =>
        new(
            DateTimeOffset.Now,
            CallQualitySeverity.Error,
            status,
            detail,
            false,
            recommendedRender,
            recommendedCapture,
            currentRender,
            currentCapture);
}
