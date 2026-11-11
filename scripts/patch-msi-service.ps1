[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $MsiPath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember(
    'OpenDatabase', 'InvokeMethod', $null, $installer, @($resolved, 1))

function Open-View([string]$sql) {
    $view = $database.GetType().InvokeMember(
        'OpenView', 'InvokeMethod', $null, $database, @($sql))
    $null = $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    return ,$view
}

try {
    $view = Open-View 'SELECT `File`, `FileName` FROM `File`'
    $fileKey = $null
    while ($null -eq $fileKey) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) {
            break
        }
        $fileName = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(2))
        if ($fileName -eq 'OpenSynapse.App.exe' -or $fileName -like '*|OpenSynapse.App.exe') {
            $fileKey = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
        }
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
    if ($null -eq $fileKey) {
        throw 'OpenSynapse.App.exe is missing from the MSI File table.'
    }

    $upgradeRows = @()
    $view = Open-View 'SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`, `Remove`, `ActionProperty` FROM `Upgrade`'
    while ($null -ne ($record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null))) {
        $upgradeRows += [pscustomobject]@{
            UpgradeCode = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(1))
            VersionMin = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(2))
            VersionMax = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(3))
            Language = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(4))
            Attributes = $record.GetType().InvokeMember('IntegerData', 'GetProperty', $null, $record, @(5))
            Remove = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(6))
            ActionProperty = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(7))
        }
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)

    foreach ($row in $upgradeRows) {
        if ($row.ActionProperty -ne 'WIX_UPGRADE_DETECTED' -or [string]::IsNullOrWhiteSpace($row.VersionMax)) {
            continue
        }
        $attributes = [int]$row.Attributes -bor 2
        $deleteSql = "DELETE FROM ``Upgrade`` WHERE ``UpgradeCode`` = '$($row.UpgradeCode)' " +
            "AND ``VersionMin`` = '$($row.VersionMin)' AND ``VersionMax`` = '$($row.VersionMax)' " +
            "AND ``Language`` = '$($row.Language)' AND ``Attributes`` = $($row.Attributes) " +
            "AND ``Remove`` = '$($row.Remove)' AND ``ActionProperty`` = 'WIX_UPGRADE_DETECTED'"
        $view = Open-View $deleteSql
        $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)

        $insertSql = "INSERT INTO ``Upgrade`` (``UpgradeCode``, ``VersionMin``, ``VersionMax``, ``Language``, ``Attributes``, ``Remove``, ``ActionProperty``) " +
            "VALUES ('$($row.UpgradeCode)', '$($row.VersionMin)', '$($row.VersionMax)', '$($row.Language)', $attributes, '$($row.Remove)', 'WIX_UPGRADE_DETECTED')"
        $view = Open-View $insertSql
        $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
    }

    $view = Open-View "UPDATE ``InstallExecuteSequence`` SET ``Condition`` = 'NOT Installed' WHERE ``Action`` = 'RemoveExistingProducts'"
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)

    $verified = $false
    $view = Open-View "SELECT ``Attributes``, ``VersionMax`` FROM ``Upgrade`` WHERE ``ActionProperty`` = 'WIX_UPGRADE_DETECTED'"
    while ($null -ne ($record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null))) {
        $versionMax = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @(2))
        if (-not [string]::IsNullOrWhiteSpace($versionMax) -and
            (([int]$record.GetType().InvokeMember('IntegerData', 'GetProperty', $null, $record, @(1))) -band 2) -ne 0) {
            $verified = $true
        }
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
    if (-not $verified) {
        throw 'The MSI Upgrade table still excludes same-version products.'
    }

    foreach ($sql in @(
        "DELETE FROM ``CustomAction`` WHERE ``Action`` = 'InstallOpenSynapseService'",
        "DELETE FROM ``InstallExecuteSequence`` WHERE ``Action`` = 'InstallOpenSynapseService'"
    )) {
        $view = Open-View $sql
        $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
    }

    $insertAction = "INSERT INTO ``CustomAction`` (``Action``, ``Type``, ``Source``, ``Target``) " +
        "VALUES ('InstallOpenSynapseService', 3090, '$fileKey', '--install-service [UserSID]')"
    $view = Open-View $insertAction
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)

    $insertSequence = "INSERT INTO ``InstallExecuteSequence`` (``Action``, ``Condition``, ``Sequence``) " +
        "VALUES ('InstallOpenSynapseService', 'NOT REMOVE', 4005)"
    $view = Open-View $insertSequence
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)

    $view = Open-View 'SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction` WHERE `Action` = ''InstallOpenSynapseService'''
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    if ($null -eq $record) {
        throw 'The service custom action was not written.'
    }
    $values = foreach ($index in 1..4) {
        $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @([int]$index))
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
    Write-Output ("Patched {0}: {1}" -f $resolved, ($values -join ' | '))
    $database.GetType().InvokeMember('Commit', 'InvokeMethod', $null, $database, $null)
}
finally {
    # Closing the database without Commit discards a partially written transaction.
    if ($null -ne $record) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($record)
    }
    if ($null -ne $view) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)
    }
    if ($null -ne $database) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    }
    if ($null -ne $installer) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
    }
}
