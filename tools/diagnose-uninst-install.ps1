$ErrorActionPreference = 'Stop'

$appDir = 'C:\pwallpaper\tmp-install\app'
$uninstExe = Join-Path $appDir 'unins000.exe'
$logDir = 'C:\pwallpaper\tmp-install'
$uninstLog = Join-Path $logDir 'uninstall-diagnostic.log'

function Get-AppFiles {
    Get-ChildItem -Path $appDir -File -ErrorAction SilentlyContinue |
        Select-Object Name, Length, LastWriteTime |
        ForEach-Object { "$($_.Name) $($_.Length) $($_.LastWriteTime)" }
}

function Report-AppDir {
    if (-not (Test-Path $appDir)) {
        Write-Host 'app_dir_absent'
        return
    }
    Write-Host 'app_dir_present'
    Get-ChildItem -Path $appDir -File -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "file=$($_.Name) size=$($_.Length)"
    }
}

# Ensure a clean state
if (Test-Path $appDir) {
    Remove-Item $appDir -Recurse -Force
}

Write-Host '--- Phase 1: verbose uninstall (if any) ---'
if (Test-Path $uninstExe) {
    Write-Host 'running existing uninstaller'
    $u = Start-Process $uninstExe -ArgumentList @('/LOG=$uninstLog','/VERBOSE','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
    Write-Host "uninstaller_exit=$($u.ExitCode)"
} else {
    Write-Host 'no existing uninstaller present'
}

Write-Host '--- Phase 2: app dir after uninstall ---'
Report-AppDir
Get-ChildItem -Path $logDir -File | Where-Object { $_.Name -like 'unins*' } | ForEach-Object {
    Write-Host "uninstall_log_candidate=$($_.Name) size=$($_.Length)"
}

# Now install
Write-Host '--- Phase 3: install ---'
$setup = 'C:\pwallpaper\installer\out\VulcansLivingWP_Setup.exe'
$setupLog = Join-Path $logDir 'install-diagnostic.log'
$i = Start-Process $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/MERGETASKS=!trayicon',"/DIR=$appDir","/LOG=$setupLog") -Wait -PassThru
Write-Host "installer_exit=$($i.ExitCode)"

Write-Host '--- Phase 4: app dir immediately after install ---'
Report-AppDir

Write-Host '--- Phase 5: poll uninstaller and app dir ---'
$uninstLocated = $false
$elapsed = 0
$lastCount = -1
while ($elapsed -lt 120000) {
    $exists = Test-Path $uninstExe
    $files = @()
    if (Test-Path $appDir) {
        $files = Get-ChildItem -Path $appDir -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
    }
    $count = $files.Count
    if ($elapsed % 10000 -eq 0) {
        Write-Host "poll at $elapsed ms uninstaller_exists=$exists file_count=$count"
        foreach ($f in $files) { Write-Host "file=$f" }
    }
    if ($exists) {
        if (-not $uninstLocated) { $uninstLocated = $true }
    } elseif ($uninstLocated) {
        Write-Host "uninstaller_gone_at_$elapsed_ms file_count_after=$count"
        if ($count -eq 0) {
            Write-Host "app_dir_empty_after_uninstaller_removed"
        }
        break
    }
    if ($count -ne $lastCount) {
        Write-Host "file_count_changed from $lastCount to $count at $elapsed ms"
        $lastCount = $count
    }
    Start-Sleep -Milliseconds 2000
    $elapsed += 2000
}
if ($uninstLocated) {
    Write-Host "uninstaller_present_through_poll"
} else {
    Write-Host "uninstaller_never_seen"
}

Write-Host '--- Phase 6: uninstaller existence after install ---'
Get-ChildItem -Path $appDir -File -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "after_install_file=$($_.Name) size=$($_.Length)"
}
