using System;
using System.Runtime.InteropServices;

// Only the members we call are meaningful; the full vtable order is declared
// so every slot lines up with the real COM interface. These are in-box Windows
// (WASAPI / Core Audio) APIs — no third-party code and nothing to install.

namespace FluxWall.Audio
{
    internal static class AudioConstants
    {
        public const int EDataFlowRender = 0;
        public const int ERoleConsole = 0;

        public const int AUDCLNT_SHAREMODE_SHARED = 0;

        public const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        public const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;

        public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x00000002;

        public const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
        public const int AUDCLNT_E_DEVICE_IN_USE = unchecked((int)0x8889000A);
        public const int AUDCLNT_E_RESOURCES_INVALIDATED = unchecked((int)0x88890026);

        public const ushort WAVE_FORMAT_PCM = 0x0001;
        public const ushort WAVE_FORMAT_IEEE_FLOAT = 0x0003;
        public const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;

        public static readonly Guid KSDATAFORMAT_SUBTYPE_PCM =
            new Guid("00000001-0000-0010-8000-00AA00389B71");
        public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT =
            new Guid("00000003-0000-0010-8000-00AA00389B71");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>Parsed, allocation-free description of the device mix format.</summary>
    public struct MixFormat
    {
        public int SampleRate;
        public int Channels;
        public int BitsPerSample;
        public int BlockAlign;
        public bool IsFloat;
        public bool IsValid;

        public static MixFormat Read(IntPtr ptr)
        {
            var result = new MixFormat();
            if (ptr == IntPtr.Zero) return result;

            var wf = (WAVEFORMATEX)Marshal.PtrToStructure(ptr, typeof(WAVEFORMATEX));
            result.SampleRate = (int)wf.nSamplesPerSec;
            result.Channels = wf.nChannels;
            result.BitsPerSample = wf.wBitsPerSample;
            result.BlockAlign = wf.nBlockAlign;

            ushort tag = wf.wFormatTag;
            if (tag == AudioConstants.WAVE_FORMAT_EXTENSIBLE && wf.cbSize >= 22)
            {
                // WAVEFORMATEXTENSIBLE: wValidBitsPerSample(2), dwChannelMask(4),
                // SubFormat(16) — the subtype GUID starts at byte 24.
                byte[] sub = new byte[16];
                Marshal.Copy(new IntPtr(ptr.ToInt64() + 24), sub, 0, 16);
                var subFormat = new Guid(sub);
                if (subFormat == AudioConstants.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT) tag = AudioConstants.WAVE_FORMAT_IEEE_FLOAT;
                else if (subFormat == AudioConstants.KSDATAFORMAT_SUBTYPE_PCM) tag = AudioConstants.WAVE_FORMAT_PCM;
            }

            result.IsFloat = tag == AudioConstants.WAVE_FORMAT_IEEE_FLOAT;
            result.IsValid =
                result.SampleRate > 0 && result.Channels > 0 && result.BlockAlign > 0 &&
                ((result.IsFloat && result.BitsPerSample == 32) ||
                 (!result.IsFloat && (result.BitsPerSample == 8 || result.BitsPerSample == 16 ||
                                      result.BitsPerSample == 24 || result.BitsPerSample == 32)));
            return result;
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint propertyCount);
        [PreserveSig] int GetAt(uint propertyIndex, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, IntPtr value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, IntPtr value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration,
            long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint paddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags,
            out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    /// <summary>Thin helpers around the Core Audio pieces we use.</summary>
    internal static class Wasapi
    {
        public static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        public static readonly Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

        public static IMMDeviceEnumerator CreateEnumerator()
        {
            return (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        public static IMMDevice DefaultRenderDevice(IMMDeviceEnumerator enumerator)
        {
            IMMDevice device;
            int hr = enumerator.GetDefaultAudioEndpoint(
                AudioConstants.EDataFlowRender, AudioConstants.ERoleConsole, out device);
            if (hr < 0) throw new COMException("No playback device is available.", hr);
            return device;
        }

        public static IAudioClient ActivateAudioClient(IMMDevice device)
        {
            var iid = IID_IAudioClient;
            object client;
            int hr = device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out client);
            if (hr < 0) throw new COMException("The audio device could not be activated.", hr);
            var audio = client as IAudioClient;
            if (audio == null)
            {
                try { Marshal.ReleaseComObject(client); } catch { }
                throw new COMException("The audio device has no WASAPI client.");
            }
            return audio;
        }

        /// <summary>Friendly name such as "Speakers (Realtek Audio)", or a fallback.</summary>
        public static string FriendlyName(IMMDevice device)
        {
            IPropertyStore store = null;
            try
            {
                if (device.OpenPropertyStore(0 /* STGM_READ */, out store) != 0 || store == null)
                    return "Default playback device";

                var key = new PROPERTYKEY
                {
                    fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
                    pid = 14, // PKEY_Device_FriendlyName
                };

                IntPtr buffer = Marshal.AllocHGlobal(32);
                try
                {
                    for (int i = 0; i < 32; i++) Marshal.WriteByte(buffer, i, 0);
                    if (store.GetValue(ref key, buffer) != 0) return "Default playback device";
                    ushort vt = (ushort)Marshal.ReadInt16(buffer);
                    if (vt != 31 /* VT_LPWSTR */) return "Default playback device";
                    IntPtr text = Marshal.ReadIntPtr(buffer, 8);
                    var name = text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text);
                    return string.IsNullOrEmpty(name) ? "Default playback device" : name;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch { return "Default playback device"; }
            finally { try { if (store != null) Marshal.ReleaseComObject(store); } catch { } }
        }
    }
}
