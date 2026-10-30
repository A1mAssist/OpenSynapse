#requires -Version 7.0

# Audits every Razer middleware bundle's declared rzDeviceType against
# RazerHeadsetChannelCatalog. Runs entirely from the local CDN snapshot.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$bundleRoot = Join-Path $repoRoot 'artifacts\razer-mw-full-cdn\bundles'
$catalogPath = Join-Path $repoRoot 'src\OpenSynapse.Windows\Devices\RazerHeadsetChannelCatalog.cs'
$outputPath = Join-Path $repoRoot 'artifacts\device-transport-scan.csv'

foreach ($required in @($bundleRoot, $catalogPath)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required path not found: $required"
    }
}

$catalog = @{}
$catalogText = [IO.File]::ReadAllText($catalogPath)
# shortcut: catalog rows must keep the [0xPID] = new("Type", RazerHeadsetChannel.X)
# shape; the row-count guard below catches a wholesale format change.
foreach ($match in [regex]::Matches(
        $catalogText,
        '\[0x([0-9A-Fa-f]{4})\]\s*=\s*new\("([^"]+)",\s*RazerHeadsetChannel\.(\w+)\)')) {
    $catalog[[Convert]::ToUInt16($match.Groups[1].Value, 16)] = [pscustomobject]@{
        DeviceType = $match.Groups[2].Value
        Channel = $match.Groups[3].Value
    }
}
if ($catalog.Count -lt 50) {
    throw "Parsed only $($catalog.Count) catalog rows; the C# table format changed."
}

$channelByDeviceType = @{
    AudioMxicDevice = 'Mxic64'
    rzDevice25ValkyrieT3 = 'Mxic64'
    rzDevice40 = 'Mxic64'
    AudioValkyrie = 'Valkyrie64'
    AudioBillieT1 = 'Billie64'
    AudioBillieT2 = 'Billie64'
    AudioBastetT1 = 'Billie64'
    rzDevice40HeadsetAiroha = 'Mxic64'
}
$dedicatedBlackSharkPids = @('0576', '0577', '101B', '101C')

$rows = @(
    foreach ($bundle in Get-ChildItem -LiteralPath $bundleRoot -Directory | Sort-Object Name) {
        $types = @(
            foreach ($mainFile in Get-ChildItem -LiteralPath $bundle.FullName -Filter 'main.*.js' -File) {
                [regex]::Matches(
                    [IO.File]::ReadAllText($mainFile.FullName),
                    'useFeature\("rzDeviceType","([^"]+)"') |
                    ForEach-Object { $_.Groups[1].Value }
            }
        ) | Select-Object -Unique
        $types = @($types)

        $productId = [Convert]::ToUInt16($bundle.Name, 16)
        $entry = $catalog[$productId]
        $channel = if ($entry) { $entry.Channel } else { 'Unsupported' }
        $expectedChannel =
            if ($entry -and $channelByDeviceType.ContainsKey($entry.DeviceType)) {
                $channelByDeviceType[$entry.DeviceType]
            } else {
                'Feature91'
            }
        $status =
            if ($types.Count -eq 0) { 'no-device-type' }
            elseif (-not $entry -or $entry.Channel -eq 'Unsupported') { 'unsupported' }
            elseif ($types.Count -ne 1 -or $types[0] -ne $entry.DeviceType) { 'mismatch' }
            elseif ($entry.Channel -eq 'DedicatedBlackShark' -and $bundle.Name -in $dedicatedBlackSharkPids) { 'ok' }
            elseif ($entry.Channel -eq $expectedChannel) { 'ok' }
            else { 'mismatch' }

        [pscustomobject]@{
            Pid = $bundle.Name
            DeviceType = $types -join ';'
            Channel = $channel
            Status = $status
        }
    }
)

if (-not ($rows | Where-Object Status -eq 'ok')) {
    throw 'No bundle matched the catalog; the rzDeviceType scan did not validate anything.'
}

$outputDirectory = Split-Path -Parent $outputPath
$null = New-Item -ItemType Directory -Path $outputDirectory -Force
$rows | Export-Csv -LiteralPath $outputPath -NoTypeInformation -Encoding utf8

"Scanned $($rows.Count) bundles -> $outputPath"
foreach ($channel in 'Feature91', 'Mxic64', 'Valkyrie64', 'Billie64', 'DedicatedBlackShark', 'Unsupported') {
    '  {0,-20} {1}' -f $channel, @($rows | Where-Object Channel -eq $channel).Count
}
$noType = @($rows | Where-Object Status -eq 'no-device-type' | ForEach-Object Pid)
"no-device-type PIDs: $(if ($noType) { $noType -join ', ' } else { '(none)' })"
$mismatches = @($rows | Where-Object Status -eq 'mismatch' | ForEach-Object Pid)
if ($mismatches) {
    "mismatch PIDs: $($mismatches -join ', ')"
}
