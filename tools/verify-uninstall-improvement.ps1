$ErrorActionPreference = 'Stop'

$setup = "C:\pwallpaper\installer\out\VulcansLivingWP_Setup.exe"
$dir = "C:\pwallpaper\tmp\uninstall-improvement\installed"
$root = "C:\pwallpaper\tmp\uninstall-improvement"
$video = "C:\pwallpaper\test-videos\sample.mp4"

New-Item -Path $root -ItemType Directory -Force | Out-Null

Write-Host '--- install ---'
$installProc = Start-Process $setup -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/MERGETASKS=!trayicon","/DIR=$dir","/LOG=$root\install.log" -Wait -PassThru
Write-Host "install_exit=$($installProc.ExitCode)"

Write-Host '--- installed files ---'
Get-ChildItem -Path $dir -File | ForEach-Object { Write-Host "installed_file=$($_.Name) size=$($_.Length)" }

Write-Host '--- launch app with wallpaper ---'
$exe = Join-Path $dir "FluxWall.exe"
$appProc = Start-Process $exe -ArgumentList "--applywall=$video" -PassThru
Write-Host "app_pid=$($appProc.Id)"
Start-Sleep -Seconds 8
$app = Get-Process -Id $appProc.Id -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0
if ($app) {
    Write-Host "app_running_before_uninstall=True"
    Write-Host "app_mainwindowhandle=$($app.MainWindowHandle)"
} else {
    Write-Host "app_not_running_before_uninstall"
}

Write-Host '--- screenshot before uninstall ---'
Add-Type -AssemblyName System.Windows.Forms
$bmp = New-Object System.Drawing.Bitmap(1280,720)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0,0,0,0,(New-Object System.Drawing.Size(1280,720)))
$bmp.Save("$root\before-uninstall.png")
$g.Dispose()
Write-Host "screenshot_saved_before=True"

Write-Host '--- uninstall while app running ---'
$uninst = Join-Path $dir "unins000.exe"
$uninstallProc = Start-Process $uninst -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/LOG=$root\uninstall.log" -Wait -PassThru
Write-Host "uninstall_exit=$($uninstallProc.ExitCode)"

Write-Host '--- screenshot after uninstall ---'
$bmp2 = New-Object System.Drawing.Bitmap(1280,720)
$g2 = [System.Drawing.Graphics]::FromImage($bmp2)
$g2.CopyFromScreen(0,0,0,0,(New-Object System.Drawing.Size(1280,720)))
$bmp2.Save("$root\after-uninstall.png")
$g2.Dispose()
Write-Host "screenshot_saved_after=True"

Write-Host '--- verify app gone after uninstall ---'
if (Get-Process FluxWall -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0) {
    Write-Host "app_still_running_right_after_uninstall=True"
} else {
    Write-Host "app_gone_right_after_uninstall=True"
}

Write-Host '--- retry file cleanup for 12 seconds ---'
$elapsed = 0
while ($elapsed -lt 12000) {
    if (-not (Test-Path $exe)) { break }
    Start-Sleep -Milliseconds 1000
    $elapsed += 1000
}
if (Test-Path $exe) {
    Write-Host "exe_still_present_after_retry=True"
} else {
    Write-Host "exe_gone_after_retry=True"
}

Write-Host '--- verify app dir removed after retry ---'
if (Test-Path $dir) {
    Write-Host "app_dir_not_removed_after_retry=True"
    Get-ChildItem -Path $dir -File | ForEach-Object { Write-Host "remaining_file=$($_.Name)" }
} else {
    Write-Host "app_dir_removed_after_retry=True"
}

Write-Host '--- verify test video intact ---'
if (Test-Path $video) {
    Write-Host "test_video_intact=True"
} else {
    Write-Host "test_video_missing"
}
