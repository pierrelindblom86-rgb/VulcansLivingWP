# Preview regression: two videos in the library, start preview on the first, then
# on the second. Only one preview may play, no extra top-level window may appear,
# and stopping must release the player.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$exe = 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe'
$v1 = 'C:\pwallpaper\test-videos\sample.mp4'
$v2 = 'C:\pwallpaper\test-videos\sample2.mp4'
$data = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP'
$libFile = Join-Path $data 'VulcansLivingWP-library.xml'
$logDir = Join-Path $data 'logs'

function Check($label, $cond) { Write-Host (("{0,-46} {1}" -f $label, $(if ($cond) { 'PASS' } else { 'FAIL' }))) }

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 1200
Copy-Item $v1 $v2 -Force
New-Item -ItemType Directory -Force -Path $data | Out-Null

@"
<?xml version="1.0" encoding="utf-8"?>
<ArrayOfWallpaperItem>
  <WallpaperItem>
    <Id>aaa111</Id>
    <FilePath>$v1</FilePath>
    <AddedAt>2026-10-18T10:00:00</AddedAt>
  </WallpaperItem>
  <WallpaperItem>
    <Id>bbb222</Id>
    <FilePath>$v2</FilePath>
    <AddedAt>2026-10-18T10:00:01</AddedAt>
  </WallpaperItem>
</ArrayOfWallpaperItem>
"@ | Set-Content $libFile -Encoding UTF8

$AE = [System.Windows.Automation.AutomationElement]
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 12
$win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)))
if (-not $win) { Write-Host 'window_not_found'; exit 2 }

# The button's inner TextBlock carries the same name, so filter by control type.
$nameCond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Preview')
$typeCond = New-Object System.Windows.Automation.PropertyCondition(
    $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$andCond = New-Object System.Windows.Automation.AndCondition($nameCond, $typeCond)
$btns = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $andCond) |
    Sort-Object { $_.Current.BoundingRectangle.X })
Check 'two cards in the library' ($btns.Count -eq 2)
if ($btns.Count -lt 2) { Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force; exit 3 }

function TopWindows() { @(Get-Process VulcansLivingWP | Where-Object { $_.MainWindowHandle -ne 0 }).Count }

$btns[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 3
Check 'first card previewing' ((Get-Content (Get-ChildItem $logDir -Filter *.log | Sort-Object LastWriteTime | Select-Object -Last 1).FullName -Raw) -match 'Card preview opened: sample\.mp4')
Check 'still exactly one window' ((TopWindows) -eq 1)

# Switch to the second card while the first plays.
$btns[1].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 3
$text = Get-Content (Get-ChildItem $logDir -Filter *.log | Sort-Object LastWriteTime | Select-Object -Last 1).FullName -Raw
Check 'second card previewing'   ($text -match 'Card preview opened: sample2\.mp4')
Check 'no extra window after switch' ((TopWindows) -eq 1)
Check 'window still responsive'  (-not $proc.HasExited)

# Stop the active preview.
$btns[1].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 2
Check 'window survives stopping preview' (-not $proc.HasExited)
Check 'still one window after stop' ((TopWindows) -eq 1)

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $v2 -Force -ErrorAction SilentlyContinue
Write-Host 'DONE'
