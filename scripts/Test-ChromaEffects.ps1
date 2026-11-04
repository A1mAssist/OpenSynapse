# Validates .ChromaEffects structure observed from Chroma Studio 4.0.896
# (master-m16-beta, commit 99be5166c0d858a84035ff9c350a17053d3022e1).
# Evidence: artifacts/chroma-studio-2026-11-03/frontend-full/analysis/
# ModalImport.873c1a25.chunk.beautified.js and ModalExport.59280937.chunk.beautified.js.
# The chroma-effects.schema.json in that folder is a derived observation, not a
# Razer-published schema. Razer ships no JSON Schema; these checks mirror
# StudioChromaImport.Validate.
# This is evidence-backed structural validation, not an official schema.
# Unknown properties are allowed.
#
# No -Path: validates the real fixtures and requires every fixture under
# Fixtures/Chroma/Invalid to fail. With -Path: reports each file and exits 1
# when any file is invalid.
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Path
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $root 'tests/OpenSynapse.Core.Tests/Fixtures/Chroma'
$invalidRoot = Join-Path $fixtureRoot 'Invalid'
$allowedEffects = @(
    'ambient', 'audio', 'breathing', 'chroma-generate', 'fire', 'reactive',
    'generate', 'gradient', 'radiate', 'ripple', 'spectrum', 'starlight',
    'static', 'tidal', 'wave', 'wheel'
)

function Add-Error {
    param([System.Collections.Generic.List[string]] $Errors, [string] $Where, [string] $Message)
    $Errors.Add("${Where}: $Message")
}

function Test-HasProperty {
    param([object] $Object, [string] $Name)
    return $null -ne $Object.PSObject.Properties[$Name]
}

function Test-Object {
    param([object] $Value, [string] $Where, [System.Collections.Generic.List[string]] $Errors)
    if ($null -eq $Value -or $Value -isnot [pscustomobject]) {
        Add-Error $Errors $Where 'expected object'
        return $false
    }
    return $true
}

function Test-Array {
    param([object] $Value, [string] $Where, [System.Collections.Generic.List[string]] $Errors)
    if ($null -eq $Value -or $Value -isnot [array]) {
        Add-Error $Errors $Where 'expected array'
        return $false
    }
    return $true
}

function Test-Integer {
    param([object] $Value)
    if ($Value -is [bool] -or $Value -isnot [ValueType]) { return $false }
    $number = [double] $Value
    return $number -eq [math]::Floor($number)
}

function Test-IntegerField {
    param(
        [object] $Object,
        [string] $Name,
        [string] $Where,
        [System.Collections.Generic.List[string]] $Errors,
        [long] $Minimum = 0,
        [long] $Maximum = [long]::MaxValue
    )
    if (-not (Test-HasProperty $Object $Name)) { return }
    $value = $Object.$Name
    if (-not (Test-Integer $value) -or [double] $value -lt $Minimum -or [double] $value -gt $Maximum) {
        Add-Error $Errors "$Where.$Name" "expected an integer from $Minimum to $Maximum"
    }
}

function Test-OptionalObject {
    param([object] $Object, [string] $Name, [string] $Where, [System.Collections.Generic.List[string]] $Errors)
    if (-not (Test-HasProperty $Object $Name)) { return }
    if ($null -ne $Object.$Name -and $Object.$Name -isnot [pscustomobject]) {
        Add-Error $Errors "$Where.$Name" 'expected object'
    }
}

function Test-Led {
    param([object] $Led, [string] $Where, [System.Collections.Generic.List[string]] $Errors)
    if (-not (Test-Object $Led $Where $Errors)) { return }
    foreach ($axis in @('row', 'col')) {
        if (-not (Test-HasProperty $Led $axis)) {
            Add-Error $Errors "$Where.$axis" 'expected a non-negative integer'
            continue
        }
        $value = $Led.$axis
        if (-not (Test-Integer $value) -or [double] $value -lt 0) {
            Add-Error $Errors "$Where.$axis" 'expected a non-negative integer'
        }
    }
    Test-IntegerField $Led 'regionId' $Where $Errors
    Test-IntegerField $Led 'regionIndex' $Where $Errors
}

function Test-Identity {
    param([object] $Device, [string] $Where, [System.Collections.Generic.List[string]] $Errors)
    if (-not (Test-Object $Device $Where $Errors)) { return }
    Test-IntegerField $Device 'productId' $Where $Errors 0 65535
    if (Test-HasProperty $Device 'instanceId') {
        $instance = $Device.instanceId
        if ($null -ne $instance -and $instance -isnot [string] -and
            (-not (Test-Integer $instance) -or [double] $instance -lt 0)) {
            Add-Error $Errors "$Where.instanceId" 'expected a non-negative integer, string or null'
        }
    }
    foreach ($field in @('deviceId', 'layout')) {
        if (-not (Test-HasProperty $Device $field)) { continue }
        $value = $Device.$field
        if ($null -ne $value -and $value -isnot [string] -and $value -isnot [ValueType] -or $value -is [bool]) {
            Add-Error $Errors "$Where.$field" 'expected a string, number or null'
        }
    }
    if (Test-HasProperty $Device 'orientation') {
        if (-not (Test-Integer $Device.orientation) -or $Device.orientation -notin @(0, 90, 180, 270)) {
            Add-Error $Errors "$Where.orientation" 'expected 0, 90, 180 or 270'
        }
    }
    if (Test-HasProperty $Device 'leds' -and (Test-Array $Device.leds "$Where.leds" $Errors)) {
        $index = 0
        foreach ($led in $Device.leds) {
            Test-Led $led "$Where.leds[$index]" $Errors
            $index++
        }
    }
}

