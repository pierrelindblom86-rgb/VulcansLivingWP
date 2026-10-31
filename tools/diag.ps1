$p = Get-Process FluxWall -ErrorAction SilentlyContinue
if ($p) {
  foreach ($proc in $p) {
    $ram = [math]::Round($proc.WorkingSet64/1MB, 1)
    $cpu = [math]::Round($proc.TotalProcessorTime.TotalSeconds, 1)
    Write-Host ("RUNNING pid=" + $proc.Id + " RAM=" + $ram + "MB CPU=" + $cpu + "s handles=" + $proc.HandleCount)
  }
} else { Write-Host "RUNNING=no" }

$dir = Join-Path $env:LOCALAPPDATA "FluxWall"
Write-Host ("AppData: " + (Test-Path $dir))
$log = Join-Path $dir "logs"
if (Test-Path $dir) {
  Get-ChildItem $dir -Recurse | ForEach-Object { Write-Host ("  " + $_.FullName.Replace($dir,'') + "  " + $_.Length + "B") }
}
$latest = Get-ChildItem $log -Filter *.log -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($latest) {
  Write-Host "--- log tail ---"
  Get-Content $latest.FullName -Tail 40
}
