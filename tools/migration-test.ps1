# Verifies the one-time upgrade migration from %LOCALAPPDATA%\FluxWall (1.0.x)
# to %LOCALAPPDATA%\VulcansLivingWP. The old folder must survive untouched.
$ErrorActionPreference = 'Stop'

$exe = 'C:\pwallpaper\src\FluxWall\bin\Release\net48\VulcansLivingWP.exe'
$video = 'C:\pwallpaper\test-videos\sample.mp4'
$new = Join-Path $env:LOCALAPPDATA 'VulcansLivingWP'
$old = Join-Path $env:LOCALAPPDATA 'FluxWall'

Get-Process VulcansLivingWP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
foreach ($d in @($new, $old)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }

# --- build a fake 1.0 profile ---------------------------------------------
New-Item -ItemType Directory -Force -Path (Join-Path $old 'thumbs') | Out-Null
@"
<?xml version="1.0" encoding="utf-8"?>
<VulcansLivingWP-Settings>
  <StartMinimized>true</StartMinimized>
  <ShowTrayIcon>true</ShowTrayIcon>
  <FirstRunDone>true</FirstRunDone>
  <TargetMonitors>Primary</TargetMonitors>
  <Scaling>Fit</Scaling>
  <PauseHotkey>Ctrl+Alt+W</PauseHotkey>
  <ActiveWallpaperId>legacy-item</ActiveWallpaperId>
</VulcansLivingWP-Settings>
"@ | Set-Content (Join-Path $old 'settings.xml') -Encoding UTF8

@"
<?xml version="1.0" encoding="utf-8"?>
<ArrayOfWallpaperItem>
  <WallpaperItem>
    <Id>legacy-item</Id>
    <FilePath>$($video.Replace('\','\'))</FilePath>
    <AddedAt>2026-01-02T03:04:05</AddedAt>
  </WallpaperItem>
</ArrayOfWallpaperItem>
"@ | Set-Content (Join-Path $old 'library.xml') -Encoding UTF8

Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 32, 18
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::Teal); $g.Dispose()
$bmp.Save((Join-Path $old 'thumbs\legacy.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

# --- run the app briefly ---------------------------------------------------
$p = Start-Process -FilePath $exe -ArgumentList '--autostop=6' -PassThru
$p.WaitForExit(30000) | Out-Null
Start-Sleep -Milliseconds 500

function Check($label, $cond) { Write-Host (("{0,-46} {1}" -f $label, $(if ($cond) { 'PASS' } else { 'FAIL' }))) }

Check 'legacy settings.xml copied'        (Test-Path (Join-Path $new 'VulcansLivingWP-settings.xml'))
Check 'legacy library.xml copied'         (Test-Path (Join-Path $new 'VulcansLivingWP-library.xml'))
Check 'legacy thumbnail copied'           (Test-Path (Join-Path $new 'thumbs\legacy.png'))
Check 'old folder still present'          (Test-Path (Join-Path $old 'settings.xml'))
Check 'old thumbnail still present'       (Test-Path (Join-Path $old 'thumbs\legacy.png'))

$s = Get-Content (Join-Path $new 'VulcansLivingWP-settings.xml') -Raw
Write-Host ('migrated Scaling=' + ([regex]::Match($s, '<Scaling>(.*?)</Scaling>').Groups[1].Value))

$log = Get-ChildItem (Join-Path $new 'logs') -Filter '*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
$migrated = Select-String -Path $log.FullName -Pattern 'Migrated settings' -SimpleMatch
Check 'log records the migration'         ($null -ne $migrated)

# Second start must not migrate again / must not disturb the new profile.
$before = (Get-Item (Join-Path $new 'VulcansLivingWP-settings.xml')).LastWriteTime
$p2 = Start-Process -FilePath $exe -ArgumentList '--autostop=5' -PassThru
$p2.WaitForExit(30000) | Out-Null
Start-Sleep -Milliseconds 500
$after = (Get-Item (Join-Path $new 'VulcansLivingWP-settings.xml')).LastWriteTime
Check 'second start keeps the profile'    ($null -ne $after)

# Cleanup: keep the new profile (it is the app's real data folder now).
Remove-Item $old -Recurse -Force
