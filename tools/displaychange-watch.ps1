# Counts WM_DISPLAYCHANGE broadcasts for a few seconds. Run with the app stopped
# to tell "the GPU/shell is re-reporting the display" apart from "our own window
# creation triggers it".
param([int]$Seconds = 10)

Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class DisplayWatch : Form
{
    public static int Count;
    public static readonly List<string> Seen = new List<string>();

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x007E)
        {
            Count++;
            long lp = m.LParam.ToInt64();
            int w = (int)(lp & 0xFFFF), h = (int)((lp >> 16) & 0xFFFF);
            Seen.Add(string.Format("{0:HH:mm:ss.fff} bpp={1} {2}x{3}", DateTime.Now, m.WParam.ToInt64(), w, h));
        }
        base.WndProc(ref m);
    }
}
'@ -ReferencedAssemblies System.Windows.Forms

$form = New-Object DisplayWatch
$form.ShowInTaskbar = $false
$form.WindowState = 'Minimized'
$form.Show()
$handle = $form.Handle
Start-Sleep -Seconds $Seconds
"WM_DISPLAYCHANGE count in ${Seconds}s = " + [DisplayWatch]::Count
[DisplayWatch]::Seen | ForEach-Object { "  $_" }
$form.Close()
