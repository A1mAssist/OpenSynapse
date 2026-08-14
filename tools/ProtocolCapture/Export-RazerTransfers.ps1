[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CapturePath,
    [Parameter(Mandatory)][ValidateRange(1, 127)][int]$DeviceAddress,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$tshark = 'C:\Program Files\Wireshark\tshark.exe'
if (-not (Test-Path -LiteralPath $tshark)) {
    throw "tshark not found: $tshark"
}
$resolvedCapture = (Resolve-Path -LiteralPath $CapturePath).Path
if ([IO.Path]::GetExtension($OutputPath) -ne '.tsv') {
    throw 'OutputPath must end in .tsv.'
}

$outputDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
& $tshark -r $resolvedCapture `
    -Y "usb.device_address == $DeviceAddress && usb.capdata" `
    -T fields -E header=y -E 'separator=/t' -E quote=d -E occurrence=a `
    -e frame.number -e frame.time_relative -e usb.bus_id -e usb.device_address `
    -e usb.endpoint_address.direction -e usb.transfer_type `
    -e usb.setup.bRequest -e usb.setup.wValue -e usb.capdata |
    Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
if ($LASTEXITCODE -ne 0) {
    throw "tshark export failed with exit code $LASTEXITCODE"
}
