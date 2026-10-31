param(
    [string]$OutDir = 'C:\pwallpaper\tmp\win-grid'
)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
public static class Cap {
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr h);
    public static Bitmap Grab(int x, int y, int w, int h) {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return bmp;
    }
    public static string Grid(Bitmap b, int cols, int rows, int ox, int oy, int cw, int ch) {
        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < rows; r++) {
            for (int c = 0; c < cols; c++) {
                double sum = 0; long n = 0;
                for (int y = 0; y < ch; y += 3)
                    for (int x = 0; x < cw; x += 3) {
                        int px = ox + c * cw + x, py = oy + r * ch + y;
                        if (px < 0 || py < 0 || px >= b.Width || py >= b.Height) continue;
                        Color p = b.GetPixel(px, py); sum += p.R + p.G + p.B; n += 3;
                    }
                sb.Append(((int)(n == 0 ? 0 : sum / n)).ToString().PadLeft(4));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
'@ -ReferencedAssemblies System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$exe = 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe'
Get-Process FluxWall -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
$p = Start-Process $exe -ArgumentList '--applywall=C:\pwallpaper\test-videos\sample.mp4' -PassThru
Start-Sleep -Seconds 9

$h = (Get-Process -Id $p.Id).MainWindowHandle
[void][Cap]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 700

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$b = $win.Current.BoundingRectangle
Write-Host ("window_rect=" + $b.X + "," + $b.Y + " " + $b.Width + "x" + $b.Height)

$bmp = [Cap]::Grab([int]$b.X, [int]$b.Y, [int]$b.Width, [int]$b.Height)
$bmp.Save((Join-Path $OutDir 'window-before.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host 'brightness grid BEFORE (columns 0..11 across, rows 0..9 down):'
Write-Host ([Cap]::Grid($bmp, 12, 10, 0, 0, [int]($b.Width/12), [int]($b.Height/10)))

$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, 'Preview')
$btns = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) | Sort-Object { $_.Current.BoundingRectangle.X })
Write-Host ('preview_buttons=' + $btns.Count)
foreach ($t in $btns) {
    $rr = $t.Current.BoundingRectangle
    Write-Host ('  button at ' + [int]$rr.X + ',' + [int]$rr.Y + ' ' + [int]$rr.Width + 'x' + [int]$rr.Height)
}
$btns[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 2

$bmp2 = [Cap]::Grab([int]$b.X, [int]$b.Y, [int]$b.Width, [int]$b.Height)
$bmp2.Save((Join-Path $OutDir 'window-after.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host 'brightness grid AFTER:'
Write-Host ([Cap]::Grid($bmp2, 12, 10, 0, 0, [int]($b.Width/12), [int]($b.Height/10)))
Get-Process FluxWall -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host 'DONE'
