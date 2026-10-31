param(
    [string]$Setup = 'C:\pwallpaper\installer\out\VulcansLivingWP_Setup.exe',
    [string]$Video = 'C:\pwallpaper\test-videos\sample.mp4'
)
$ErrorActionPreference = 'Continue'

$appData = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP'
$backup = 'C:\pwallpaper\tmp\appdata-backup'
if (Test-Path $appData) {
    if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
    Copy-Item $appData $backup -Recurse -Force
    Write-Host ("backed_up_appdata=" + $backup)
}

function KillAll {
    Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

function RunKeyValue {
    $v = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'VulcansLivingWP' -ErrorAction SilentlyContinue)
    if ($v) { return $v.VulcansLivingWP } else { return '<none>' }
}

function InstallTo([string]$dir) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
    $p = Start-Process -FilePath $Setup -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER',
        ('/DIR=' + $dir), '/TASKS="autostart"'
    ) -PassThru -Wait
    Write-Host ("install_exit=" + $p.ExitCode)
    Start-Sleep -Seconds 1
}

Write-Host '=== scenario A: uninstall while the app is closed ==='
$dirA = 'C:\pwallpaper\tmp\instA'
InstallTo $dirA
Write-Host ("installed_exe=" + (Test-Path (Join-Path $dirA 'VulcansLivingWP.exe')))
Write-Host ("installed_readme=" + (Test-Path (Join-Path $dirA 'README.md')))
Write-Host ("run_key=" + (RunKeyValue))
Write-Host ("startmenu_shortcut=" + (Test-Path (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\VulcansLivingWP\VulcansLivingWP.lnk')))

$u = Join-Path $dirA 'unins000.exe'
$p = Start-Process -FilePath $u -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -PassThru -Wait
Write-Host ("uninstall_exit=" + $p.ExitCode)
Start-Sleep -Seconds 2
Write-Host ("dir_gone=" + (-not (Test-Path $dirA)))
Write-Host ("run_key_after=" + (RunKeyValue))
Write-Host ("shortcut_gone=" + (-not (Test-Path (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\VulcansLivingWP'))))
Write-Host ("video_still_there=" + (Test-Path $Video))

Write-Host ''
Write-Host '=== scenario B: uninstall while wallpaper is playing (window open) ==='
$dirB = 'C:\pwallpaper\tmp\instB'
InstallTo $dirB
$exeB = Join-Path $dirB 'VulcansLivingWP.exe'
$app = Start-Process -FilePath $exeB -ArgumentList ('--applywall=' + $Video) -PassThru
Start-Sleep -Seconds 9
Write-Host ("app_running=" + [bool](Get-Process -Id $app.Id -ErrorAction SilentlyContinue))
Write-Host ("main_window=" + ((Get-Process -Id $app.Id).MainWindowHandle -ne 0))

$u = Join-Path $dirB 'unins000.exe'
$uproc = Start-Process -FilePath $u -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -PassThru
$sawNewWindow = $false
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Milliseconds 250
    foreach ($q in @(Get-Process VulcansLivingWP -ErrorAction SilentlyContinue)) {
        if ($q.MainWindowHandle -ne 0 -and $q.Id -ne $app.Id) { $sawNewWindow = $true }
    }
    if ($uproc.HasExited) { break }
}
$uproc.WaitForExit()
Write-Host ("uninstall_exit=" + $uproc.ExitCode)
Write-Host ("extra_app_window_during_uninstall=" + $sawNewWindow)
Start-Sleep -Seconds 2
Write-Host ("app_gone=" + (-not (Get-Process -Id $app.Id -ErrorAction SilentlyContinue)))
Write-Host ("dir_gone=" + (-not (Test-Path $dirB)))
Write-Host ("video_still_there=" + (Test-Path $Video))

Write-Host ''
Write-Host '=== scenario C: uninstall while minimised to tray ==='
$dirC = 'C:\pwallpaper\tmp\instC'
InstallTo $dirC
$exeC = Join-Path $dirC 'VulcansLivingWP.exe'
$app2 = Start-Process -FilePath $exeC -ArgumentList ('--applywall=' + $Video), '--minimized' -PassThru
Start-Sleep -Seconds 9
Write-Host ("app_running=" + [bool](Get-Process -Id $app2.Id -ErrorAction SilentlyContinue))
Write-Host ("window_hidden=" + (((Get-Process -Id $app2.Id -ErrorAction SilentlyContinue).MainWindowHandle -eq 0)))

$u = Join-Path $dirC 'unins000.exe'
$uproc2 = Start-Process -FilePath $u -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -PassThru
$sawNewWindow2 = $false
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Milliseconds 250
    foreach ($q in @(Get-Process VulcansLivingWP -ErrorAction SilentlyContinue)) {
        if ($q.MainWindowHandle -ne 0 -and $q.Id -ne $app2.Id) { $sawNewWindow2 = $true }
    }
    if ($uproc2.HasExited) { break }
}
$uproc2.WaitForExit()
Write-Host ("uninstall_exit=" + $uproc2.ExitCode)
Write-Host ("extra_app_window_during_uninstall=" + $sawNewWindow2)
Start-Sleep -Seconds 2
Write-Host ("app_gone=" + (-not (Get-Process -Id $app2.Id -ErrorAction SilentlyContinue)))
Write-Host ("dir_gone=" + (-not (Test-Path $dirC)))
Write-Host ("video_still_there=" + (Test-Path $Video))

KillAll
Write-Host 'DONE'
