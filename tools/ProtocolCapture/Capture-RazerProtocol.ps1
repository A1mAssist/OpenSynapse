[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Interface,
    [Parameter(Mandatory)][ValidateRange(1, 127)][int]$DeviceAddress,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Experiment,
    [Parameter(Mandatory)][ValidateRange(5, 120)][int]$DurationSeconds,
    [Parameter(Mandatory)][ValidateSet('02C6', '00B8')][string]$ProductId,
    [Parameter(Mandatory)][string]$OriginalValue,
    [Parameter(Mandatory)][string]$ChangedValue,
    [Parameter(Mandatory)][string]$RestoredValue
)

$ErrorActionPreference = 'Stop'
$tshark = 'C:\Program Files\Wireshark\tshark.exe'
if (-not (Test-Path -LiteralPath $tshark)) {
    throw "tshark not found: $tshark"
}
if ($Interface -notmatch '^USBPcap[1-9][0-9]*$') {
    throw 'Interface must be USBPcap followed by a positive integer.'
}

$captureInterfaces = & $tshark -D
if ($LASTEXITCODE -ne 0 -or -not ($captureInterfaces -match "\b$([regex]::Escape($Interface))\b")) {
    throw "Interface is not present in tshark -D: $Interface"
}

$root = Join-Path $env:LOCALAPPDATA 'OpenSynapse\reverse-engineering\raw'
$directory = Join-Path $root "$(Get-Date -Format 'yyyy-MM-dd')\$Experiment"
if (Test-Path -LiteralPath $directory) {
    throw "Experiment directory already exists: $directory"
}
New-Item -ItemType Directory -Path $directory | Out-Null

$capturePath = Join-Path $directory 'capture.pcapng'
$manifestPath = Join-Path $directory 'capture-manifest.json'
$startedAt = [DateTimeOffset]::UtcNow
& $tshark -i $Interface -a "duration:$DurationSeconds" -w $capturePath
if ($LASTEXITCODE -ne 0) {
    throw "tshark capture failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $capturePath)) {
    throw 'Capture completed without producing capture.pcapng.'
}

$version = (& $tshark --version | Select-Object -First 1)
$manifest = [ordered]@{
    SchemaVersion = 1
    Experiment = $Experiment
    ProductId = $ProductId.ToUpperInvariant()
    Interface = $Interface
    DeviceAddress = $DeviceAddress
    DurationSeconds = $DurationSeconds
    OriginalValue = $OriginalValue
    ChangedValue = $ChangedValue
    RestoredValue = $RestoredValue
    StartedAt = $startedAt
    CompletedAt = [DateTimeOffset]::UtcNow
    TsharkVersion = $version
    CaptureSha256 = (Get-FileHash -LiteralPath $capturePath -Algorithm SHA256).Hash
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

& (Join-Path $PSScriptRoot 'Export-RazerTransfers.ps1') `
    -CapturePath $capturePath `
    -DeviceAddress $DeviceAddress `
    -OutputPath (Join-Path $directory 'transfers.tsv')

[pscustomobject]@{
    Directory = $directory
    Capture = $capturePath
    Manifest = $manifestPath
    Transfers = (Join-Path $directory 'transfers.tsv')
}
