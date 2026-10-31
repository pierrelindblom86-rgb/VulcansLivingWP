param(
    [string]$InstallDir = 'C:\pwallpaper\tmp\install-run-test\app',
    [string]$Video1 = 'C:\pwallpaper\test-videos\sample.mp4',
    [string]$OutDir = 'C:\pwallpaper\tmp\install-run-test'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -Path $OutDir -ItemType Directory -Force | Out-Null
$exe = Join-Path $InstallDir 'FluxWall.exe'
$log = Join-Path $OutDir 'install.log'
$setup = 'C:\pwallpaper\installer\out\VulcansLivingWP_Setup.exe'
$installArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART', ('/DIR=' + $InstallDir), ('/LOG=' + $log))
Write-Host '--- install ---'
$install = Start-Process -FilePath $setup -ArgumentList $installArgs -Wait -PassThru
Write-Host ('install_exit=' + $install.ExitCode)
if ($install.ExitCode -ne 0) { Write-Host 'install_failed_exitcode'; return }
$attempt = 0
while (-not (Test-Path $InstallDir) -and $attempt -lt 20) { Start-Sleep -Milliseconds 500; $attempt += 1 }
if (-not (Test-Path $InstallDir)) { Write-Host 'install_dir_missing'; return }
$exeFound = $false
$attempt = 0
while (-not $exeFound -and $attempt -lt 20) { Start-Sleep -Milliseconds 500; $attempt += 1; if (Test-Path $exe) { $exeFound = $true } }
if (-not $exeFound) { Write-Host 'app_exe_missing'; return }
Get-ChildItem -Path $InstallDir -File | ForEach-Object { Write-Host ('installed_file=' + $_.Name + ' size=' + $_.Length) }
Write-Host '--- launch + wallpaper ---'
$proc = Start-Process -FilePath $exe -ArgumentList ('--applywall=' + $Video1) -PassThru
Write-Host ('app_pid=' + $proc.Id)
Start-Sleep -Seconds 14
$app = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0
if ($app) {
    Write-Host ('app_mainwindowhandle=' + $app.MainWindowHandle)
} else {
    Write-Host 'app_window_handle_missing'
}
$bmp = New-Object System.Drawing.Bitmap(1440,900)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0,0,0,0,(New-Object System.Drawing.Size(1440,900)))
$g.Dispose()
$bmp.Save((Join-Path $OutDir 'after-launch.png'))
$bmp.Dispose()
Write-Host 'screenshot_saved=True'
