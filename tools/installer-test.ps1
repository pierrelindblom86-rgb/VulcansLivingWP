$ErrorActionPreference = 'Stop'
$setup = 'C:\pwallpaper\installer\out\FluxWallSetup-1.0.0.exe'
$dir   = 'C:\pwallpaper\test-install'
$video = 'C:\pwallpaper\test-videos\sample.mp4'
$videoBefore = (Get-Item $video).Length

Get-Process FluxWall -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }

$i = Start-Process $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/MERGETASKS=!autostart',"/DIR=$dir") -Wait -PassThru
Write-Host "install_exit=$($i.ExitCode)"
$exe = Join-Path $dir 'FluxWall.exe'
Write-Host "installed_exe_exists=$(Test-Path $exe)"
if (Test-Path $exe) { Write-Host "installed_exe_bytes=$((Get-Item $exe).Length)" }
Write-Host "uninstaller_exists=$(Test-Path (Join-Path $dir 'unins000.exe'))"
Write-Host "autostart_entry_absent=$($null -eq (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name FluxWall -ErrorAction SilentlyContinue))"

$u = Start-Process (Join-Path $dir 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
Write-Host "uninstall_exit=$($u.ExitCode)"
Write-Host "exe_removed=$(-not (Test-Path $exe))"
Write-Host "install_dir_removed=$(-not (Test-Path $dir))"
Write-Host "video_intact=$((Test-Path $video) -and ((Get-Item $video).Length -eq $videoBefore))"
