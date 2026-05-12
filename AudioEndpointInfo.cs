namespace WinPods;

public enum AudioEndpointFlow
{
    Render,
    Capture,
}

public sealed record AudioEndpointInfo(
    string Id,
    string Name,
    AudioEndpointFlow Flow,
    bool IsDefaultCommunications,
    bool IsDefaultMultimedia)
{
    public bool IsAppleAudio => AudioEndpointClassifier.IsAppleAudio(Name);

    public bool IsHandsFree => AudioEndpointClassifier.IsHandsFree(Name);

    public bool IsStereoOutput => Flow == AudioEndpointFlow.Render && IsAppleAudio && !IsHandsFree;

    public bool IsSafeMicrophone => Flow == AudioEndpointFlow.Capture && !IsAppleAudio && !IsLoopbackLike;

    public bool IsLoopbackLike
    {
        get
        {
            var name = Name.ToLowerInvariant();
            return name.Contains("stereo mix") ||
                name.Contains("what u hear") ||
                name.Contains("loopback") ||
                name.Contains("monitor of");
        }
    }
}
