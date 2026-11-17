[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [int]$TimeoutSec = 20
)

$ErrorActionPreference = 'Stop'

# RazerProductAliases.cs is the only place these pairs are declared.
$aliasPath = Join-Path $RepoRoot 'src/OpenSynapse.Core/Devices/RazerProductAliases.cs'
$source = Get-Content -LiteralPath $aliasPath -Raw
$pairs = @([regex]::Matches(
    $source, '\[0x([0-9A-Fa-f]{4})\]\s*=\s*0x([0-9A-Fa-f]{4})') |
    ForEach-Object {
        [pscustomobject]@{
            Alias = [Convert]::ToUInt16($_.Groups[1].Value, 16)
            Target = [Convert]::ToUInt16($_.Groups[2].Value, 16)
        }
    })

function Get-DeviceManifest([int]$Page) {
    $uri = "https://apps.razer.com/synapse/products/$Page/mw/lighting-manifests/DeviceManifest_${Page}_0.json"
    try {
        return Invoke-RestMethod -Uri $uri -TimeoutSec $TimeoutSec
    }
    catch {
        return $null
    }
}

$manifests = @{}
$drift = 0
$missing = 0
foreach ($pair in $pairs) {
    $page = [int]$pair.Target
    if (-not $manifests.ContainsKey($page)) {
        $manifests[$page] = Get-DeviceManifest -Page $page
    }

    $manifest = $manifests[$page]
    if ($null -eq $manifest) {
        Write-Host ("SKIP {0:X4} -> {1:X4}: no device manifest" -f $pair.Alias, $pair.Target) -ForegroundColor DarkGray
        $missing++
        continue
    }

    $virtual = @($manifest.virtualProductIdList | ForEach-Object { [int]$_ })
    $declared =
        ([int]$manifest.wiredProductId -eq $pair.Alias) -or
        ([int]$manifest.dongleProductId -eq $pair.Alias) -or
        ([int]$manifest.bleProductId -eq $pair.Alias) -or
        ($virtual -contains $pair.Alias)
    $wiredOk = [int]$manifest.wiredProductId -eq $page

    if ($declared -and $wiredOk) {
        Write-Host ("OK   {0:X4} -> {1:X4}" -f $pair.Alias, $pair.Target) -ForegroundColor Green
    }
    else {
        Write-Warning ("DRIFT {0:X4} -> {1:X4}: wired={2} dongle={3} ble={4} virtual={5}" -f `
            $pair.Alias, $pair.Target,
            $manifest.wiredProductId, $manifest.dongleProductId, $manifest.bleProductId,
            ($virtual -join ','))
        $drift++
    }
}

Write-Host ''
Write-Host ("aliases={0} drift={1} no-manifest={2}" -f $pairs.Count, $drift, $missing)
exit ([int]($drift -gt 0))
