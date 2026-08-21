[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+([+-][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$ReleaseNotes,

    [string]$SignParams = $env:OPEN_SYNAPSE_SIGN_PARAMS,

    [switch]$Upload
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\OpenSynapse.App\OpenSynapse.App.csproj'
$artifactRoot = Join-Path $repoRoot "artifacts\velopack-v$Version"
$publishDir = Join-Path $artifactRoot 'publish'
$releaseDir = Join-Path $artifactRoot 'releases'
$toolDir = Join-Path $repoRoot 'artifacts\tools'
$vpk = Join-Path $toolDir 'vpk.exe'
$repoUrl = 'https://github.com/A1mAssist/OpenSynapse'
$token = $env:GITHUB_TOKEN

if ($Upload -and [string]::IsNullOrWhiteSpace($token)) {
    throw 'Set GITHUB_TOKEN before using -Upload.'
}
if ($Upload -and [string]::IsNullOrWhiteSpace($SignParams)) {
    throw 'Set OPEN_SYNAPSE_SIGN_PARAMS before publishing a production release.'
}

if (-not (Test-Path -LiteralPath $vpk)) {
    dotnet tool install --tool-path $toolDir vpk --version 1.2.0
    if ($LASTEXITCODE -ne 0) { throw 'Could not install vpk 1.2.0.' }
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir, $releaseDir | Out-Null
dotnet publish $project -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$unexpectedMui = Get-ChildItem -LiteralPath $publishDir -Directory |
    Where-Object Name -NotIn @('en-US', 'en-us', 'zh-CN') |
    Where-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.mui' -File }
if ($unexpectedMui) {
    throw "Unexpected language directories: $($unexpectedMui.Name -join ', ')"
}

if ($Upload) {
    & $vpk download github --repoUrl $repoUrl --outputDir $releaseDir --channel win
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'No prior Velopack release was downloaded; a full first release will be created.'
    }
}

$packArgs = @(
    'pack',
    '--packId', 'OpenSynapse',
    '--packVersion', $Version,
    '--packDir', $publishDir,
    '--mainExe', 'OpenSynapse.App.exe',
    '--packTitle', 'OpenSynapse',
    '--packAuthors', 'A1mAssist',
    '--runtime', 'win-x64',
    '--icon', (Join-Path $repoRoot 'src\OpenSynapse.App\Assets\OpenSynapse.ico'),
    '--shortcuts', 'StartMenuRoot',
    '--outputDir', $releaseDir
)
if ($ReleaseNotes) {
    $packArgs += @('--releaseNotes', (Resolve-Path -LiteralPath $ReleaseNotes).Path)
}
if ($SignParams) {
    $packArgs += @('--signParams', $SignParams)
}

& $vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }

if ($Upload) {
    & $vpk upload github --repoUrl $repoUrl --outputDir $releaseDir --channel win `
        --token $token --publish true --releaseName "OpenSynapse v$Version" --tag "v$Version"
    if ($LASTEXITCODE -ne 0) { throw 'vpk upload github failed.' }
}

Get-ChildItem -LiteralPath $releaseDir -File | Select-Object Name, Length
