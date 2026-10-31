# Measures FluxWall RAM + CPU over a sample window.
$procs = Get-Process FluxWall -ErrorAction SilentlyContinue
if (-not $procs) { Write-Host "NOT-RUNNING"; exit }

$p = $procs[0]
$c1 = $p.TotalProcessorTime.TotalMilliseconds
$ws1 = $p.WorkingSet64 / 1MB
Start-Sleep -Seconds 10
$p.Refresh()
$c2 = $p.TotalProcessorTime.TotalMilliseconds
$ws2 = $p.WorkingSet64 / 1MB
$cpuPct = ($c2 - $c1) / 10000.0 * 100.0

$ram = [math]::Round($p.WorkingSet64 / 1MB, 1)
$priv = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
$cpu = [math]::Round($cpuPct, 2)
$label = $args[0]
Write-Host ("MEASURE[" + $label + "] RAM=" + $ram + "MB Private=" + $priv + "MB CPU=" + $cpu + "%")
