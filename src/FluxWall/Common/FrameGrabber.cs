using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FluxWall.Common
{
    #region Media Foundation interop
    // Only the members we call are meaningful; the full vtable order is declared
    // so every slot lines up with the real COM interface.
    // These are in-box Windows (Media Foundation) APIs — no third-party code.

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, out bool pbResult);
        [PreserveSig] int Compare(IntPtr pTheirs, int matchType, out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, byte[] pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IntPtr pDest);
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, out bool pbResult);
        [PreserveSig] int Compare(IntPtr pTheirs, int matchType, out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, byte[] pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IntPtr pDest);
        [PreserveSig] int GetMajorType(out Guid pguidMajorType);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, out bool pbResult);
        [PreserveSig] int Compare(IntPtr pTheirs, int matchType, out bool pbResult);
        [PreserveSig] int GetUINT32(ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong punValue);
        [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(ref Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64(ref Guid guidKey, ulong unValue);
        [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
        [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(ref Guid guidKey, byte[] pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IntPtr pDest);
        [PreserveSig] int GetSampleFlags(out uint pdwSampleFlags);
        [PreserveSig] int SetSampleFlags(uint dwSampleFlags);
        [PreserveSig] int GetSampleTime(out long phnsSampleTime);
        [PreserveSig] int SetSampleTime(long hnsSampleTime);
        [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
        [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
        [PreserveSig] int GetBufferCount(out uint pdwBufferCount);
        [PreserveSig] int GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);
        [PreserveSig] int RemoveBufferByIndex(uint dwIndex);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out uint pcbTotalLength);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(uint cbCurrentLength);
        [PreserveSig] int GetMaxLength(out uint pcbMaxLength);
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint dwStreamIndex, out bool pfSelected);
        [PreserveSig] int SetStreamSelection(uint dwStreamIndex, bool fSelected);
        [PreserveSig] int GetNativeMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int GetCurrentMediaType(uint dwStreamIndex, out IMFMediaType ppMediaType);
        [PreserveSig] int SetCurrentMediaType(uint dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
        [PreserveSig] int SetCurrentPosition(ref Guid guidTimeFormat, IntPtr varPosition);
        [PreserveSig] int ReadSample(uint dwStreamIndex, uint dwFlags,
            out uint pdwActualStreamIndex, out uint pdwStreamFlags, out long pllTimestamp, out IMFSample ppSample);
        [PreserveSig] int Flush(uint dwStreamIndex);
        [PreserveSig] int GetServiceForStream(uint dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int GetPresentationAttribute(uint dwStreamIndex, ref Guid guidAttribute, IntPtr pvarAttribute);
    }
    #endregion

    /// <summary>
    /// Pulls a single poster frame straight out of the Windows media decoder
    /// (Media Foundation source reader). This needs no window and no screen
    /// capture, so generating thumbnails never flashes anything on screen.
    /// </summary>
    public static class FrameGrabber
    {
        const uint FirstVideoStream = 0xFFFFFFFC;

        static readonly Guid MtMajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        static readonly Guid MtSubtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        static readonly Guid MtFrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        static readonly Guid MtDefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        static readonly Guid ReaderEnableVideoProcessing = new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
        static readonly Guid MediaTypeVideo = new Guid("73646976-0000-0010-8000-00AA00389B71");
        static readonly Guid VideoFormatRgb32 = new Guid("00000016-0000-0010-8000-00AA00389B71");

        [DllImport("mfplat.dll", ExactSpelling = true)]
        static extern int MFStartup(int version, int dwFlags);
        [DllImport("mfplat.dll", ExactSpelling = true)]
        static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, int cInitialSize);
        [DllImport("mfplat.dll", ExactSpelling = true)]
        static extern int MFCreateMediaType(out IMFMediaType ppMFType);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int MFCreateSourceReaderFromURL(
            [MarshalAs(UnmanagedType.LPWStr)] string pwszURL, IMFAttributes pAttributes, out IMFSourceReader ppSourceReader);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        static extern void CopyMemory(IntPtr dest, IntPtr src, IntPtr count);

        static readonly object InitGate = new object();
        static bool _mfReady;

        static bool EnsureMediaFoundation()
        {
            lock (InitGate)
            {
                if (_mfReady) return true;
                // MF_VERSION for Windows 7+ is 0x00020070. 0 == synchronous start.
                int hr = MFStartup(0x00020070, 0);
                if (hr < 0)
                {
                    Logger.Info("Media Foundation could not be started (hr=0x" + hr.ToString("X8") + "); thumbnails unavailable.");
                    return false;
                }
                _mfReady = true;
                return true;
            }
        }

        /// <summary>
        /// Decodes the first usable frame and returns it scaled to fit
        /// <paramref name="maxWidth"/> x <paramref name="maxHeight"/>.
        /// Returns null when the file cannot be decoded (missing codec, bad file).
        /// </summary>
        public static Bitmap ExtractPoster(string path, int maxWidth, int maxHeight)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!EnsureMediaFoundation()) return null;

            IMFAttributes attrs = null;
            IMFMediaType outputType = null;
            IMFSourceReader reader = null;
            try
            {
                int hr = MFCreateAttributes(out attrs, 1);
                if (hr < 0) return null;
                Guid processing = ReaderEnableVideoProcessing;
                attrs.SetUINT32(ref processing, 1);

                hr = MFCreateSourceReaderFromURL(path, attrs, out reader);
                if (hr < 0)
                {
                    Logger.Info("Frame grab failed for '" + path + "' (source reader hr=0x" + hr.ToString("X8") + ").");
                    return null;
                }

                hr = MFCreateMediaType(out outputType);
                if (hr < 0) return null;
                Guid majorKey = MtMajorType, majorValue = MediaTypeVideo;
                Guid subKey = MtSubtype, subValue = VideoFormatRgb32;
                outputType.SetGUID(ref majorKey, ref majorValue);
                outputType.SetGUID(ref subKey, ref subValue);

                hr = reader.SetCurrentMediaType(FirstVideoStream, IntPtr.Zero, outputType);
                if (hr < 0)
                {
                    Logger.Info("Frame grab failed for '" + path + "' (RGB32 unusable hr=0x" + hr.ToString("X8") + ").");
                    return null;
                }

                // Ask the decoder what it actually negotiated (size + row stride).
                IMFMediaType current;
                hr = reader.GetCurrentMediaType(FirstVideoStream, out current);
                if (hr < 0) return null;

                int width, height, stride;
                try
                {
                    Guid sizeKey = MtFrameSize, strideKey = MtDefaultStride;
                    ulong size;
                    current.GetUINT64(ref sizeKey, out size);
                    width = (int)(size >> 32);      // MF packs width in the high dword
                    height = (int)(uint)size;
                    uint strideValue;
                    stride = current.GetUINT32(ref strideKey, out strideValue) >= 0
                        ? (int)strideValue : width * 4;
                }
                finally { Marshal.ReleaseComObject(current); }

                if (width <= 0 || height <= 0) return null;
                if (stride == 0) stride = width * 4;

                int avatarBytes = Math.Abs(stride) * height;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    uint actual, flags;
                    long timestamp;
                    IMFSample sample;
                    hr = reader.ReadSample(FirstVideoStream, 0, out actual, out flags, out timestamp, out sample);
                    if (hr < 0) return null;
                    if (sample == null) continue;

                    IMFMediaBuffer buffer = null;
                    try
                    {
                        hr = sample.ConvertToContiguousBuffer(out buffer);
                        if (hr < 0) continue;

                        IntPtr pixels;
                        uint maxLength, currentLength;
                        hr = buffer.Lock(out pixels, out maxLength, out currentLength);
                        if (hr < 0) continue;
                        try
                        {
                            if (currentLength < (uint)avatarBytes) continue;
                            return CopyToBitmap(pixels, width, height, stride, maxWidth, maxHeight);
                        }
                        finally { buffer.Unlock(); }
                    }
                    finally
                    {
                        if (buffer != null) Marshal.ReleaseComObject(buffer);
                        Marshal.ReleaseComObject(sample);
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                Logger.Info("Frame grab failed for '" + path + "': " + e.Message);
                return null;
            }
            finally
            {
                if (outputType != null) { try { Marshal.ReleaseComObject(outputType); } catch { } }
                if (reader != null) { try { Marshal.ReleaseComObject(reader); } catch { } }
                if (attrs != null) { try { Marshal.ReleaseComObject(attrs); } catch { } }
            }
        }

        static Bitmap CopyToBitmap(IntPtr pixels, int width, int height, int stride, int maxWidth, int maxHeight)
        {
            // Fit inside the requested box, keeping the video's real aspect ratio.
            double scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
            if (scale <= 0) scale = 1.0;
            int outW = Math.Max(1, (int)Math.Round(width * scale));
            int outH = Math.Max(1, (int)Math.Round(height * scale));

            using (var raw = new Bitmap(width, height, PixelFormat.Format32bppRgb))
            {
                var rect = new Rectangle(0, 0, width, height);
                var data = raw.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    for (int y = 0; y < height; y++)
                    {
                        // A negative stride means the decoder handed us a bottom-up image.
                        int sourceRow = stride < 0 ? height - 1 - y : y;
                        CopyMemory(
                            IntPtr.Add(data.Scan0, y * data.Stride),
                            IntPtr.Add(pixels, sourceRow * Math.Abs(stride)),
                            (IntPtr)(width * 4));
                    }
                }
                finally { raw.UnlockBits(data); }

                var poster = new Bitmap(outW, outH, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(poster))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(raw, new Rectangle(0, 0, outW, outH));
                }
                return poster;
            }
        }
    }
}
