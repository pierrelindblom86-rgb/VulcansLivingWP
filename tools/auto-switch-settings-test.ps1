# Checks the automatic-switching settings through the real UI:
# rows exist, the interval is disabled until switching is on, clicking the toggle
# writes settings.xml, all six intervals are offered, picking an interval writes
# it too, both survive a restart, and an invalid saved interval falls back to
# 5 minutes.
#
# Run against the installed app with -Exe 'C:\Program Files (x86)\...'.
param(
    [string]$Exe = 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
public static class Pix {
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    public static void Click(int x, int y) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(120);
        mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
    }
    public static Color Pixel(int x, int y) {
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
            return bmp.GetPixel(0, 0);
        }
    }
}
'@ -ReferencedAssemblies System.Drawing

$appDir  = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP'
$libFile = Join-Path $appDir 'VulcansLivingWP-library.xml'
$setFile = Join-Path $appDir 'VulcansLivingWP-settings.xml'
$logDir  = Join-Path $appDir 'logs'
$backup  = 'C:\pwallpaper\tmp\auto-switch-test\backup'
$videoA  = 'C:\Users\Perra\Downloads\white-wolf-girl.1920x1080.mp4'
$videoB  = 'C:\Users\Perra\Downloads\gas-station-in-the-snowy-night.1920x1080.mp4'
$idA     = 'autotest0000000000000000000000a'
$idB     = 'autotest0000000000000000000000b'
$AE      = [System.Windows.Automation.AutomationElement]
$script:failures = 0

function Check($label, $cond, $detail = '') {
    if ($cond) { Write-Host (("{0,-58} PASS {1}" -f $label, $detail)) }
    else { $script:failures++; Write-Host (("{0,-58} FAIL {1}" -f $label, $detail)) }
}
function Stop-App {
    Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}
function Write-Library {
    $items = @(
        ('  <WallpaperItem><Id>' + $idA + '</Id><FilePath>' + [System.Security.SecurityElement]::Escape($videoA) + '</FilePath><AddedAt>' + (Get-Date).ToString('o') + '</AddedAt></WallpaperItem>'),
        ('  <WallpaperItem><Id>' + $idB + '</Id><FilePath>' + [System.Security.SecurityElement]::Escape($videoB) + '</FilePath><AddedAt>' + (Get-Date).ToString('o') + '</AddedAt></WallpaperItem>')
    )
    $xml = "<?xml version=`"1.0`"?>`r`n<ArrayOfWallpaperItem xmlns:xsd=`"http://www.w3.org/2001/XMLSchema`" xmlns:xsi=`"http://www.w3.org/2001/XMLSchema-instance`">`r`n" +
           ($items -join "`r`n") + "`r`n</ArrayOfWallpaperItem>"
    [IO.File]::WriteAllText($libFile, $xml, (New-Object System.Text.UTF8Encoding($false)))
}
function Set-Setting([string]$name, [string]$value) {
    [xml]$xml = Get-Content $setFile -Raw
    $node = $xml.DocumentElement.SelectSingleNode($name)
    if (-not $node) { $node = $xml.CreateElement($name); [void]$xml.DocumentElement.AppendChild($node) }
    $node.InnerText = $value
    $xml.Save($setFile)
}
function Get-Setting([string]$name) {
    [xml]$xml = Get-Content $setFile -Raw
    $node = $xml.DocumentElement.SelectSingleNode($name)
    if ($node) { return $node.InnerText } else { return '' }
}
function Focus-Settings([int]$procId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $procId)
    $win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if (-not $win) { throw 'main window not found' }
    $navCond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Settings')
    $nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $navCond)
    $nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 1000
    return $win
}
function TextElement($win, [string]$name) {
    $c = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
    return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function Combos($win) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
    return @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c) |
        Sort-Object { $_.Current.BoundingRectangle.Y })
}
function SelectedComboText($combo) {
    $p = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    $sel = $p.Current.GetSelection()
    if ($sel.Count -eq 0) { return '' }
    return $sel[0].Current.Name
}
function Toggle-Center($win, $columnRight) {
    $label = TextElement $win 'Change wallpaper automatically'
    if (-not $label) { throw 'toggle label not found' }
    $lr = $label.Current.BoundingRectangle
    # The switches share the control column with the combo boxes below them.
    return @([int]($columnRight - 22), [int]($lr.Y + 19))
}

Stop-App
Copy-Item $libFile (Join-Path $backup 'ui-library.xml') -Force
Copy-Item $setFile (Join-Path $backup 'ui-settings.xml') -Force

