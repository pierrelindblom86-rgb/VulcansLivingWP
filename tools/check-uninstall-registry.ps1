$ErrorActionPreference = 'Stop'

$rootKeys = @('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall')
$found = @()
foreach ($root in $rootKeys) {
    $found += Get-Item -Path "$root\*" -ErrorAction SilentlyContinue |
        Where-Object { $_.GetValue('ProductName') -match 'VulcansLivingWP' }
}

if ($found) {
    foreach ($k in $found) {
        Write-Host 'uninstall_key_found=yes'
        Write-Host "ProductName=$($k.GetValue('ProductName'))"
        Write-Host "DisplayName=$($k.GetValue('DisplayName'))"
        Write-Host "DisplayVersion=$($k.GetValue('DisplayVersion'))"
        Write-Host "Publisher=$($k.GetValue('Publisher'))"
        Write-Host "InstallLocation=$($k.GetValue('InstallLocation'))"
        Write-Host "UninstallString=$($k.GetValue('UninstallString'))"
        Write-Host "DisplayIcon=$($k.GetValue('DisplayIcon'))"
        Write-Host "EstimatedSize=$($k.GetValue('EstimatedSize'))"
        Write-Host "RegistryPath=$($k.PSParentPath)"
        Write-Host "UninstallRegister:$($k.PSParentPath)"
    }
} else {
    Write-Host 'uninstall_key_found=no'
}

$uninstExe = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\VulcansLivingWP\Uninstall VulcansLivingWP.lnk'
if (Test-Path $uninstExe) {
    $ws = New-Object -ComObject WScript.Shell
    $target = $ws.CreateShortcut($uninstExe).TargetPath
    Write-Host "uninstaller_shortcut_target=$target"
} else {
    Write-Host "uninstaller_shortcut_missing"
}

$userStart = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\VulcansLivingWP'
if (Test-Path $userStart) {
    $items = Get-ChildItem -Path $userStart
    Write-Host "startmenu_folder_items=$($items.Count)"
} else {
    Write-Host "startmenu_folder_missing"
}

$desktopLnk = 'C:\Users\Public\Desktop\VulcansLivingWP.lnk'
if (Test-Path $desktopLnk) {
    Write-Host "desktop_shortcut_target=$(((New-Object -ComObject WScript.Shell).CreateShortcut($desktopLnk).TargetPath))"
} else {
    Write-Host "desktop_shortcut_missing"
}
