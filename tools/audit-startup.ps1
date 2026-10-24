$ErrorActionPreference = 'SilentlyContinue'

$cfgPath = Join-Path $env:APPDATA 'TrayStartup\apps.json'
$outPath = 'E:\Dsh\workspace\TrayStartup\tools\startup-audit.txt'
$lines = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path $cfgPath)) {
    'NO_CONFIG' | Set-Content -LiteralPath $outPath -Encoding UTF8
    exit
}

$cfg = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
$run = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValues = @{}
if ($run) {
    $run.PSObject.Properties | Where-Object { $_.Value -is [string] } | ForEach-Object { $runValues[$_.Name] = $_.Value }
}

$machineValues = @{}
foreach ($keyPath in @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'
)) {
    $key = Get-ItemProperty -Path $keyPath
    if ($key) {
        $key.PSObject.Properties | Where-Object { $_.Value -is [string] } | ForEach-Object {
            $machineValues[$keyPath + '|' + $_.Name] = $_.Value
        }
    }
}

$startupLinks = @()
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @(
    [Environment]::GetFolderPath('Startup'),
    [Environment]::GetFolderPath('CommonStartup')
)) {
    if ($folder -and (Test-Path $folder)) {
        Get-ChildItem -LiteralPath $folder -Filter '*.lnk' | ForEach-Object {
            $target = ''
            try { $target = $shell.CreateShortcut($_.FullName).TargetPath } catch { }
            $startupLinks += [pscustomobject]@{ Link = $_.FullName; Target = $target }
        }
    }
}

$tasks = @()
Get-ScheduledTask | ForEach-Object {
    $task = $_
    foreach ($action in $task.Actions) {
        $execute = $action.Execute
        if ($execute) {
            $tasks += [pscustomobject]@{ Name = $task.TaskName; Path = $task.TaskPath; Execute = $execute; State = $task.State }
        }
    }
}

$services = Get-CimInstance Win32_Service | Select-Object Name, DisplayName, State, StartMode, PathName

$lines.Add('CONFIG=' + $cfgPath)
$lines.Add('APPS=' + $cfg.apps.Count)
$lines.Add('')
foreach ($app in $cfg.apps) {
    $lines.Add('=== ' + $app.name + ' ===')
    $lines.Add('path=' + $app.path)
    $lines.Add('registryName=' + $app.registryName)
    $lines.Add('adopted=' + $app.adopted)
    $lines.Add('configOtherStartup=' + $app.otherStartup)

    $managedPresent = $runValues.ContainsKey($app.registryName)
    $lines.Add('managedHKCURunPresent=' + $managedPresent)
    if ($managedPresent) { $lines.Add('managedHKCURunValue=' + $runValues[$app.registryName]) }

    $runMatches = @($runValues.GetEnumerator() | Where-Object { $_.Value -like ('*' + $app.path + '*') })
    $lines.Add('HKCURunValuesTargetingExe=' + (($runMatches | ForEach-Object { $_.Name + '=' + $_.Value }) -join ' || '))

    $machineMatches = @($machineValues.GetEnumerator() | Where-Object { $_.Value -like ('*' + $app.path + '*') })
    $lines.Add('HKLM RunValuesTargetingExe=' + (($machineMatches | ForEach-Object { $_.Name + '=' + $_.Value }) -join ' || '))

    $linkMatches = @($startupLinks | Where-Object { $_.Target -and $_.Target -ieq $app.path })
    $lines.Add('StartupLinksTargetingExe=' + (($linkMatches | ForEach-Object { $_.Link }) -join ' || '))

    $taskMatches = @($tasks | Where-Object { $_.Execute -like ('*' + $app.path + '*') -or $_.Execute -like ('*' + [IO.Path]::GetFileName($app.path) + '*') })
    $lines.Add('ScheduledTasks=' + (($taskMatches | ForEach-Object { $_.Path + $_.Name + ' [' + $_.State + '] -> ' + $_.Execute }) -join ' || '))

    $serviceMatches = @($services | Where-Object { $_.PathName -like ('*' + $app.path + '*') -or $_.PathName -like ('*' + [IO.Path]::GetFileName($app.path) + '*') })
    $lines.Add('Services=' + (($serviceMatches | ForEach-Object { $_.Name + ' [' + $_.State + '/' + $_.StartMode + '] -> ' + $_.PathName }) -join ' || '))
    $lines.Add('')
}

$lines.Add('=== ALL STARTUP LINKS ===')
foreach ($link in $startupLinks) {
    $lines.Add($link.Link + ' -> ' + $link.Target)
}

$lines.Add('')
$lines.Add('=== LGHUB RELATED TASKS ===')
foreach ($task in @($tasks | Where-Object { $_.Execute -match 'LGHUB|Logi|logi' -or $_.Name -match 'LGHUB|Logi|logi' })) {
    $lines.Add($task.Path + $task.Name + ' [' + $task.State + '] -> ' + $task.Execute)
}

$lines.Add('')
$lines.Add('=== LGHUB RELATED SERVICES ===')
foreach ($service in @($services | Where-Object { $_.Name -match 'LGHUB|Logi|logi' -or $_.DisplayName -match 'LGHUB|Logi|logi' -or $_.PathName -match 'LGHUB|Logi|logi' })) {
    $lines.Add($service.Name + ' [' + $service.State + '/' + $service.StartMode + '] -> ' + $service.PathName)
}

$lines | Set-Content -LiteralPath $outPath -Encoding UTF8
Write-Output $outPath
