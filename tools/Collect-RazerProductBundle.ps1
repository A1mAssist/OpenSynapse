[CmdletBinding()]
param(
    [string]$Sku,
    [string]$UsbPid,
    [string]$ProductId,
    [string]$Root,
    [string]$OutputDirectory,
    [int]$MaxFileSizeMB = 50,
    [int]$MaxFiles = 60
)

$ErrorActionPreference = 'Stop'

if (-not $Sku -and -not $UsbPid -and -not $ProductId) {
    throw 'Specify at least one of -Sku, -UsbPid, or -ProductId.'
}

if (-not $Root) {
    $Root = Join-Path $env:LOCALAPPDATA 'Razer\RazerAppEngine\User Data'
}
if (-not (Test-Path -LiteralPath $Root)) {
    throw "Razer AppEngine user data was not found: $Root"
}

if (-not $OutputDirectory) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputDirectory = Join-Path $env:TEMP "OpenSynapse-RazerProductBundle-$stamp"
}
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Output directory already exists: $OutputDirectory"
}

$patterns = @()
if ($Sku) { $patterns += $Sku }
if ($UsbPid) {
    $pidText = ($UsbPid -replace '^0x', '' -replace ':', '').ToUpperInvariant()
    if ($pidText.Length -eq 8 -and $pidText.StartsWith('1532')) {
        $pidText = $pidText.Substring(4)
    }
    $patterns += "1532:$pidText"
    $patterns += "0x$pidText"
}

$maxBytes = $MaxFileSizeMB * 1MB
$filesRoot = Join-Path $OutputDirectory 'files'
$manifestPath = Join-Path $OutputDirectory 'manifest.txt'
$readmePath = Join-Path $OutputDirectory 'README.txt'

New-Item -ItemType Directory -Path $filesRoot -Force | Out-Null

$foundFiles = @()
$scanned = 0
$skipped = 0

Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    $file = $_
    if ($file.Length -le 0 -or $file.Length -gt $maxBytes) { return }

    $extension = [System.IO.Path]::GetExtension($file.Name).ToLowerInvariant()
    if ($extension -and $extension -notin @('.js', '.mjs')) { return }

    $path = $file.FullName
    if ($path -match '\\(Logs|Local Storage|Session Storage|IndexedDB|Cookies|Network)(\\|$)') { return }

    $scanned++
    $prefix = New-Object byte[] ([Math]::Min(4096, [int]$file.Length))
    $stream = [System.IO.File]::Open($file.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $null = $stream.Read($prefix, 0, $prefix.Length)
    }
    finally {
        $stream.Dispose()
    }

    if ($prefix -contains 0) {
        $skipped++
        return
    }

    $text = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    $reasons = @()

    if ($file.Name -match 'rzDevice25') {
        $reasons += 'filename:rzDevice25'
    }

    foreach ($pattern in $patterns) {
        if ($text.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $reasons += "match:$pattern"
        }
    }

    if ($ProductId -and $text -match ('productId[^0-9]{0,8}' + [regex]::Escape($ProductId) + '(?![0-9])')) {
        $reasons += "productId:$ProductId"
    }

    if ($text.IndexOf('rzDevice25', [System.StringComparison]::Ordinal) -ge 0) {
        $reasons += 'references:rzDevice25'
    }

    if ($reasons.Count -eq 0) { return }

    $firstIndex = -1
    foreach ($pattern in $patterns) {
        $firstIndex = $text.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase)
        if ($firstIndex -ge 0) { break }
    }
    if ($firstIndex -lt 0) {
        $firstIndex = $text.IndexOf('rzDevice25', [System.StringComparison]::Ordinal)
    }

    $context = ''
    if ($firstIndex -ge 0) {
        $start = [Math]::Max(0, $firstIndex - 120)
        $length = [Math]::Min(280, $text.Length - $start)
        $context = ($text.Substring($start, $length) -replace '\s+', ' ')
    }

    $foundFiles += [pscustomobject]@{
        Source = $file.FullName
        Name = $file.Name
        Length = $file.Length
        LastWriteTime = $file.LastWriteTime
        Reasons = (($reasons | Select-Object -Unique) -join ', ')
        Context = $context
    }
}

if ($foundFiles.Count -eq 0) {
    throw "No matching Razer product files were found. Scanned $scanned text candidates under $Root. Open Synapse once on this device, then run the script again."
}

$selected = @($foundFiles | Select-Object -First $MaxFiles)
$index = 0
foreach ($match in $selected) {
    $index++
    $targetName = ('{0:D3}_{1}' -f $index, $match.Name)
    Copy-Item -LiteralPath $match.Source -Destination (Join-Path $filesRoot $targetName)
}

$lines = @()
$lines += 'OpenSynapse Razer product bundle collector'
$lines += 'Generated: ' + (Get-Date).ToString('o')
$lines += 'Sku: ' + $Sku
$lines += 'UsbPid: ' + $UsbPid
$lines += 'ProductId: ' + $ProductId
$lines += 'Source: ' + $Root
$lines += 'Scanned text candidates: ' + $scanned
$lines += 'Skipped binary candidates: ' + $skipped
$lines += 'Matched files: ' + $foundFiles.Count
$lines += 'Packaged files: ' + $selected.Count
$lines += ''
$lines += 'This package contains only candidate Razer JS cache files. Do not upload it publicly.'
$lines += ''

foreach ($match in $selected) {
    $lines += 'FILE: ' + $match.Name
    $lines += 'SIZE: ' + $match.Length
    $lines += 'MODIFIED: ' + $match.LastWriteTime.ToString('o')
    $lines += 'MATCH: ' + $match.Reasons
    $lines += 'CONTEXT: ' + $match.Context
    $lines += ''
}

[System.IO.File]::WriteAllLines($manifestPath, $lines, (New-Object System.Text.UTF8Encoding($false)))

$readme = @"
These files are read-only candidates copied from the local Razer AppEngine cache.

They may contain Razer proprietary JavaScript. Do not commit them to a public repository.
Send the ZIP to the OpenSynapse maintainer through a temporary file link and delete the
link after the protocol review is complete.

The collector did not execute any JavaScript. It only packages files that match the
device/protocol filters; account data, cookies, local storage, and serial numbers are
not intentionally included.
"@
[System.IO.File]::WriteAllText($readmePath, $readme, (New-Object System.Text.UTF8Encoding($false)))

$zipPath = $OutputDirectory.TrimEnd('\') + '.zip'
Compress-Archive -LiteralPath $OutputDirectory -DestinationPath $zipPath -CompressionLevel Optimal

[pscustomobject]@{
    ZipPath = $zipPath
    ManifestPath = $manifestPath
    MatchedFiles = $foundFiles.Count
    PackagedFiles = $selected.Count
    ScannedFiles = $scanned
    SkippedBinary = $skipped
}