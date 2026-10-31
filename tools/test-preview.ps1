param(
    [Parameter(Mandatory = $true)]
    [string]
    $InstallDir
)

$ErrorActionPreference = 'Stop'

$setup = 'C:\pwallpaper\installer\out\VulcansLivingWP_Setup.exe'
$video = 'C:\pwallpaper\test-videos\sample.mp4'
$installLog = Join-Path $InstallDir 'install.log'
$screenshotFile = Join-Path $InstallDir 'after-launch.png'

$ErrorActionPreference = 'Stop'

Write-Host '--- install ---'
$installArgs = @(
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    '/MERGETASKS=!trayicon',
    '/DIR=' + $InstallDir,
    '/LOG=' + $installLog
)
$install = Start-Process -FilePath $setup -ArgumentList $installArgs -Wait -PassThru
Write-Host "install_exit=$($install.ExitCode)"
if ($install.ExitCode -ne 0 -or -not (Test-Path $InstallDir)) {
    return
}

Get-ChildItem -Path $InstallDir -File | ForEach-Object { Write-Host "installed_file=$($_.Name) size=$($_.Length)" }

$exe = Join-Path $InstallDir 'FluxWall.exe'
Write-Host '--- launching app with wallpaper ---'
$launched = Start-Process -FilePath $exe -ArgumentList ('--applywall=' + $video) -PassThru
Write-Host "app_pid=$($launched.Id)"
Start-Sleep -Seconds 12

$app = Get-Process -Id $launched.Id -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0
if ($app) {
    Write-Host 'app_running=True'
    Write-Host ('app_mainwindowhandle=' + $app.MainWindowHandle)
} else {
    Write-Host 'app_not_running'
}

Write-Host '--- screenshot ---'
Add-Type -AssemblyName System.Windows.Forms
$bmp = New-Object System.Drawing.Bitmap(1280, 720)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size(1280, 720)))
$g.Dispose()
$bmp.Save($screenshotFile)
$bmp.Dispose()
Write-Host 'screenshot_saved=True'
