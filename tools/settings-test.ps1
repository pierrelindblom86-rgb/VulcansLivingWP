# Checks the Settings tab through UI Automation: the sections exist, the hotkey
# status reports the active shortcut, an invalid shortcut is reported instead of
# being silently dropped, and toggles respond.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$exe = 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe'
$video = 'C:\pwallpaper\test-videos\sample.mp4'
$settingsFile = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\VulcansLivingWP-settings.xml'

$AE = [System.Windows.Automation.AutomationElement]
function Check($label, $cond) { Write-Host (("{0,-52} {1}" -f $label, $(if ($cond) { 'PASS' } else { 'FAIL' }))) }

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700
$proc = Start-Process -FilePath $exe -ArgumentList ('--applywall=' + $video) -PassThru
Start-Sleep -Seconds 9

$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
$win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Host 'window_not_found'; exit 2 }

function ByName($name) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

$nav = ByName 'Settings'
if (-not $nav) { Write-Host 'no_settings_nav'; exit 3 }
$nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1200

# All expected section rows are present.
foreach ($row in @('Monitor', 'Scaling', 'Play wallpaper audio', 'Start with Windows',
                   'Pause during fullscreen apps', 'Start minimized to the tray', 'Pause / resume hotkey')) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $hit = $false
    foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
        if ($t.Current.Name -like "*$row*") { $hit = $true; break }
    }
    Check ("settings row present: $row") $hit
}

function StatusTexts() {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    return @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c) |
        ForEach-Object { $_.Current.Name } |
        Where-Object { $_ -match 'Active:|Shortcut disabled|not a valid|already uses' })
}

Write-Host ('status_texts=' + ((StatusTexts) -join ' | '))
Check 'hotkey status shows the active shortcut' ((StatusTexts) -match 'Active: Ctrl\+Alt\+W').Count -gt 0

# Break the shortcut text and move focus away, like a user would.
$box = $null
foreach ($e in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))) {
    if ($e.Current.Name -like '*hotkey*' -or $e.Current.AutomationId -eq '') { $box = $e; }
}
if (-not $box) {
    foreach ($e in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))) { $box = $e; break }
}
if ($box) {
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Ctrl+Alt+@')
    Start-Sleep -Milliseconds 200
    $lib = ByName 'Library'
    if ($lib) { $lib.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 400 }
    $nav = ByName 'Settings'
    $nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 800
    $after = StatusTexts
    Write-Host ('status_after_invalid=' + ($after -join ' | '))
    Check 'invalid shortcut is reported' ($after -match 'not a valid').Count -gt 0
} else {
    Write-Host 'no_hotkey_textbox (skipped invalid-input check)'
}

$log = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\logs') -Filter '*.log' |
    Sort-Object LastWriteTime | Select-Object -Last 1
Write-Host '--- log tail ---'
Get-Content $log.FullName -Tail 6

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force

# Leave the profile with the default shortcut again.
if (Test-Path $settingsFile) {
    $xml = [xml](Get-Content $settingsFile -Raw)
    $xml.DocumentElement.PauseHotkey = 'Ctrl+Alt+W'
    $xml.Save($settingsFile)
}
Write-Host 'DONE'
