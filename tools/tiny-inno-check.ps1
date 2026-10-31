param(
    [string]$Exe = 'C:\pwallpaper\installer\out\VulcansLivingWP_Setup.exe',
    [string]$Dir = 'C:\pwallpaper\tmp\roi-plain',
    [int]$Timeout = 120
)
$ErrorActionPreference = 'Stop'
Get-ChildItem $Dir -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
New-Item -Path $Dir -ItemType Directory -Force | Out-Null

Write-Host "[probe] exe=$Exe"
Write-Host "[probe] dir=$Dir"

$p = Start-Process -FilePath $Exe -ArgumentList "/SUPPRESSMSGBOXES","/NORESTART","/DIR=$Dir","/LOG=$Dir\install.log" -PassThru
Write-Host "[probe] pid=$($p.Id)"
Start-Sleep -Seconds 1
Write-Host "[probe] exit=$($p.ExitCode)"

for ($i = 0; $i -lt $Timeout; ++$i) {
    Start-Sleep -Seconds 1
    if (Test-Path (Join-Path $Dir 'FluxWall.exe')) {
        Write-Host "[probe] exe_present_tick=$(($i+1))s"
        break
    }
    if (Test-Path (Join-Path $Dir 'install.log')) {
        Write-Host "[probe] log_present_tick=$(($i+1))s"
        break
    }
    if (-not (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)) {
        Write-Host "[probe] installer_process_gone_tick=$(($i+1))s"
    }
}

Write-Host "[probe] --- dir listing ---"
if (Test-Path $Dir) {
    Get-ChildItem $Dir -Recurse -ErrorAction SilentlyContinue | Where-Object { -not $_.Name.EndsWith('-del.ico') -and -not $_.Name.EndsWith('-del.dat') } | ForEach-Object { Write-Host ($_.FullName + ' ' + $_.Length + ' ' + $_.Extension) }
} else {
    Write-Host "[probe] dir_absent"
}

if (Test-Path (Join-Path $Dir 'install.log')) {
    Write-Host "[probe] --- install.log ---"
    Get-Content (Join-Path $Dir 'install.log') -Tail 200
} else {
    Write-Host "[probe] no install log present"
}