function Test-Region {
    param([object] $Region, [string] $Where, [System.Collections.Generic.List[string]] $Errors)
    if (-not (Test-Object $Region $Where $Errors)) { return }
    Test-OptionalObject $Region 'params' $Where $Errors
    Test-OptionalObject $Region 'params2' $Where $Errors
    if (-not (Test-HasProperty $Region 'devices')) { return }
    if (-not (Test-Array $Region.devices "$Where.devices" $Errors)) { return }
    $index = 0
    foreach ($device in $Region.devices) {
        Test-Identity $device "$Where.devices[$index]" $Errors
        $index++
    }
}

function Test-Layer {
    param([object] $Layer, [string] $Where, [System.Collections.Generic.List[string]] $Errors)
    if (-not (Test-Object $Layer $Where $Errors)) { return }

    $type = $null
    if (Test-HasProperty $Layer 'type') {
        $type = [string] $Layer.type
        if ($type -notin @('layer', 'group')) {
            Add-Error $Errors "$Where.type" "expected layer or group, got '$type'"
            return
        }
    }
    $isGroup = $type -eq 'group' -or (Test-HasProperty $Layer 'isGroup' -and $Layer.isGroup -eq $true)
    if ($isGroup) {
        if (-not (Test-HasProperty $Layer 'layers')) { return }
        if (-not (Test-Array $Layer.layers "$Where.layers" $Errors)) { return }
        $index = 0
        foreach ($child in $Layer.layers) {
            Test-Layer $child "$Where.layers[$index]" $Errors
            $index++
        }
        return
    }

    if (Test-HasProperty $Layer 'layers') {
        if ($Layer.layers -isnot [array]) {
            Add-Error $Errors "$Where.layers" 'expected array'
        } elseif ($Layer.layers.Count -gt 0) {
            Add-Error $Errors "$Where.layers" "child layers require type 'group'"
        }
    }
    if (Test-HasProperty $Layer 'name' -and $null -ne $Layer.name) {
        $name = [string] $Layer.name
        if ($name -and $allowedEffects -notcontains $name) {
            Add-Error $Errors "$Where.name" "unknown effect '$name'"
        }
    }
    Test-OptionalObject $Layer 'params' $Where $Errors
    Test-OptionalObject $Layer 'params2' $Where $Errors
    if (-not (Test-HasProperty $Layer 'regions')) { return }
    if (-not (Test-Array $Layer.regions "$Where.regions" $Errors)) { return }
    $index = 0
    foreach ($region in $Layer.regions) {
        Test-Region $region "$Where.regions[$index]" $Errors
        $index++
    }
}

function Test-Profile {
    param([object] $Profile, [string] $Where, [System.Collections.Generic.List[string]] $Errors)
    if (-not (Test-Object $Profile $Where $Errors)) { return }
    foreach ($field in @('id', 'title')) {
        if (-not (Test-HasProperty $Profile $field)) { continue }
        if ($null -ne $Profile.$field -and [string]::IsNullOrWhiteSpace([string] $Profile.$field)) {
            Add-Error $Errors "$Where.$field" 'must not be empty'
        }
    }
    if (Test-HasProperty $Profile 'default' -and $Profile.default -isnot [bool]) {
        Add-Error $Errors "$Where.default" 'expected boolean'
    }
    if (Test-HasProperty $Profile 'devices') {
        if (Test-Array $Profile.devices "$Where.devices" $Errors) {
            $index = 0
            foreach ($device in $Profile.devices) {
                Test-Identity $device "$Where.devices[$index]" $Errors
                $index++
            }
        }
    }
    if (-not (Test-HasProperty $Profile 'layers')) {
        Add-Error $Errors "$Where.layers" 'expected array'
        return
    }
    if (-not (Test-Array $Profile.layers "$Where.layers" $Errors)) { return }
    $index = 0
    foreach ($layer in $Profile.layers) {
        Test-Layer $layer "$Where.layers[$index]" $Errors
        $index++
    }
}

