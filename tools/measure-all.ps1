# Full measurement cycle in ONE host process so nothing can kill the app
# between samples. Launches FluxWall with the test wallpaper, measures:
#   state 1 = playing normally
#   state 2 = our fullscreen test window is up (auto-pause kicks in)
#   state 3 = fullscreen window closed (playback resumes)
# then stops the app.
$ErrorActionPreference = 'Continue'
$tools = $PSScriptRoot
$root  = Split-Path -Parent $tools
$exe   = Join-Path $root 'src\FluxWall\bin\Release\net48\FluxWall.exe'
$video = Join-Path $root 'test-videos\sample.mp4'

function Measure-State([string]$label, [int]$seconds) {
    $procs = @(Get-Process FluxWall -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { Write-Host "MEASURE[$label] NOT-RUNNING"; return }
    $pid0 = $procs[0].Id
    $c1 = $procs[0].TotalProcessorTime.TotalMilliseconds
    $wsMax = 0.0; $privMax = 0.0
    $nSamples = [math]::Max(1, [int]($seconds / 3))
    for ($i = 0; $i -lt $nSamples; $i++) {
        Start-Sleep -Seconds 3
        $p2 = Get-Process -Id $pid0 -ErrorAction SilentlyContinue
        if (-not $p2) { Write-Host "MEASURE[$label] process exited"; return }
        $wsMax = [math]::Max($wsMax, $p2.WorkingSet64 / 1MB)
        $privMax = [math]::Max($privMax, $p2.PrivateMemorySize64 / 1MB)
    }
    $p = Get-Process -Id $pid0
    $c2 = $p.TotalProcessorTime.TotalMilliseconds
    $cpuPct = ($c2 - $c1) / ($nSamples * 3000.0) * 100.0
    Write-Host ("MEASURE[$label] pid=$pid0 RAM(max)=" + [math]::Round($wsMax,1) +
        "MB Private=" + [math]::Round($privMax,1) + "MB CPU=" + [math]::Round($cpuPct,2) + "%")
}

# ---------------- start from a clean slate ----------------
Get-Process FluxWall -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host "--- Launching FluxWall with test video ---"
Start-Process -FilePath $exe -ArgumentList @("--applywall=$video", '--tray') -PassThru | Out-Null
Start-Sleep -Seconds 10

Measure-State 'playing' 30

Write-Host "--- Opening fullscreen test window ---"
$fs = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -ArgumentList @('-STA','-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $tools 'fs-window.ps1')) `
    -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 8

Measure-State 'fullscreen-paused' 30

Write-Host "--- Closing fullscreen window ---"
Stop-Process -Id $fs.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 6

Measure-State 'resumed' 30

Write-Host "--- Stopping FluxWall ---"
Get-Process FluxWall -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host 'DONE'
