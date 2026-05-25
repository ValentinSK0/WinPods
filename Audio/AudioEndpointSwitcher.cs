using System.Runtime.InteropServices;

namespace WinPods;

public sealed class AudioEndpointSwitcher
{
    public void SetDefaultRender(AudioEndpointInfo endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Flow != AudioEndpointFlow.Render)
        {
            throw new ArgumentException("Endpoint must be render.", nameof(endpoint));
        }

        SetDefaultEndpoint(endpoint.Id, Role.Console);
        SetDefaultEndpoint(endpoint.Id, Role.Multimedia);
        SetDefaultEndpoint(endpoint.Id, Role.Communications);
    }

    public void SetDefaultCapture(AudioEndpointInfo endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Flow != AudioEndpointFlow.Capture)
        {
            throw new ArgumentException("Endpoint must be capture.", nameof(endpoint));
        }

        SetDefaultEndpoint(endpoint.Id, Role.Console);
        SetDefaultEndpoint(endpoint.Id, Role.Multimedia);
        SetDefaultEndpoint(endpoint.Id, Role.Communications);
    }

    private static void SetDefaultEndpoint(string endpointId, Role role)
    {
        IPolicyConfig? policyConfig = null;
        try
        {
            var policyConfigType = Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"), true)!;
            policyConfig = (IPolicyConfig)Activator.CreateInstance(policyConfigType)!;
            Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(endpointId, role));
        }
        finally
        {
            if (policyConfig is not null && Marshal.IsComObject(policyConfig))
            {
                Marshal.ReleaseComObject(policyConfig);
            }
        }
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr format);

        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool defaultFormat, IntPtr format);

        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName);

        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr endpointFormat, IntPtr mixFormat);

        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool defaultPeriod, IntPtr defaultPeriodValue, IntPtr minimumPeriodValue);

        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr periodValue);

        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);

        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);

        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PropertyKey key, IntPtr value);

        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PropertyKey key, IntPtr value);

        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceName, Role role);

        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool visible);
    }

    private enum Role
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        private Guid formatId;
        private int propertyId;
    }
}
