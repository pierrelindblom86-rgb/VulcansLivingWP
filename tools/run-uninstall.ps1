$ErrorActionPreference = 'Stop'

$appDir = 'C:\pwallpaper\tmp-install\app'
$uninst = Join-Path $appDir 'unins000.exe'

$attempts = 0
$max = 30
$uninstLocated = $false
while ($attempts -lt $max) {
    if (Test-Path $uninst) {
        $uninstLocated = $true
        break
    }
    Start-Sleep -Milliseconds 1000
    $attempts++
}

if (-not $uninstLocated) {
    Write-Host 'uninstaller_missing'
    if (-not (Test-Path $appDir)) {
        Write-Host 'install_dir_missing'
    } else {
        Write-Host 'install_dir_present_but_uninstaller_missing'
        Get-ChildItem -Path $appDir -File -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "unexpected_file=$($_.Name)"
        }
    }
    exit 2
}

Write-Host "uninstaller_found=True path=$uninst"
$u = Start-Process $uninst -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
Write-Host "uninstall_exit=$($u.ExitCode)"

if (-not (Test-Path $appDir)) {
    Write-Host 'install_dir_still_exists=False'
} else {
    Write-Host 'install_dir_still_exists=True'
    Get-ChildItem -Path $appDir -File -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "remaining_file=$($_.Name)"
    }
}

$exe = Join-Path $appDir 'FluxWall.exe'
Write-Host "exe_removed=$(-not (Test-Path $exe))"

$readme = Join-Path $appDir 'README.md'
Write-Host "readme_removed=$(-not (Test-Path $readme))"

$uninstKeys = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D9A6C9EA-7B4F-4D9E-8B6A-0F17E4B7E58A}_is1',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D9A6C9EA-7B4F-4D9E-8B6A-0F17E4B7E58A}_is1'
)
$keyStillPresent = $false
foreach ($k in $uninstKeys) {
    if (Test-Path $k) {
        Write-Host 'uninstall_key_still_present=True path=' + $k
        $keyStillPresent = $true
    }
}
if (-not $keyStillPresent) {
    Write-Host 'uninstall_key_still_present=False'
}

$sm = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\VulcansLivingWP'
if (-not (Test-Path $sm)) {
    Write-Host 'startmenu_folder_still_exists=False'
} else {
    Write-Host 'startmenu_folder_still_exists=True'
    Get-ChildItem -Path $sm -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "shortcut_remaining=$($_.Name)"
    }
}

$publicDesktop = 'C:\Users\Public\Desktop\VulcansLivingWP.lnk'
Write-Host "desktop_shortcut_removed=$(-not (Test-Path $publicDesktop))"
