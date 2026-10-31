$ErrorActionPreference = 'Stop'

$silent = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /MERGETASKS=!trayicon'
$dir    = 'C:\pwallpaper\tmp-install\app'
$log    = 'C:\pwallpaper\tmp-install\install.log'

if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
if (Test-Path $log) { Remove-Item $log -Force }
New-Item -Path $dir -ItemType Directory -Force | Out-Null

Write-Host "start_install_dir=$dir"
Write-Host "installer=C:\pwallpaper\installer\out\VulcansLivingWP_Setup.exe"
Write-Host "install_exit_before_run_unknown"

$i = Start-Process 'C:\pwallpaper\installer\out\VulcansLivingWP_Setup.exe' -ArgumentList @($silent, "/DIR=$dir", "/LOG=$log") -Wait -PassThru
Write-Host "install_exit=$($i.ExitCode)"

Write-Host "--- files under install dir ---"
Get-ChildItem -Path $dir -Recurse -File | ForEach-Object {
    Write-Host "file=$($_.FullName) size=$($_.Length)"
}
Write-Host "exe_exists=$(Test-Path (Join-Path $dir 'FluxWall.exe'))"
Write-Host "uninstaller_exists=$(Test-Path (Join-Path $dir 'unins000.exe'))"
Write-Host "readme_exists=$(Test-Path (Join-Path $dir 'README.md'))"
Write-Host "config_exists=$(Test-Path (Join-Path $dir 'FluxWall.exe.config'))"
if (Test-Path $log) {
    Write-Host "--- install log ---"
    Get-Content $log | ForEach-Object { Write-Host $_ }
}
