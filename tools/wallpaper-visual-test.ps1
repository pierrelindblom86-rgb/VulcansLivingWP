# Visual + hotkey check: applies a wallpaper, screenshots the real desktop layer,
# then uses the global Ctrl+Alt+W shortcut to pause and resume it.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class Keys {
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    public static void Hotkey() {
        const byte CTRL = 0x11, ALT = 0x12, W = 0x57, UP = 0x2;
        keybd_event(CTRL, 0, 0, UIntPtr.Zero);
        keybd_event(ALT, 0, 0, UIntPtr.Zero);
        keybd_event(W, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        keybd_event(W, 0, UP, UIntPtr.Zero);
        keybd_event(ALT, 0, UP, UIntPtr.Zero);
        keybd_event(CTRL, 0, UP, UIntPtr.Zero);
    }
}
'@ -ReferencedAssemblies System.Drawing

$exe = 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe'
$video = 'C:\pwallpaper\test-videos\sample.mp4'
$out = 'C:\pwallpaper\tmp\visual-test'
$logDir = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\logs'
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Check($label, $cond) { Write-Host (("{0,-40} {1}" -f $label, $(if ($cond) { 'PASS' } else { 'FAIL' }))) }

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

$app = Start-Process -FilePath $exe -ArgumentList ('--applywall=' + $video) -PassThru
Start-Sleep -Seconds 10
$app.Refresh()
[void][Keys]::SetForegroundWindow($app.MainWindowHandle)
Start-Sleep -Milliseconds 400

# Hide the main window so only the desktop layer is captured.
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class M{[DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);}'
[void][M]::SendMessage($app.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 3

$s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $s.Width, $s.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $s.Size)
$g.Dispose()
$bmp.Save((Join-Path $out 'desktop-with-wallpaper.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Check 'desktop screenshot saved' (Test-Path (Join-Path $out 'desktop-with-wallpaper.png'))

# Global shortcut: pause, then resume.
[Keys]::Hotkey()
Start-Sleep -Seconds 2
$log = Get-ChildItem $logDir -Filter '*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
$text = Get-Content $log.FullName -Raw
Check 'Ctrl+Alt+W paused playback'   ($text -match 'Playback paused manually')
[Keys]::Hotkey()
Start-Sleep -Seconds 2
$text = Get-Content $log.FullName -Raw
Check 'Ctrl+Alt+W resumed playback'  ($text -match 'Playback resumed manually')

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host 'DONE'