try {
    Write-Library
    Set-Setting 'AutoSwitchWallpapers' 'false'
    Set-Setting 'AutoSwitchMinutes' '15'
    Set-Setting 'ActiveWallpaperId' $idA
    Set-Setting 'PlayWallpaperAudio' 'false'
    Set-Setting 'PauseHotkey' 'Ctrl+Alt+W'

    Write-Host "`n=== saved state is shown when the page opens ==="
    $proc = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 10
    $win = Focus-Settings $proc.Id

    Check 'row: Change wallpaper automatically' ($null -ne (TextElement $win 'Change wallpaper automatically'))
    Check 'row: Change every'                   ($null -ne (TextElement $win 'Change every'))
    $combos = Combos $win
    Check 'three combo boxes on the page'       ($combos.Count -eq 3) ("count=" + $combos.Count)
    $interval = $combos[$combos.Count - 1]
    Check 'saved interval is selected'          ((SelectedComboText $interval) -eq 'Every 15 minutes') (SelectedComboText $interval)
    Check 'interval disabled while switching off' (-not $interval.Current.IsEnabled)

    $columnRight = $interval.Current.BoundingRectangle.Right
    $p = Toggle-Center $win $columnRight
    $offPixel = [Pix]::Pixel($p[0], $p[1])

    Write-Host "`n=== toggle on ==="
    [Pix]::Click($p[0], $p[1])
    Start-Sleep -Milliseconds 900
    Check 'settings.xml stores enabled=true'    ((Get-Setting 'AutoSwitchWallpapers') -eq 'true') (Get-Setting 'AutoSwitchWallpapers')
    $combos = Combos $win
    $interval = $combos[$combos.Count - 1]
    Check 'interval becomes usable'             ($interval.Current.IsEnabled)
    $onPixel = [Pix]::Pixel($p[0], $p[1])
    Check 'toggle paints as on'                 ($onPixel.B -gt 200 -and $onPixel.G -lt 120) ("rgb=" + $onPixel.R + ',' + $onPixel.G + ',' + $onPixel.B)
    $log = Get-ChildItem $logDir -Filter '*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
    Check 'timer armed at the saved 15 minutes' ((Get-Content $log.FullName -Raw) -match 'Auto-switch: next change in 15 min\.')

    $expand = $interval.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 700
    $itemCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $names = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -like 'Every *' } | Select-Object -Unique)
    try { $expand.Collapse() } catch { }
    $expected = @('Every minute','Every 5 minutes','Every 10 minutes','Every 15 minutes','Every 30 minutes','Every 60 minutes')
    $missing = @($expected | Where-Object { $names -notcontains $_ })
    Check 'all six interval choices'            ($missing.Count -eq 0) (($names -join ', '))

    Write-Host "`n=== choose 30 minutes ==="
    $expand = $interval.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 700
    $itemCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Every 30 minutes')),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
    # WPF exposes the popup items twice; only the real ComboBoxItem instances
    # support SelectionItemPattern, so pick the first one that does.
    $item = $null
    $selection = $null
    foreach ($candidate in $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCond)) {
        $pattern = $null
        if ($candidate.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            $item = $candidate
            $selection = $pattern
            break
        }
    }
    if ($item) {
        $selection.Select()
        Start-Sleep -Milliseconds 700
        try { $expand.Collapse() } catch { }
    } else {
        Write-Host 'list item not found'
    }
    Check 'settings.xml stores 30 minutes'      ((Get-Setting 'AutoSwitchMinutes') -eq '30') (Get-Setting 'AutoSwitchMinutes')
    Check 'timer re-armed at 30 minutes'        ((Get-Content $log.FullName -Raw) -match 'Auto-switch: next change in 30 min\.')

    Write-Host "`n=== toggle off and on again ==="
    [Pix]::Click($p[0], $p[1])
    Start-Sleep -Milliseconds 900
    Check 'settings.xml stores enabled=false'   ((Get-Setting 'AutoSwitchWallpapers') -eq 'false') (Get-Setting 'AutoSwitchWallpapers')
    $combos = Combos $win
    $interval = $combos[$combos.Count - 1]
    Check 'interval disabled again'             (-not $interval.Current.IsEnabled)
    [Pix]::Click($p[0], $p[1])
    Start-Sleep -Milliseconds 900
    Check 'settings.xml stores enabled=true again' ((Get-Setting 'AutoSwitchWallpapers') -eq 'true') (Get-Setting 'AutoSwitchWallpapers')

    Write-Host "`n=== restart keeps everything ==="
    Stop-App
    $proc = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 10
    $win = Focus-Settings $proc.Id
    $combos = Combos $win
    $interval = $combos[$combos.Count - 1]
    Check 'interval still 30 minutes'           ((SelectedComboText $interval) -eq 'Every 30 minutes') (SelectedComboText $interval)
    $p = Toggle-Center $win $interval.Current.BoundingRectangle.Right
    $restartPixel = [Pix]::Pixel($p[0], $p[1])
    Check 'toggle still on after restart'       ($restartPixel.B -gt 200 -and $restartPixel.G -lt 120) ("rgb=" + $restartPixel.R + ',' + $restartPixel.G + ',' + $restartPixel.B)

    Write-Host "`n=== unrelated settings untouched ==="
    Check 'StartWithWindows preserved'          ((Get-Setting 'StartWithWindows') -eq 'true')
    Check 'PauseHotkey preserved'               ((Get-Setting 'PauseHotkey') -eq 'Ctrl+Alt+W')
    Check 'PlayWallpaperAudio preserved'        ((Get-Setting 'PlayWallpaperAudio') -eq 'false')

    Write-Host "`n=== invalid saved interval falls back to 5 ==="
    Stop-App
    Set-Setting 'AutoSwitchMinutes' '7'
    $proc = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 10
    $win = Focus-Settings $proc.Id
    $combos = Combos $win
    $interval = $combos[$combos.Count - 1]
    Check 'invalid interval shows 5 minutes'    ((SelectedComboText $interval) -eq 'Every 5 minutes') (SelectedComboText $interval)
    $log = Get-ChildItem $logDir -Filter '*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
    Check 'no ERROR in log'                     ((Get-Content $log.FullName -Raw) -notmatch '\[ERROR\]')
}
finally {
    Stop-App
    Copy-Item (Join-Path $backup 'ui-library.xml') $libFile -Force
    Copy-Item (Join-Path $backup 'ui-settings.xml') $setFile -Force
}

Write-Host ''
if ($script:failures -eq 0) { Write-Host 'RESULT: all checks passed' }
else { Write-Host ("RESULT: {0} check(s) failed" -f $script:failures) }
Write-Host 'DONE'
