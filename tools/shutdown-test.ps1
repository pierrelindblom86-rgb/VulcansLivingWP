$ErrorActionPreference = 'Continue'
$exe = 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe'
$video = 'C:\pwallpaper\test-videos\sample.mp4'

function AppProcesses { @(Get-Process FluxWall -ErrorAction SilentlyContinue) }

Write-Host '=== case 1: nothing running ==='
Get-Process FluxWall -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $exe -ArgumentList '--shutdown' -PassThru -Wait
$sw.Stop()
Write-Host ("noop_exit=" + $p.ExitCode + " elapsed_ms=" + $sw.ElapsedMilliseconds)

Write-Host '=== case 2: app running with wallpaper ==='
$app = Start-Process -FilePath $exe -ArgumentList ('--applywall=' + $video) -PassThru
Start-Sleep -Seconds 8
Write-Host ("app_pid=" + $app.Id + " alive=" + [bool](Get-Process -Id $app.Id -ErrorAction SilentlyContinue))
Write-Host ("processes_before=" + (AppProcesses).Count)

$windowsBefore = @(Get-Process FluxWall -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 }).Count
Write-Host ("main_windows_before=" + $windowsBefore)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$sp = Start-Process -FilePath $exe -ArgumentList '--shutdown' -PassThru -Wait
$sw.Stop()
Write-Host ("shutdown_exit=" + $sp.ExitCode + " elapsed_ms=" + $sw.ElapsedMilliseconds)

Start-Sleep -Milliseconds 800
$left = AppProcesses
Write-Host ("processes_after=" + $left.Count)
Write-Host ("app_still_alive=" + [bool](Get-Process -Id $app.Id -ErrorAction SilentlyContinue))
$windowsAfter = @(Get-Process FluxWall -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 }).Count
Write-Host ("main_windows_after=" + $windowsAfter)

Write-Host '=== case 3: --stopwall shows no window ==='
$sp2 = Start-Process -FilePath $exe -ArgumentList '--stopwall' -PassThru
$seenWindow = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 100
    if (Get-Process -Id $sp2.Id -ErrorAction SilentlyContinue) {
        foreach ($q in @(Get-Process FluxWall -ErrorAction SilentlyContinue)) {
            if ($q.MainWindowHandle -ne 0) { $seenWindow = $true }
        }
    }
    if ($sp2.HasExited) { break }
}
$sp2.WaitForExit()
Write-Host ("stopwall_exit=" + $sp2.ExitCode + " window_seen=" + $seenWindow)

$logDir = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP\logs'
$log = Get-ChildItem $logDir -Filter '*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
Write-Host '--- log tail ---'
Get-Content $log.FullName -Tail 14
Write-Host 'DONE'
