param(
    [Parameter(Mandatory=$true)]
    [string]$InstallDir
)

$ErrorActionPreference = 'Stop'

Write-Host "install_dir=$InstallDir"
Get-ChildItem -Path $InstallDir -File | ForEach-Object {
    Write-Host "file=$($_.Name) size=$($_.Length)"
}

$uninst = Join-Path $InstallDir 'unins000.exe'
if (-not (Test-Path $uninst)) {
    Write-Host "uninstaller_missing"
} else {
    $fv = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($uninst)
    Write-Host "uninstaller_productName=$($fv.ProductName)"
    Write-Host "uninstaller_productVersion=$($fv.ProductVersion)"
    Write-Host "uninstaller_fileDescription=$($fv.FileDescription)"
    Write-Host "uninstaller_exists=True"
}

$uninstKeyPattern = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
$found = Get-Item $uninstKeyPattern -ErrorAction SilentlyContinue | Where-Object {
        $_.GetValue('ProductName') -match 'VulcansLivingWP'
    }
if ($found) {
    foreach ($k in $found) {
        Write-Host "uninstall_key=yes DisplayName=$($k.DisplayName) DisplayVersion=$($k.DisplayVersion) InstallLocation=$($k.InstallLocation) Publisher=$($k.Publisher) UninstallString=$($k.UninstallString) KeyName=$($k.PSParentPath)"
    }
} else {
    Write-Host "uninstall_key=no"
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty $runKey -Name VulcansLivingWP -ErrorAction SilentlyContinue) {
    Write-Host "run_key=VulcansLivingWP present"
} else {
    Write-Host "run_key=VulcansLivingWP absent"
}

$sources = @(
    'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\VulcansLivingWP',
    'C:\Users\Public\Desktop\VulcansLivingWP.lnk'
)
foreach ($s in $sources) {
    Get-ChildItem -Path $s -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "shortcut=$($_.FullName)"
    }
}
