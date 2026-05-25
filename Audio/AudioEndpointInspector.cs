using System.Runtime.InteropServices;

namespace WinPods;

public sealed class AudioEndpointInspector
{
    private const int DeviceStateActive = 0x00000001;
    private const int StgmRead = 0x00000000;
    private static readonly PropertyKey FriendlyNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    public AudioEndpointInventory Inspect()
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(new Guid("bcde0395-e52f-467c-8e3d-c4579291692e"), true)!;
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            var defaultCommunicationRender = GetDefaultEndpoint(enumerator, DataFlow.Render, Role.Communications);
            var defaultCommunicationCapture = GetDefaultEndpoint(enumerator, DataFlow.Capture, Role.Communications);
            var defaultMultimediaRender = GetDefaultEndpoint(enumerator, DataFlow.Render, Role.Multimedia);
            var defaultMultimediaCapture = GetDefaultEndpoint(enumerator, DataFlow.Capture, Role.Multimedia);

            var renderEndpoints = EnumerateEndpoints(
                enumerator,
                DataFlow.Render,
                defaultCommunicationRender?.Id,
                defaultMultimediaRender?.Id);
            var captureEndpoints = EnumerateEndpoints(
                enumerator,
                DataFlow.Capture,
                defaultCommunicationCapture?.Id,
                defaultMultimediaCapture?.Id);

            return new AudioEndpointInventory(
                defaultCommunicationRender,
                defaultCommunicationCapture,
                defaultMultimediaRender,
                defaultMultimediaCapture,
                renderEndpoints,
                captureEndpoints);
        }
        finally
        {
            ReleaseIfCom(enumerator);
        }
    }

    private static AudioEndpointInfo? GetDefaultEndpoint(IMMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        IMMDevice? device = null;
        try
        {
            var hr = enumerator.GetDefaultAudioEndpoint(flow, role, out device);
            return hr == 0 && device is not null
                ? ReadEndpoint(device, flow, role == Role.Communications, role == Role.Multimedia)
                : null;
        }
        finally
        {
            ReleaseIfCom(device);
        }
    }

    private static IReadOnlyList<AudioEndpointInfo> EnumerateEndpoints(
        IMMDeviceEnumerator enumerator,
        DataFlow flow,
        string? defaultCommunicationsId,
        string? defaultMultimediaId)
    {
        IMMDeviceCollection? collection = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));

            var endpoints = new List<AudioEndpointInfo>((int)count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                    var endpoint = ReadEndpoint(
                        device,
                        flow,
                        DeviceIdEquals(device, defaultCommunicationsId),
                        DeviceIdEquals(device, defaultMultimediaId));
                    endpoints.Add(endpoint);
                }
                finally
                {
                    ReleaseIfCom(device);
                }
            }

            return endpoints
                .OrderByDescending(static endpoint => endpoint.IsDefaultCommunications)
                .ThenByDescending(static endpoint => endpoint.IsDefaultMultimedia)
                .ThenBy(static endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            ReleaseIfCom(collection);
        }
    }

    private static bool DeviceIdEquals(IMMDevice device, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        Marshal.ThrowExceptionForHR(device.GetId(out var deviceId));
        return deviceId.Equals(id, StringComparison.OrdinalIgnoreCase);
    }

    private static AudioEndpointInfo ReadEndpoint(
        IMMDevice device,
        DataFlow flow,
        bool isDefaultCommunications,
        bool isDefaultMultimedia)
    {
        Marshal.ThrowExceptionForHR(device.GetId(out var id));
        var name = ReadFriendlyName(device);
        return new AudioEndpointInfo(
            id,
            name,
            flow == DataFlow.Render ? AudioEndpointFlow.Render : AudioEndpointFlow.Capture,
            isDefaultCommunications,
            isDefaultMultimedia);
    }

    private static string ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        var propVariant = PropVariant.Empty;
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(StgmRead, out propertyStore));
            var friendlyNameKey = FriendlyNameKey;
            Marshal.ThrowExceptionForHR(propertyStore.GetValue(ref friendlyNameKey, out propVariant));
            return propVariant.GetString() ?? "Unknown audio device";
        }
        finally
        {
            PropVariantClear(ref propVariant);
            ReleaseIfCom(propertyStore);
        }
    }

    private static void ReleaseIfCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport]
    [Guid("a95664d2-9614-4f35-a746-de8db63617e6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(DataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);

        int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice endpoint);

        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        int RegisterEndpointNotificationCallback(IntPtr client);

        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0bd7a1be-7a1a-44db-8397-cc5392387b5e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint count);

        int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("d666063f-1587-4e43-81f1-b948e807363f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr interfacePointer);

        int OpenPropertyStore(int access, out IPropertyStore properties);

        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        int GetState(out int state);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint propertyCount);

        int GetAt(uint propertyIndex, out PropertyKey key);

        int GetValue(ref PropertyKey key, out PropVariant value);

        int SetValue(ref PropertyKey key, ref PropVariant value);

        int Commit();
    }

    private enum DataFlow
    {
        Render = 0,
        Capture = 1,
    }

    private enum Role
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid formatId, int propertyId)
    {
        private readonly Guid formatId = formatId;
        private readonly int propertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        private ushort vt;
        private ushort reserved1;
        private ushort reserved2;
        private ushort reserved3;
        private IntPtr pointerValue;
        private int value1;
        private int value2;

        public static PropVariant Empty => default;

        public string? GetString() => vt == 31 && pointerValue != IntPtr.Zero
            ? Marshal.PtrToStringUni(pointerValue)
            : null;
    }
}

public sealed record AudioEndpointInventory(
    AudioEndpointInfo? DefaultCommunicationRender,
    AudioEndpointInfo? DefaultCommunicationCapture,
    AudioEndpointInfo? DefaultMultimediaRender,
    AudioEndpointInfo? DefaultMultimediaCapture,
    IReadOnlyList<AudioEndpointInfo> RenderEndpoints,
    IReadOnlyList<AudioEndpointInfo> CaptureEndpoints);
