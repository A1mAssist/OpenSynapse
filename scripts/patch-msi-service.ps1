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
    if ($null -ne $database) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    }
    if ($null -ne $installer) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
    }
}
