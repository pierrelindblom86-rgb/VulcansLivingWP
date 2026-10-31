$ErrorActionPreference = 'Stop'
$exe = 'C:\pwallpaper\installer\out\VulcansLivingWP_Setup.exe'
$dir = 'C:\pwallpaper\tmp\roi-plain'
Get-ChildItem $dir -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
New-Item -Path $dir -ItemType Directory -Force | Out-Null
$log = Join-Path $dir 'install.log'
$args = @("/SUPPRESSMSGBOXES","/NORESTART","/DIR=" + $dir,"/LOG=" + $log)
Write-Host "starting installer with args: $($args -join ' ')"
$p = Start-Process -FilePath $exe -ArgumentList $args -PassThru
Write-Host "pid=$($p.Id) exit=$($p.ExitCode)"

for ($i = 0; $i -lt 60; ++$i) {
    Start-Sleep -Seconds 1
    if (Test-Path $log) { Write-Host "log_present_tick=$(($i+1))s"; break }
    if (Test-Path (Join-Path $dir 'FluxWall.exe')) { Write-Host "exe_present_tick=$(($i+1))s"; break }
}

Write-Host "--- dir listing ---"
Get-ChildItem $dir -Recurse | Where-Object { -not $_.Name.EndsWith('-del.ico') -and -not $_.Name.EndsWith('-del.dat') } | ForEach-Object { Write-Host ($_.FullName + ' ' + $_.Length + ' ' + $_.Extension) }
if (Test-Path $log) {
    Write-Host '--- install.log ---'
    Get-Content $log -Tail 200
} else {
    Write-Host 'no install log present'
}
