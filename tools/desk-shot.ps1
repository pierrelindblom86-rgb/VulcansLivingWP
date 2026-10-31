Add-Type -AssemblyName System.Drawing
Add-Type '
using System;
using System.Runtime.InteropServices;
public class SH {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
}'
# minimize the FluxWall main window and any big foreground apps except shell
Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and $_.Id -ne $PID } | ForEach-Object {
  [SH]::ShowWindow($_.MainWindowHandle, 6) | Out-Null   # SW_MINIMIZE
}
Start-Sleep -Seconds 3

$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
$g.Dispose()
$out = 'C:\pwallpaper\test-videos\desktop-now.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("saved " + $out + " " + (Get-Item $out).Length + " bytes")
