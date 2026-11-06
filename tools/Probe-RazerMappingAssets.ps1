[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int]$ProductId,

    [string]$CacheDirectory = (Join-Path $env:TEMP 'OpenSynapse-RazerMappingProbe'),

    [switch]$NoCache,

    [string]$JsonPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore

function Get-FirstValue([object]$value) {
    if ($null -eq $value) { return $null }
    if ($value -is [System.Array]) { return [string]$value[0] }
    return [string]$value
}

function Get-RemoteLength([string]$url) {
    if (-not $url) { return 0 }
    try {
        $response = Invoke-WebRequest -Uri $url -Method Head -TimeoutSec 30
        $length = $response.Headers['Content-Length'] | Select-Object -First 1
        if ($length) { return [int64]$length }
    } catch {
    }
    return 0
}

function Get-Number([string]$text, [string]$name) {
    $match = [regex]::Match($text, [regex]::Escape($name) + ':(?<value>-?\d+(?:\.\d+)?)')
    if (-not $match.Success) { return $null }
    return [double]::Parse($match.Groups['value'].Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-SvgPathBounds([string]$pathData) {
    if ([string]::IsNullOrWhiteSpace($pathData)) { return $null }
    try {
        $geometry = [System.Windows.Media.Geometry]::Parse($pathData)
        $bounds = $geometry.Bounds
        if ($bounds.IsEmpty) { return $null }
        return [pscustomobject]@{
            CenterX = $bounds.X + $bounds.Width / 2
            CenterY = $bounds.Y + $bounds.Height / 2
            Width = $bounds.Width
            Height = $bounds.Height
        }
    } catch {
        return $null
    }
}

function Get-KeyboardAnchors([string]$text) {
    $seenInput = [Collections.Generic.HashSet[string]]::new()
    $seenHid = [Collections.Generic.HashSet[int]]::new()
    $anchors = @()
    $objects = [regex]::Matches($text, '\{buttonKey:"(?<key>[^"]+)"[^{}]*?\}')
    foreach ($object in $objects) {
        $body = $object.Value
        $idMatch = [regex]::Match($body, 'inputID:"(?<id>[^"]+)"')
        $dMatch = [regex]::Match($body, 'd:"(?<d>[^"]+)"')
        if (-not $idMatch.Success -or -not $dMatch.Success) { continue }
        $inputId = $idMatch.Groups['id'].Value
        $hidMatch = [regex]::Match($body, 'HID:"(?<hid>\d+)"')
        $hid = if ($hidMatch.Success) { [int]$hidMatch.Groups['hid'].Value } else { $null }
        if ($null -ne $hid) {
            if (-not $seenHid.Add($hid)) { continue }
        } elseif (-not $seenInput.Add($inputId)) {
            continue
        }
        $bounds = Get-SvgPathBounds $dMatch.Groups['d'].Value
        if (-not $bounds) { continue }
        $anchors += [pscustomobject]@{
            InputId = $inputId
            Hid = $hid
            Key = $object.Groups['key'].Value
            CenterX = $bounds.CenterX
            CenterY = $bounds.CenterY
            Width = $bounds.Width
            Height = $bounds.Height
        }
    }
    return @($anchors | Sort-Object InputId)
}

New-Item -ItemType Directory -Force -Path $CacheDirectory | Out-Null
$base = "https://apps.razer.com/synapse/products/$ProductId/ui"
$manifestUrl = "$base/asset-manifest.json"
$total = [Diagnostics.Stopwatch]::StartNew()
$manifestWatch = [Diagnostics.Stopwatch]::StartNew()

try {
    $manifest = Invoke-RestMethod -Uri $manifestUrl -TimeoutSec 30
} catch {
    $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    [pscustomobject]@{
        ProductId = $ProductId
        Status = "asset-manifest-error:$status"
        AssetManifest = $manifestUrl
        Error = $_.Exception.Message
    } | ConvertTo-Json -Depth 6
    exit 2
}

$manifestWatch.Stop()
$mainJs = Get-FirstValue $manifest.files.PSObject.Properties['main.js'].Value
$image3x = Get-FirstValue ($manifest.files.PSObject.Properties | Where-Object {
    $_.Name -match '^static/media/prd-3x[^/]*\.(avif|png|webp)$'
} | Select-Object -First 1).Value
$image1x = Get-FirstValue ($manifest.files.PSObject.Properties | Where-Object {
    $_.Name -match '^static/media/prd-1x[^/]*\.(avif|png|webp)$'
} | Select-Object -First 1).Value

if (-not $mainJs) {
    [pscustomobject]@{
        ProductId = $ProductId
        Status = 'main-js-missing'
        AssetManifest = $manifestUrl
        Image3x = if ($image3x) { "$base/$($image3x -replace '^\./','')" } else { $null }
    } | ConvertTo-Json -Depth 6
    exit 2
}

$mainJsUrl = "$base/$($mainJs -replace '^\./','')"
$mainJsName = Split-Path $mainJsUrl -Leaf
$mainJsCache = Join-Path $CacheDirectory "$ProductId-$mainJsName"
$fromCache = (-not $NoCache) -and (Test-Path -LiteralPath $mainJsCache)
$downloadWatch = [Diagnostics.Stopwatch]::StartNew()
if ($fromCache) {
    $downloadWatch.Stop()
} else {
    if (Test-Path -LiteralPath $mainJsCache) { Remove-Item -LiteralPath $mainJsCache -Force }
    Invoke-WebRequest -Uri $mainJsUrl -OutFile $mainJsCache -TimeoutSec 120
    $downloadWatch.Stop()
}

$parseWatch = [Diagnostics.Stopwatch]::StartNew()
$text = [IO.File]::ReadAllText($mainJsCache, [Text.Encoding]::UTF8)
$anchorMatches = [regex]::Matches(
    $text,
    'drawLine\((?<index>\d+),\s*this\.props\.buttonList\[(?<button>\d+)\],\s*(?<x>-?\d+(?:\.\d+)?),\s*(?<y>-?\d+(?:\.\d+)?),\s*!(?<left>[01])\)')
$seen = [Collections.Generic.HashSet[int]]::new()
$anchors = @()
foreach ($match in $anchorMatches) {
    $index = [int]$match.Groups['index'].Value
    if (-not $seen.Add($index)) { continue }
    $anchors += [pscustomobject]@{
        Index = $index
        ButtonListIndex = [int]$match.Groups['button'].Value
        CanvasX = [double]::Parse($match.Groups['x'].Value, [Globalization.CultureInfo]::InvariantCulture)
        CanvasY = [double]::Parse($match.Groups['y'].Value, [Globalization.CultureInfo]::InvariantCulture)
        IsLeft = $match.Groups['left'].Value -eq '0'
    }
}
$anchors = @($anchors | Sort-Object Index)

$h5Start = $text.IndexOf('cvsH:', [StringComparison]::Ordinal)
$h5 = $null
if ($h5Start -ge 0) {
    $snippet = $text.Substring($h5Start, [Math]::Min(700, $text.Length - $h5Start))
    $h5 = [pscustomobject]@{
        CanvasHeight = Get-Number $snippet 'cvsH'
        CanvasWidth = Get-Number $snippet 'cvsW'
        DotRadius = Get-Number $snippet 'dotRadius'
        CircleRadius = Get-Number $snippet 'circleRadius'
        XLeft = Get-Number $snippet 'xLeft'
        XRight = Get-Number $snippet 'xRight'
        YStart = Get-Number $snippet 'yStart'
        LineLength = Get-Number $snippet 'lineLength'
    }
}

$imageOffsetX = $null
if ($h5 -and $h5.CanvasWidth -ge 300) {
    $imageOffsetX = ($h5.CanvasWidth - 300) / 2
}
foreach ($anchor in $anchors) {
    if ($imageOffsetX -ne $null) {
        $anchor | Add-Member -NotePropertyName ImageX -NotePropertyValue ($anchor.CanvasX - $imageOffsetX)
        $anchor | Add-Member -NotePropertyName ImageY -NotePropertyValue $anchor.CanvasY
    }
}

$keyboardAnchors = @(Get-KeyboardAnchors $text)
$keyboardImageEntries = [regex]::Matches(
    $text,
    '\./(?<pid>\d+)_(?<edition>\d+)/img_prods/(?<name>[^"/]+)-3x\.png')
$keyboardImages = @()
$keyboardImageSeen = [Collections.Generic.HashSet[string]]::new()
foreach ($entry in $keyboardImageEntries) {
    $key = $entry.Groups['name'].Value
    if ($key -eq 'prd') { continue }
    if (-not $keyboardImageSeen.Add($key)) { continue }
    $keyboardImages += [pscustomobject]@{
        Name = $key
        Url = "$base/$($entry.Groups['pid'].Value)_$($entry.Groups['edition'].Value)/img_prods/$key-3x.png"
    }
}
$keyboardImages = @($keyboardImages | Sort-Object Name)
$parseWatch.Stop()
$total.Stop()

$status = if ($anchors.Count -gt 0) {
    'mouse-anchors'
} elseif ($keyboardAnchors.Count -gt 0) {
    'keyboard-paths'
} elseif ($image3x -or $keyboardImages.Count -gt 0) {
    'image-only'
} else {
    'no-anchors'
}

$result = [pscustomobject]@{
    ProductId = $ProductId
    Status = $status
    AssetManifest = $manifestUrl
    MainJs = $mainJsUrl
    Image3x = if ($image3x) { "$base/$($image3x -replace '^\./','')" } else { $null }
    Image1x = if ($image1x) { "$base/$($image1x -replace '^\./','')" } else { $null }
    Image3xBytes = if ($image3x) { Get-RemoteLength "$base/$($image3x -replace '^\./','')" } else { 0 }
    Image1xBytes = if ($image1x) { Get-RemoteLength "$base/$($image1x -replace '^\./','')" } else { 0 }
    MainJsBytes = (Get-Item -LiteralPath $mainJsCache).Length
    FromCache = $fromCache
    HasSvgKeys = $text.Contains('svg-key')
    Canvas = $h5
    ImageOffsetX = $imageOffsetX
    MouseAnchorCount = $anchors.Count
    KeyboardAnchorCount = $keyboardAnchors.Count
    AnchorCount = $anchors.Count + $keyboardAnchors.Count
    Anchors = $anchors
    KeyboardAnchors = $keyboardAnchors
    KeyboardImageCount = $keyboardImages.Count
    KeyboardImages = $keyboardImages
    Timings = [pscustomobject]@{
        ManifestMs = [math]::Round($manifestWatch.Elapsed.TotalMilliseconds, 1)
        MainJsMs = [math]::Round($downloadWatch.Elapsed.TotalMilliseconds, 1)
        ParseMs = [math]::Round($parseWatch.Elapsed.TotalMilliseconds, 1)
        TotalMs = [math]::Round($total.Elapsed.TotalMilliseconds, 1)
    }
}

$json = $result | ConvertTo-Json -Depth 8
if ($JsonPath) {
    [IO.File]::WriteAllText($JsonPath, $json, [Text.UTF8Encoding]::new($false))
}

Write-Host ("Product {0}: {1}, mouse={2}, keyboard={3}, image3x={4}, keyboardImages={5}" -f $ProductId, $result.Status, $result.MouseAnchorCount, $result.KeyboardAnchorCount, [bool]$result.Image3x, $result.KeyboardImageCount)
Write-Host ("main.js: {0:N0} bytes, cache={1}, manifest={2} ms, js={3} ms, parse={4} ms, total={5} ms" -f $result.MainJsBytes, $result.FromCache, $result.Timings.ManifestMs, $result.Timings.MainJsMs, $result.Timings.ParseMs, $result.Timings.TotalMs)
if ($result.MouseAnchorCount -gt 0) {
    $result.Anchors | Format-Table Index, ButtonListIndex, CanvasX, CanvasY, IsLeft, ImageX, ImageY -AutoSize
}
if ($result.KeyboardAnchorCount -gt 0) {
    $result.KeyboardAnchors | Select-Object -First 12 | Format-Table InputId, Hid, Key, CenterX, CenterY, Width, Height -AutoSize
}
$json