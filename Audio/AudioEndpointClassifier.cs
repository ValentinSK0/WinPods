namespace WinPods;

public static class AudioEndpointClassifier
{
    public static bool IsAppleAudio(string name)
    {
        var value = name.ToLowerInvariant();
        return value.Contains("airpods") ||
            value.Contains("air pods") ||
            value.Contains("beats");
    }

    public static bool IsHandsFree(string name)
    {
        var value = name.ToLowerInvariant();
        return value.Contains("hands-free") ||
            value.Contains("hands free") ||
            value.Contains("handsfree") ||
            value.Contains("headset") ||
            value.Contains("ag audio") ||
            value.Contains("hfp") ||
            value.Contains("hsp");
    }
}
