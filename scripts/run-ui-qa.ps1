# 后台跑 UI 冒烟：六个页面逐个导航 + 截图。会临时停掉正在运行的 OpenSynapse，结束后恢复。
param(
    [string]$Configuration = 'Release',
    [string]$SkillRoot = 'C:\Users\A1mAssist\.codex\skills\winui-ui-automation',
    [string]$OutDir = 'C:\Temp\opensynapse-qa\pages'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $root "src\OpenSynapse.App\bin\$Configuration\net10.0-windows10.0.26100.0\win-x64"
$exe = Join-Path $appDir 'OpenSynapse.App.exe'
if (-not (Test-Path $exe)) { throw "先 build：$exe 不存在" }

$scenarioPath = Join-Path $env:TEMP 'opensynapse-ui-pages.json'
$scenario = [ordered]@{
    exe              = $exe
    workingDirectory = $appDir
    window           = @{ titleContains = 'OpenSynapse' }
    timeoutMs        = 25000
    keepForeground   = $true
    parkPopups       = $true
    outDir           = $OutDir
    steps            = @(
        @{ action = 'wait'; ms = 3500 }
        @{ action = 'assertExists'; automationId = 'OverviewNavigationItem'; timeoutMs = 10000 }
        @{ action = 'select'; automationId = 'OverviewNavigationItem' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '01-overview' }
        @{ action = 'select'; automationId = 'DevicesNavigationItem' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '02-devices' }
        @{ action = 'select'; controlType = 'ListItem'; name = '宏' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '03-macros' }
        @{ action = 'select'; controlType = 'ListItem'; name = '配置' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '04-profiles' }
        @{ action = 'select'; controlType = 'ListItem'; name = '诊断' }
        @{ action = 'wait'; ms = 1200 }
        @{ action = 'capture'; name = '05-diagnostics' }
        @{ action = 'select'; controlType = 'ListItem'; name = '设置' }
        @{ action = 'wait'; ms = 1500 }
        @{ action = 'capture'; name = '06-settings' }
        @{ action = 'select'; automationId = 'OverviewNavigationItem' }
        @{ action = 'wait'; ms = 1000 }
        @{ action = 'tree'; name = 'back-to-overview' }
    )
}
$scenario | ConvertTo-Json -Depth 6 | Set-Content -Path $scenarioPath -Encoding utf8

$running = Get-Process -Name OpenSynapse.App -ErrorAction SilentlyContinue |
    Where-Object { $_.Path } | Select-Object -First 1
$restart = $null
if ($running) {
    $restart = @{ Exe = $running.Path; Dir = (Split-Path $running.Path -Parent) }
    Stop-Process -Id $running.Id -Force
    Start-Sleep -Seconds 2
}

try {
    & pwsh -NoProfile -File (Join-Path $SkillRoot 'scripts\winui-qa.ps1') -Scenario $scenarioPath
    $code = $LASTEXITCODE
    $report = Get-Content (Join-Path $OutDir 'report.json') -Raw | ConvertFrom-Json
    "UI 冒烟：$($report.status)（截图 $OutDir）"
    exit $code
}
finally {
    if ($restart) {
        Start-Process -FilePath $restart.Exe -ArgumentList '--silent' -WorkingDirectory $restart.Dir -WindowStyle Hidden
    }
}
