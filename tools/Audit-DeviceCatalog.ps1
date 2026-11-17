[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string[]]$LogPath = @(),
    [string[]]$Allowlist = @('010D')
)

$ErrorActionPreference = 'Stop'

function Convert-HexProductId {
    param([string]$Value)
    if ($Value -notmatch '^[0-9A-Fa-f]{4}$') { return $null }
    return [Convert]::ToUInt16($Value, 16)
}

# THX profiles are declared as AddV3/AddV4/AddV4Alias(profiles, 0x....).
$thxRoot = Join-Path $RepoRoot 'src/OpenSynapse.Windows/Audio'
$thxText = (Get-ChildItem -LiteralPath $thxRoot -Filter 'ThxHeadsetAdmission*.cs' |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
$supportedThx = [System.Collections.Generic.HashSet[ushort]]::new()
foreach ($match in [regex]::Matches(
    $thxText, 'Add(?:V4Alias|V4|V3)\(profiles,\s*0x([0-9A-Fa-f]{4})')) {
    [void]$supportedThx.Add([Convert]::ToUInt16($match.Groups[1].Value, 16))
}

$confirmedIds = [System.Collections.Generic.HashSet[ushort]]::new()
foreach ($path in $LogPath) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Warning "log not found: $path"
        continue
    }
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -notmatch "optional state read 'brightness' is unavailable") { continue }
        $match = [regex]::Match($line, '(?:pid|PID)[_=:]\s*([0-9A-Fa-f]{4})')
        if ($match.Success) {
            [void]$confirmedIds.Add([Convert]::ToUInt16($match.Groups[1].Value, 16))
        }
    }
}

Write-Host ''
Write-Host '== THX admission gaps ==' -ForegroundColor Cyan
$bundleRoot = Join-Path $RepoRoot 'artifacts/upstream-reference/razer-mw-full-cdn/bundles'
$thxRows = @()
if (Test-Path -LiteralPath $bundleRoot) {
    foreach ($directory in Get-ChildItem -LiteralPath $bundleRoot -Directory | Sort-Object Name) {
        $productId = Convert-HexProductId $directory.Name
        if ($null -eq $productId) { continue }
        $manifestPath = Join-Path $directory.FullName 'manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath)) { continue }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw
        if ($manifest -notmatch 'THXAPO') { continue }
        $deviceName = [regex]::Match(
            $manifest, '"deviceName"\s*:\s*"([^"]*)"').Groups[1].Value
        if ($deviceName -notmatch 'BlackShark|Kraken|Barracuda|Nari|Hammerhead|Kitty|Headset') {
            continue
        }
        $resource = [regex]::Match(
            $manifest, '"resourceName"\s*:\s*"([^"]*THXAPO[^"]*)"').Groups[1].Value
        $thxRows += [pscustomobject]@{
            ProductId = $directory.Name.ToUpperInvariant()
            Device = $deviceName
            Supported = $supportedThx.Contains($productId)
            ThxResource = $resource
        }
    }
}
else {
    Write-Warning "bundle root not found: $bundleRoot"
}

$missingThx = @($thxRows | Where-Object { -not $_.Supported })
if ($missingThx.Count -eq 0) {
    Write-Host "No gaps across $($thxRows.Count) THX bundle(s)."
}
else {
    $missingThx | Format-Table -AutoSize | Out-String -Width 200
}

Write-Host ''
Write-Host '== Brightness extended write without extended read ==' -ForegroundColor Cyan
$catalogRoot = Join-Path $RepoRoot 'src/OpenSynapse.Windows/Devices/RazerHid'
$brightnessRows = @()
foreach ($file in Get-ChildItem -LiteralPath $catalogRoot -Filter '*.json' | Sort-Object Name) {
    $catalog = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    foreach ($device in @($catalog.devices)) {
        if ($null -eq $device.transactions) { continue }
        $transactions = @($device.transactions.PSObject.Properties.Name)
        if ($transactions -notcontains 'razer_chroma_extended_matrix_brightness') { continue }
        if ($transactions -contains 'razer_chroma_extended_matrix_get_brightness') { continue }

        $reads = @($transactions | Where-Object { $_ -match 'get.*brightness' })
        $productId = Convert-HexProductId $device.productId
        $assessment =
            if ($reads.Count -gt 0) { 'standard-read' }
            elseif ($Allowlist -contains $device.productId) { 'allowlisted' }
            else { 'no-read' }
        $brightnessRows += [pscustomobject]@{
            ProductId = $device.productId.ToUpperInvariant()
            Name = $device.displayName
            Category = $device.category
            ExistingRead = $reads -join ','
            LogConfirmed = $null -ne $productId -and $confirmedIds.Contains($productId)
            Assessment = $assessment
            Source = $file.Name
        }
    }
}

if ($brightnessRows.Count -eq 0) {
    Write-Host 'No extended-write devices without an extended getter.'
}
else {
    $brightnessRows | Sort-Object Assessment, ProductId |
        Format-Table -AutoSize | Out-String -Width 240
}

Write-Host ''
Write-Host '== OpenRazer matrix_brightness:rw cross-check ==' -ForegroundColor Cyan
$openRazerCatalog = @(
    (Join-Path $RepoRoot 'openrazer-capability-catalog.json'),
    (Join-Path $RepoRoot 'artifacts/upstream-reference/openrazer-capability-catalog.json')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($null -eq $openRazerCatalog) {
    Write-Host 'Skipped: no openrazer-capability-catalog.json in the repo or artifacts.'
}
else {
    Write-Host "Skipped: schema for $openRazerCatalog is not pinned; add the parser with a sample."
}

$confirmedRows = @($brightnessRows | Where-Object { $_.LogConfirmed })
$noReadRows = @($brightnessRows | Where-Object { $_.Assessment -eq 'no-read' })
Write-Host ''
Write-Host '== Summary ==' -ForegroundColor Cyan
Write-Host "THX bundles missing admission : $($missingThx.Count) / $($thxRows.Count)"
Write-Host "Extended-write brightness rows : $($brightnessRows.Count)"
Write-Host "  no brightness read at all    : $($noReadRows.Count)"
Write-Host "  log-confirmed                : $($confirmedRows.Count)"
