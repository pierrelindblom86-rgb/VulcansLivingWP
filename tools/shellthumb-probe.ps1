param(
    [string]$OutDir = 'C:\pwallpaper\tmp\shellthumb'
)
$ErrorActionPreference = 'Stop'
New-Item -Path $OutDir -ItemType Directory -Force | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8dc59b30d163"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(int cx, int cy, uint flags, out IntPtr phbitmap);
}

public static class ShellThumb
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    public const uint SIIGBF_RESIZETOFIT = 0;
    public const uint SIIGBF_THUMBNAILONLY = 8;
    public const uint SIIGBF_BIGGERSIZEOK = 1;

    public static IntPtr Get(string path, int w, int h, out int hr)
    {
        var guid = typeof(IShellItemImageFactory).GUID;
        IShellItemImageFactory f;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref guid, out f);
        try
        {
            IntPtr hb;
            hr = f.GetImage(w, h, SIIGBF_THUMBNAILONLY, out hb);
            if (hr == 0 && hb != IntPtr.Zero) return hb;
            hr = f.GetImage(w, h, SIIGBF_RESIZETOFIT, out hb);
            if (hr == 0 && hb != IntPtr.Zero) return hb;
            return IntPtr.Zero;
        }
        finally { Marshal.ReleaseComObject(f); }
    }
}
'@

Add-Type -AssemblyName PresentationCore, WindowsBase

foreach ($video in @('C:\pwallpaper\test-videos\sample.mp4', 'C:\pwallpaper\test-videos\black-cat.mp4')) {
    if (-not (Test-Path $video)) { Write-Host ("missing: " + $video); continue }
    $hr = 0
    $hb = [IntPtr]::Zero
    try {
        $hb = [ShellThumb]::Get($video, 480, 270, [ref]$hr)
    } catch {
        Write-Host ("EXCEPTION " + (Split-Path $video -Leaf) + " :: " + $_.Exception.Message)
        continue
    }
    if ($hb -eq [IntPtr]::Zero) {
        Write-Host ((Split-Path $video -Leaf) + " => NO FRAME hr=" + $hr)
        continue
    }
    $src = [System.Windows.Interop.Imaging]::CreateBitmapSourceFromHBitmap(
        $hb, [IntPtr]::Zero, [System.Windows.Int32Rect]::Empty,
        [System.Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions())
    $name = [IO.Path]::GetFileNameWithoutExtension($video) + '-shell.png'
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($src))
    $fs = [IO.File]::Create((Join-Path $OutDir $name))
    $enc.Save($fs)
    $fs.Close()
    [void][ShellThumb]::DeleteObject($hb)

    # Average brightness so a black frame is distinguishable from a real one.
    $w = $src.PixelWidth; $h = $src.PixelHeight
    $conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($src, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
    $stride = $w * 4
    $buf = New-Object byte[] ($stride * $h)
    $conv.CopyPixels($buf, $stride, 0)
    [double]$sum = 0; [int]$n = 0
    for ($y = 0; $y -lt $h; $y += 7) {
        for ($x = 0; $x -lt $w; $x += 7) {
            $i = $y * $stride + $x * 4
            $sum += $buf[$i] + $buf[$i + 1] + $buf[$i + 2]
            $n += 3
        }
    }
    Write-Host ((Split-Path $video -Leaf) + " => " + $w + "x" + $h + " brightness=" + [math]::Round($sum / $n, 1) + " saved=" + $name)
}
