$ErrorActionPreference = 'Stop'

$name = ([char]0x81EA).ToString() + [char]0x542F + [char]0x5F00 + [char]0x5173
$desktop = [Environment]::GetFolderPath('Desktop')
$desktopExe = Join-Path $desktop ($name + '.exe')
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $desktopExe } | ForEach-Object {
    Stop-Process -Id $_.ProcessId -Force
}
Start-Sleep -Milliseconds 400

$dataDir = Join-Path $env:APPDATA 'TrayStartup'
$cfgPath = Join-Path $dataDir 'apps.json'
$backupDir = Join-Path $dataDir 'disabled-startup'
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
Copy-Item -LiteralPath $cfgPath -Destination (Join-Path $dataDir 'apps.json.before-residual-fix') -Force

$cfg = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
function Set-Prop($object, $property, $value) {
    if ($object.PSObject.Properties[$property]) {
        $object.PSObject.Properties[$property].Value = $value
    } else {
        $object | Add-Member -NotePropertyName $property -NotePropertyValue $value
    }
}
$lghub = $cfg.apps | Where-Object { $_.name -eq 'LGHUB' } | Select-Object -First 1
if ($lghub) {
    Set-Prop $lghub 'path' 'C:\Program Files\LGHUB\system_tray\lghub_system_tray.exe'
    Set-Prop $lghub 'arguments' '--minimized'
    Set-Prop $lghub 'registryName' 'LGHUB'
    Set-Prop $lghub 'adopted' $true
    Set-Prop $lghub 'otherStartup' ''
}
$cfg | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cfgPath -Encoding UTF8

$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runProps = Get-ItemProperty -Path $runPath -ErrorAction SilentlyContinue
if ($runProps -and $runProps.LGHUB) {
    Set-Content -LiteralPath (Join-Path $backupDir 'LGHUB-run-value.txt') -Value $runProps.LGHUB -Encoding UTF8
    Remove-ItemProperty -Path $runPath -Name 'LGHUB' -Force
}

$snipasteLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'Snipaste.lnk'
if (Test-Path $snipasteLink) {
    Move-Item -LiteralPath $snipasteLink -Destination (Join-Path $backupDir 'Snipaste.lnk') -Force
}

$batName = ([char]0x5173).ToString() + [char]0x95ED + 'Snipaste' + ([char]0x81EA).ToString() + [char]0x542F + '.bat'
$batPath = Join-Path $desktop $batName
$bat = @'
@echo off
schtasks /Change /TN "\Snipaste (Run As Admin) @57F0" /DISABLE
if errorlevel 1 (
    echo Failed. Right-click this file and choose "Run as administrator".
) else (
    echo Snipaste login task disabled.
)
pause
'@
Set-Content -LiteralPath $batPath -Value $bat -Encoding ASCII

$configOk = $false
try {
    $check = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $configOk = ($check.apps.Count -eq 3)
} catch {
    $configOk = $false
}

Write-Output ('ConfigBackup=' + (Join-Path $dataDir 'apps.json.before-residual-fix'))
Write-Output ('ConfigValid=' + $configOk)
Write-Output ('LGHUBRunPresent=' + [bool](Get-ItemProperty -Path $runPath -Name LGHUB -ErrorAction SilentlyContinue))
Write-Output ('SnipasteLinkPresent=' + (Test-Path $snipasteLink))
Write-Output ('SnipasteLinkBackup=' + (Join-Path $backupDir 'Snipaste.lnk'))
Write-Output ('AdminBatch=' + $batPath)
