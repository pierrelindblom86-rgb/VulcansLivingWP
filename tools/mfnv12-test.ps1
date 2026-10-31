Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.IO;

public static class MFNv12
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PV { public short vt, r1, r2, r3; public long p0, p1; }

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAttr {
        [PreserveSig] int GetItem(ref Guid k, ref PV v);
        [PreserveSig] int GetItemType(ref Guid k, out int t);
        [PreserveSig] int CompareItem(ref Guid k, ref PV v, out bool eq);
        [PreserveSig] int Compare(IntPtr other, int mt, out bool eq);
        [PreserveSig] int GetUINT32(ref Guid k, out int v);
        [PreserveSig] int GetUINT64(ref Guid k, out long v);
        [PreserveSig] int GetDouble(ref Guid k, out double v);
        [PreserveSig] int GetGUID(ref Guid k, out Guid v);
        [PreserveSig] int GetStringLength(ref Guid k, out int n);
        [PreserveSig] int GetString(ref Guid k, IntPtr p, int c, out int n);
        [PreserveSig] int GetAllocatedString(ref Guid k, out IntPtr p, out int n);
        [PreserveSig] int GetBlobSize(ref Guid k, out int n);
        [PreserveSig] int GetBlob(ref Guid k, IntPtr p, int c, out int n);
        [PreserveSig] int GetAllocatedBlob(ref Guid k, out IntPtr p, out int n);
        [PreserveSig] int GetUnknown(ref Guid k, ref Guid iid, out IntPtr v);
        [PreserveSig] int SetItem(ref Guid k, ref PV v);
        [PreserveSig] int DeleteItem(ref Guid k);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid k, int v);
        [PreserveSig] int SetUINT64(ref Guid k, long v);
        [PreserveSig] int SetDouble(ref Guid k, double v);
        [PreserveSig] int SetGUID(ref Guid k, ref Guid v);
        [PreserveSig] int SetString(ref Guid k, [MarshalAs(UnmanagedType.LPWStr)] string s);
        [PreserveSig] int SetBlob(ref Guid k, IntPtr p, int c);
        [PreserveSig] int SetUnknown(ref Guid k, IntPtr u);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int GetItemByIndex(int i, out Guid k, out PV v);
        [PreserveSig] int CopyAllItems(IntPtr dest);
    }
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMT : IAttr { }
    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISR {
        [PreserveSig] int GetStreamSelection(int i, out bool sel);
        [PreserveSig] int SetStreamSelection(int i, bool sel);
        [PreserveSig] int GetNativeMediaType(int s, int i, out IMT mt);
        [PreserveSig] int GetCurrentMediaType(int s, out IMT mt);
        [PreserveSig] int SetCurrentMediaType(int s, IntPtr res, IMT mt);
        [PreserveSig] int SetCurrentPosition(ref Guid fmt, ref PV pos);
        [PreserveSig] int ReadSample(int s, int flags, out int ai, out int sf, out long ts, out IntPtr sample);
        [PreserveSig] int Flush(int s);
        [PreserveSig] int GetServiceForStream(int s, ref Guid svc, ref Guid iid, out IntPtr v);
        [PreserveSig] int GetPresentationAttribute(int s, ref Guid a, out PV v);
    }
    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISample : IAttr {
        [PreserveSig] int GetSampleFlags(out int f);
        [PreserveSig] int SetSampleFlags(int f);
        [PreserveSig] int GetSampleTime(out long t);
        [PreserveSig] int SetSampleTime(long t);
        [PreserveSig] int GetSampleDuration(out long d);
        [PreserveSig] int SetSampleDuration(long d);
        [PreserveSig] int GetBufferCount(out int c);
        [PreserveSig] int GetBufferByIndex(int i, out IntPtr b);
        [PreserveSig] int ConvertToContiguousBuffer(out IntPtr b);
        [PreserveSig] int AddBuffer(IntPtr b);
        [PreserveSig] int RemoveBufferByIndex(int i);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out int l);
        [PreserveSig] int CopyToBuffer(IntPtr b);
    }
    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBuf {
        [PreserveSig] int Lock(out IntPtr p, out int max, out int cur);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int c);
        [PreserveSig] int SetCurrentLength(int c);
        [PreserveSig] int GetMaxLength(out int m);
    }

    [DllImport("mfplat.dll")] static extern int MFStartup(int v, int f);
    [DllImport("mfplat.dll")] static extern int MFCreateAttributes([MarshalAs(UnmanagedType.Interface)] out IAttr a, int n);
    [DllImport("mfplat.dll")] static extern int MFCreateMediaType([MarshalAs(UnmanagedType.Interface)] out IMT t);
    [DllImport("mfreadwrite.dll", CharSet=CharSet.Unicode)] static extern int MFCreateSourceReaderFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.Interface)] IAttr attrs,
        [MarshalAs(UnmanagedType.Interface)] out ISR r);

    static unsafe byte[] Nv12ToBmp(byte* data, int w, int h, int yStride, int bufLen)
    {
        // NV12: Y plane (h rows), UV plane (h/2 rows interleaved U,V)
        int ySize = yStride * h;
        int uvStride = w;   // assume packed stride == width; validated by caller
        var pixels = new byte[w * h * 4];   // BGRA, top-down
        for (int y = 0; y < h; y++) {
            byte* yrow = data + y * yStride;
            byte* uvrow = data + ySize + (y / 2) * uvStride;
            for (int x = 0; x < w; x++) {
                int Y = yrow[x];
                int u = uvrow[(x & ~1)];
                int v = uvrow[(x & ~1) + 1];
                int c = Y - 16;
                int d = u - 128, e = v - 128;
                int B = (298 * c + 516 * d + 128) >> 8;
                int G = (298 * c - 100 * d - 208 * e + 128) >> 8;
                int R = (298 * c + 409 * e + 128) >> 8;
                int o = (y * w + x) * 4;
                pixels[o]     = (byte)(B < 0 ? 0 : B > 255 ? 255 : B);
                pixels[o + 1] = (byte)(G < 0 ? 0 : G > 255 ? 255 : G);
                pixels[o + 2] = (byte)(R < 0 ? 0 : R > 255 ? 255 : R);
                pixels[o + 3] = 255;
            }
        }
        // BMP container
        var bmp = new byte[54 + pixels.Length];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        int fsz = bmp.Length;
        bmp[2] = (byte)fsz; bmp[3] = (byte)(fsz >> 8); bmp[4] = (byte)(fsz >> 16); bmp[5] = (byte)(fsz >> 24);
        bmp[10] = 54; bmp[14] = 40;
        bmp[18] = (byte)w; bmp[19] = (byte)(w >> 8); bmp[20] = (byte)(w >> 16); bmp[21] = (byte)(w >> 24);
        bmp[22] = (byte)h; bmp[23] = (byte)(h >> 8); bmp[24] = (byte)(h >> 16); bmp[25] = (byte)(h >> 24);
        bmp[26] = 1; bmp[28] = 32;
        int rowLen = w * 4;
        int rawAt = 54 + (h - 1) * rowLen;   // bottom-up BMP
        for (int y = 0; y < h; y++) {
            Array.Copy(pixels, y * rowLen, bmp, 54 + (h - 1 - y) * rowLen, rowLen);
        }
        return bmp;
    }

    public static string Run(string path, string outFile)
    {
        int hr = MFStartup(0x00020070, 0);
        if (hr < 0) return "MFStartup 0x" + hr.ToString("X");
        IAttr attrs; hr = MFCreateAttributes(out attrs, 1); if (hr < 0) return "attrs 0x" + hr.ToString("X");
        ISR reader; hr = MFCreateSourceReaderFromURL(path, attrs, out reader);
        if (hr < 0) return "create 0x" + hr.ToString("X");
        try {
            IMT nat;
            hr = reader.GetNativeMediaType(unchecked((int)0xFFFFFFFC), 0, out nat);
            if (hr < 0) return "GetNative 0x" + hr.ToString("X");

            // clone native into empty MT
            IMT mt; hr = MFCreateMediaType(out mt);
            if (hr < 0) return "MFCreateMediaType 0x" + hr.ToString("X");
            IntPtr natPtr = Marshal.GetComInterfaceForObject(nat, typeof(IAttr));
            hr = ((IAttr)mt).CopyAllItems(natPtr);
            Marshal.Release(natPtr);
            if (hr < 0) return "CopyAllItems 0x" + hr.ToString("X");

            // force subtype NV12
            var kSub = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
            var nv12 = new Guid("3132564e-0000-0010-8000-00aa00389b71");
            hr = ((IAttr)mt).SetGUID(ref kSub, ref nv12);
            if (hr < 0) return "SetGUID-NV12 0x" + hr.ToString("X");

            hr = reader.SetCurrentMediaType(unchecked((int)0xFFFFFFFC), IntPtr.Zero, mt);
            if (hr < 0) return "SetCurrentMediaType 0x" + hr.ToString("X");

            var kFsz = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
            long packed;
            hr = ((IAttr)mt).GetUINT64(ref kFsz, out packed);
            int w = (int)(packed & 0xFFFFFFFF), h = (int)((packed >> 32) & 0xFFFFFFFF);

            int ai, sf; long ts; IntPtr smp;
            hr = reader.ReadSample(unchecked((int)0xFFFFFFFC), 0, out ai, out sf, out ts, out smp);
            if (hr < 0) return "ReadSample 0x" + hr.ToString("X");
            if (smp == IntPtr.Zero) return "null sample";
            var sample = (ISample)Marshal.GetObjectForIUnknown(smp);
            IntPtr bufPtr;
            hr = sample.ConvertToContiguousBuffer(out bufPtr);
            if (hr < 0) return "Convert 0x" + hr.ToString("X");
            var buf = (IBuf)Marshal.GetObjectForIUnknown(bufPtr);
            IntPtr data; int maxL, curL;
            hr = buf.Lock(out data, out maxL, out curL);
            if (hr < 0) return "Lock 0x" + hr.ToString("X");
            try {
                if (curL < w * h * 3 / 2) return "buffer too small: " + curL + " < " + (w * h * 3 / 2);
                unsafe {
                    var bmp = Nv12ToBmp((byte*)data, w, h, w, curL);
                    File.WriteAllBytes(outFile, bmp);
                    return "OK " + w + "x" + h + " bufLen=" + curL + " bytes=" + new FileInfo(outFile).Length;
                }
            } finally {
                buf.Unlock();
                Marshal.ReleaseComObject(buf);
                Marshal.ReleaseComObject(sample);
                Marshal.Release(smp);
                Marshal.ReleaseComObject(nat);
                Marshal.ReleaseComObject(mt);
            }
        } finally {
            Marshal.ReleaseComObject(reader);
            Marshal.ReleaseComObject(attrs);
        }
    }
}
"@
Write-Host ([MFNv12]::Run('C:\pwallpaper\test-videos\sample.mp4', 'C:\pwallpaper\test-videos\nv12-frame.bmp'))
