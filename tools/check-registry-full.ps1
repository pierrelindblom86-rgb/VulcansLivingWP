$ErrorActionPreference = 'Stop'

$roots = @('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
           'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
           'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')

foreach ($root in $roots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem -Path $root -ErrorAction SilentlyContinue | ForEach-Object {
        $key = $_
        $productName = $key.GetValue('ProductName')
        $displayName = $key.GetValue('DisplayName')
        $any = $productName -or $displayName
        if ($any -and ($productName -match 'VulcansLivingWP' -or $productName -match 'FluxWall' -or
                        $displayName -match 'VulcansLivingWP' -or $displayName -match 'FluxWall')) {
            Write-Host "MATCH: Path=$($key.PSPath)"
            Write-Host "  ProductName=$productName"
            Write-Host "  DisplayName=$displayName"
            Write-Host "  DisplayVersion=$($key.GetValue('DisplayVersion'))"
            Write-Host "  Publisher=$($key.GetValue('Publisher'))"
            Write-Host "  InstallLocation=$($key.GetValue('InstallLocation'))"
            Write-Host "  UninstallString=$($key.GetValue('UninstallString'))"
            Write-Host "  DisplayIcon=$($key.GetValue('DisplayIcon'))"
            Write-Host "  EstimatedSize=$($key.GetValue('EstimatedSize'))"
            Write-Host "  NoModify=$($key.GetValue('NoModify'))"
            Write-Host "  NoRepair=$($key.GetValue('NoRepair'))"
            Write-Host "  SystemComponent=$($key.GetValue('SystemComponent'))"
            Write-Host "  Version=$($key.GetValue('Version'))"
            Write-Host "  InstallSource=$($key.GetValue('InstallSource'))"
            Write-Host "  InstallDate=$($key.GetValue('InstallDate'))"
            Write-Host "  URLInfoAbout=$($key.GetValue('URLInfoAbout'))"
            Write-Host "  URLUpdateInfo=$($key.GetValue('URLUpdateInfo'))"
            foreach ($v in $key.Property) {
                Write-Host "  [$v]=$($key.GetValue($v))"
            }
        }
    }
}

Write-Host '--- search for any key with DisplayVersion 1.0.1 ---'
foreach ($root in $roots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem -Path $root -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.GetValue('DisplayVersion') -eq '1.0.1') {
            Write-Host "VERSION-MATCH: Path=$($_.PSPath)"
            Write-Host "  ProductName=$($_.GetValue('ProductName'))"
            Write-Host "  DisplayName=$($_.GetValue('DisplayName'))"
            foreach ($v in $_.Property) {
                Write-Host "  [$v]=$($_.GetValue($v))"
            }
        }
    }
}

Write-Host '--- raw list of uninstall keys with visible DisplayName ---'
foreach ($root in $roots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem -Path $root -ErrorAction SilentlyContinue | ForEach-Object {
        $dn = $_.GetValue('DisplayName')
        if ($dn) {
            Write-Host "  DisplayName=[$dn] Path=$($_.PSPath)"
        }
    }
}