function Test-ChromaFile {
    param([string] $File, [switch] $ExpectInvalid)

    $errors = [System.Collections.Generic.List[string]]::new()
    $valid = $false
    try {
        $json = Get-Content -LiteralPath $File -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 100
        if (Test-Object $json 'root' $errors) {
            if ((Test-HasProperty $json 'version') -and
                ((-not (Test-Integer $json.version)) -or $json.version -ne 1)) {
                Add-Error $errors 'root.version' "expected 1, got $($json.version)"
            } elseif (-not (Test-HasProperty $json 'profiles') -or -not (Test-Array $json.profiles 'root.profiles' $errors)) {
            } elseif ($json.profiles.Count -eq 0) {
                Add-Error $errors 'root.profiles' 'must not be empty'
            } else {
                $index = 0
                foreach ($profile in $json.profiles) {
                    Test-Profile $profile "profiles[$index]" $errors
                    $index++
                }
            }
        }
        $valid = $errors.Count -eq 0
    } catch {
        Add-Error $errors 'root' $_.Exception.Message
    }

    if ($ExpectInvalid) {
        if ($valid) {
            Write-Host "UNEXPECTEDLY VALID $File"
            return $false
        }
        Write-Host "INVALID (expected) $File"
        return $true
    }
    if ($valid) {
        Write-Host "VALID   $File"
        return $true
    }
    Write-Host "INVALID $File"
    foreach ($error in $errors) { Write-Host "  $error" }
    return $false
}

function Get-ChromaFileKind {
    param([string] $File)
    $bytes = [System.IO.File]::ReadAllBytes($File)
    if ($bytes.Length -ge 4 -and $bytes[0] -eq 0x50 -and $bytes[1] -eq 0x4B -and
        $bytes[2] -in 3, 5, 7 -and $bytes[3] -in 4, 6, 8) {
        return 'zip'
    }

    $index = 0
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $index = 3
    }
    if ($bytes.Length -ge 4 -and
        (($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) -or
         ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF))) {
        return 'xml'
    }
    while ($index -lt $bytes.Length -and $bytes[$index] -in 0x20, 0x09, 0x0D, 0x0A) { $index++ }
    if ($index -lt $bytes.Length -and $bytes[$index] -eq 0x3C) { return 'xml' }
    return 'json'
}

function Read-TextEntry {
    param([System.IO.Stream] $Stream)
    $reader = [System.IO.StreamReader]::new($Stream, [System.Text.Encoding]::UTF8, $true)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Test-LegacyFile {
    param([string] $File)

    try {
        $kind = Get-ChromaFileKind $File
        $found = $false
        if ($kind -eq 'zip') {
            Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
            $archive = [System.IO.Compression.ZipFile]::OpenRead($File)
            try {
                foreach ($entry in $archive.Entries) {
                    if ($entry.Length -eq 0 -or [string]::IsNullOrEmpty($entry.Name)) { continue }
                    $stream = $entry.Open()
                    try { $text = Read-TextEntry $stream } finally { $stream.Dispose() }
                    try { $document = [xml] $text } catch { continue }
                    if ($null -ne $document.SelectSingleNode('//LightingEffects')) {
                        $found = $true
                        break
                    }
                }
            }
            finally {
                $archive.Dispose()
            }
        } else {
            $stream = [System.IO.File]::OpenRead($File)
            try { $text = Read-TextEntry $stream } finally { $stream.Dispose() }
            $document = [xml] $text
            $found = $null -ne $document.SelectSingleNode('//LightingEffects')
        }

        if (-not $found) {
            Write-Host "INVALID $File"
            Write-Host '  no LightingEffects XML profile'
            return $false
        }
        Write-Host "VALID   $File (Synapse 3)"
        return $true
    } catch {
        Write-Host "INVALID $File"
        Write-Host "  $($_.Exception.Message)"
        return $false
    }
}

$files = @()
if (-not $Path -or $Path.Count -eq 0) {
    $files = @(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.ChromaEffects' -File | ForEach-Object FullName)
    if (Test-Path -LiteralPath $invalidRoot) {
        $files += @(Get-ChildItem -LiteralPath $invalidRoot -Filter '*.ChromaEffects' -File | ForEach-Object FullName)
    }
    if ($files.Count -eq 0) {
        Write-Host "INVALID $fixtureRoot"
        Write-Host '  no .ChromaEffects fixtures found'
        exit 1
    }
} else {
    $files = $Path
}

$ok = $true
foreach ($file in $files) {
    $expectInvalid = -not ($Path -and $Path.Count -gt 0) -and $file.StartsWith($invalidRoot, [StringComparison]::OrdinalIgnoreCase)
    if ((Get-ChromaFileKind $file) -eq 'json') {
        if (-not (Test-ChromaFile -File $file -ExpectInvalid:$expectInvalid)) { $ok = $false }
        continue
    }

    $valid = Test-LegacyFile -File $file
    if ($expectInvalid) {
        if ($valid) {
            Write-Host "UNEXPECTEDLY VALID $file"
            $ok = $false
        } else {
            Write-Host "INVALID (expected) $file"
        }
    } elseif (-not $valid) {
        $ok = $false
    }
}
if (-not $ok) { exit 1 }