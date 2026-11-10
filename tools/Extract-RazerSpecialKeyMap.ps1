param(
    [string]$Root = (Join-Path $PSScriptRoot '..\artifacts\razer-mw-full-cdn'),
    [string]$Output = (Join-Path $PSScriptRoot '..\src\OpenSynapse.Windows\Devices\RazerHid\mapping-private-keys.json')
)

$ErrorActionPreference = 'Stop'

# Only labels whose mapping-page id is known. Anything else is left out on purpose.
$labelToMappingId = @{
    SensitivityStageUp         = 0x0B
    SensitivityStageDown       = 0x0C
    CycleUpProfile             = 0x0E
    SensitivityClutch          = 0x0F
    CycleUpSensitivityStages   = 0x60
    CycleDownSensitivityStages = 0x61
    ScrollModeSwitch           = 0x6A
}

$definitions = Get-Content (Join-Path $Root 'device-definitions.json') -Raw | ConvertFrom-Json
$bundleDirs = @{}
Get-ChildItem (Join-Path $Root 'bundles') -Directory | ForEach-Object { $bundleDirs[$_.Name] = $_.FullName }
$bundleFiles = @{}
Get-ChildItem (Join-Path $Root 'bundles') -File -Recurse -Filter 'main.*.js' |
    ForEach-Object { $bundleFiles[$_.Name] = $_.FullName }

function Resolve-Bundle([object]$definition) {
    $productHex = $definition.productId.ToUpperInvariant()
    $productDec = [Convert]::ToInt32($definition.productId, 16)
    $usbHex = '{0:X4}' -f [int]$definition.usb.productPid
    foreach ($name in @($productHex, $usbHex, [string]$productDec, [string][int]$definition.usb.productPid)) {
        if ($name -and $bundleDirs.ContainsKey($name)) {
            $path = Join-Path $bundleDirs[$name] $definition.bundle
            if (Test-Path -LiteralPath $path) { return $path }
        }
    }

    if ($bundleFiles.ContainsKey($definition.bundle)) { return $bundleFiles[$definition.bundle] }
    return $null
}

$result = [ordered]@{}
foreach ($definition in $definitions | Where-Object { $_.category -eq 'mouse' -and $_.bundle }) {
    $bundlePath = Resolve-Bundle $definition
    if (-not $bundlePath) {
        Write-Warning "NO_EVIDENCE $($definition.productId) $($definition.deviceName): $($definition.bundle)"
        continue
    }

    $text = Get-Content -LiteralPath $bundlePath -Raw
    $keys = [ordered]@{}
    foreach ($match in [regex]::Matches($text, '\{inputID:"([^"]+)",buttonKey:"([^"]+)",key:(\d+)\}')) {
        $label = $match.Groups[2].Value
        if (-not $labelToMappingId.ContainsKey($label)) { continue }
        $id = [int]$labelToMappingId[$label]
        $code = [int]$match.Groups[3].Value
        if ($code -lt 0 -or $code -gt 255) {
            throw "Key code $code is outside one byte for $($definition.productId) $label"
        }
        $keys[('{0:X2}' -f $id)] = [ordered]@{ code = $code; label = $label }
    }

    if ($keys.Count -eq 0) { continue }

    $source = $bundlePath.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/'
    $aliases = @($definition.productId, ('{0:X4}' -f [int]$definition.usb.productPid))
    if ([int]$definition.usb.donglePid -ne 0) {
        $aliases += '{0:X4}' -f [int]$definition.usb.donglePid
    }
    if ([int]$definition.usb.blePid -ne 0) {
        $aliases += '{0:X4}' -f [int]$definition.usb.blePid
    }

    $entry = [ordered]@{
        source     = $source
        catalogPid = $definition.productId
        keys       = $keys
    }
    foreach ($alias in ($aliases | Select-Object -Unique)) {
        if ($result.Contains($alias)) {
            if ($alias -ceq $definition.productId) {
                $result[$alias] = $entry
                continue
            }
            $existing = $result[$alias]
            if ($existing.catalogPid -eq $definition.productId) { continue }
            if (($existing.keys | ConvertTo-Json -Compress) -eq ($keys | ConvertTo-Json -Compress)) { continue }
            Write-Warning "Conflicting private-key entry for $alias; keeping $($existing.catalogPid)"
            continue
        }
        $result[$alias] = $entry
    }
}

function Assert-Key([string]$productId, [string]$mappingId, [int]$code) {
    if (-not $result.Contains($productId) -or
        -not $result[$productId].keys.Contains($mappingId) -or
        $result[$productId].keys[$mappingId].code -ne $code) {
        throw "Expected $productId mapping $mappingId -> $code"
    }
}

Assert-Key '009C' '0B' 32
Assert-Key '009C' '0C' 33
Assert-Key '00CD' '0E' 80
Assert-Key '00CD' '0F' 81
Assert-Key '00C1' '60' 82
Assert-Key '00BF' '60' 82

$json = $result | ConvertTo-Json -Depth 6
Set-Content -LiteralPath $Output -Value $json -Encoding utf8
Write-Host "Wrote $($result.Count) products to $Output"
