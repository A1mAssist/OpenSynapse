# Run the UI smoke test in the background. Leave existing instances alone unless -AllowRestart is set.
param(
    [string]$Configuration = 'Release',
    [string]$SkillRoot = 'C:\Users\A1mAssist\.codex\skills\winui-ui-automation',
    [string]$OutDir = 'D:\Temp\opensynapse-qa\pages',
    [string]$AppPath,
    [switch]$AllowRestart
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $root "src\OpenSynapse.App\bin\$Configuration\net10.0-windows10.0.26100.0\win-x64"
$exe = if ($AppPath) { [IO.Path]::GetFullPath($AppPath) } else { Join-Path $appDir 'OpenSynapse.App.exe' }
$appDir = Split-Path -Parent $exe
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Build first: $exe does not exist" }
$runner = Join-Path $SkillRoot 'scripts/winui-qa.ps1'
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) { throw "UIA runner does not exist: $runner" }

$running = @(Get-Process -Name OpenSynapse.App -ErrorAction SilentlyContinue)
if ($running.Count -gt 0 -and -not $AllowRestart) {
    throw 'OpenSynapse is already running. Exit it first, or pass -AllowRestart.'
}
if ($running.Count -gt 1) { throw 'Multiple instances are running and cannot be restored safely. Exit them first.' }
if ($running.Count -eq 1 -and -not $running[0].Path) { throw 'Cannot determine the existing instance path; it was not stopped.' }

$runDir = Join-Path $OutDir ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $runDir -Force
$scenarioPath = Join-Path $runDir 'scenario.json'
$scenario = [ordered]@{
    exe              = $exe
    workingDirectory = $appDir
    window           = @{ titleContains = 'OpenSynapse' }
    timeoutMs        = 25000
    keepForeground   = $true
    parkPopups       = $true
    outDir           = $runDir
    steps            = @(
        @{ action = 'wait'; ms = 3500 }
        @{ action = 'assertExists'; automationId = 'OverviewNavigationItem'; timeoutMs = 10000 }
        @{ action = 'select'; automationId = 'OverviewNavigationItem' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '01-overview' }
        @{ action = 'select'; automationId = 'DevicesNavigationItem' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '02-devices' }
        @{ action = 'select'; automationId = 'MacrosNavigationItem' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '03-macros' }
        @{ action = 'select'; automationId = 'ProfilesNavigationItem' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '04-profiles' }
        @{ action = 'select'; automationId = 'DiagnosticsNavigationItem' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '05-diagnostics' }
        @{ action = 'select'; automationId = 'SettingsNavigationItem' }
        @{ action = 'wait'; ms = 1500 }
        @{ action = 'capture'; name = '06-settings' }
        @{ action = 'select'; automationId = 'OverviewNavigationItem' }
        @{ action = 'wait'; ms = 1000 }
        @{ action = 'tree'; name = 'back-to-overview' }
    )
}
$scenario | ConvertTo-Json -Depth 6 | Set-Content -Path $scenarioPath -Encoding utf8

$restart = $null
try {
    if ($running.Count -eq 1) {
        $process = $running[0]
        $restore = @{ Exe = $process.Path; Dir = (Split-Path $process.Path -Parent) }
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        $restart = $restore
        Start-Sleep -Seconds 2
    }
    $startedAt = [DateTimeOffset]::UtcNow
    & pwsh -NoProfile -File $runner -Scenario $scenarioPath
    if ($LASTEXITCODE -ne 0) { throw "UIA runner failed: exit $LASTEXITCODE (report $runDir)" }
    $reportPath = Join-Path $runDir 'report.json'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'This run did not produce report.json.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $reportStarted = [DateTimeOffset]$report.startedAt
    $reportFinished = [DateTimeOffset]$report.finishedAt
    if ($report.status -ne 'passed' -or
        [IO.Path]::GetFullPath($report.scenario) -ne [IO.Path]::GetFullPath($scenarioPath) -or
        [IO.Path]::GetFullPath($report.exe) -ne [IO.Path]::GetFullPath($exe) -or
        $reportStarted -lt $startedAt -or $reportFinished -lt $reportStarted) {
        throw "UIA report failed or does not belong to this run: $reportPath"
    }
    "UI smoke test: $($report.status) (screenshots $runDir)"
}
finally {
    if ($restart) {
        Start-Process -FilePath $restart.Exe -ArgumentList '--silent' -WorkingDirectory $restart.Dir -WindowStyle Hidden | Out-Null
    }
}
