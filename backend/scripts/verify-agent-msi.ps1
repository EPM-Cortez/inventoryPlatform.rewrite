[CmdletBinding()]
param(
    [string]$MsiPath = (
        Join-Path $PSScriptRoot `
            '..\artifacts\agent\msi\Inventory.Agent.Installer.msi'),

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion = '1.0.1'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedMsiPath = [IO.Path]::GetFullPath($MsiPath)
if (-not (Test-Path -LiteralPath $resolvedMsiPath -PathType Leaf)) {
    throw "Agent MSI was not found at '$resolvedMsiPath'."
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember(
    'OpenDatabase',
    'InvokeMethod',
    $null,
    $installer,
    @($resolvedMsiPath, 0))

function Read-MsiRow {
    param(
        [Parameter(Mandatory)]
        [string]$Sql,

        [Parameter(Mandatory)]
        [int]$FieldCount
    )

    $view = $database.GetType().InvokeMember(
        'OpenView',
        'InvokeMethod',
        $null,
        $database,
        @($Sql))
    try {
        $view.GetType().InvokeMember(
            'Execute',
            'InvokeMethod',
            $null,
            $view,
            $null) | Out-Null
        $record = $view.GetType().InvokeMember(
            'Fetch',
            'InvokeMethod',
            $null,
            $view,
            $null)
        if ($null -eq $record) {
            return $null
        }

        [object[]]$values = @(
            for ($index = 1; $index -le $FieldCount; $index++) {
                $record.GetType().InvokeMember(
                    'StringData',
                    'GetProperty',
                    $null,
                    $record,
                    $index)
            }
        )

        return ,$values
    }
    finally {
        $view.GetType().InvokeMember(
            'Close',
            'InvokeMethod',
            $null,
            $view,
            $null) | Out-Null
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [AllowNull()]
        [string]$Actual,

        [Parameter(Mandatory)]
        [string]$Expected
    )

    if (-not [string]::Equals(
            $Actual,
            $Expected,
            [StringComparison]::Ordinal)) {
        throw "$Name mismatch. Expected '$Expected'; found '$Actual'."
    }
}

function Read-Property {
    param([Parameter(Mandatory)][string]$Name)

    $row = Read-MsiRow `
        -Sql "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$Name'" `
        -FieldCount 1
    if ($null -eq $row) {
        return $null
    }

    return $row[0]
}

function Read-RegistryValue {
    param(
        [Parameter(Mandatory)]
        [string]$Key,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $row = Read-MsiRow `
        -Sql (
            "SELECT ``Value`` FROM ``Registry`` " +
            "WHERE ``Key`` = '$Key' AND ``Name`` = '$Name'") `
        -FieldCount 1
    if ($null -eq $row) {
        return $null
    }

    return $row[0]
}

function Assert-RegistrySearch {
    param(
        [Parameter(Mandatory)]
        [string]$Signature,

        [Parameter(Mandatory)]
        [string]$Key,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $row = Read-MsiRow `
        -Sql (
            'SELECT `Root`,`Key`,`Name`,`Type` FROM `RegLocator` ' +
            "WHERE ``Signature_`` = '$Signature'") `
        -FieldCount 4
    if ($null -eq $row) {
        throw "The MSI is missing registry search '$Signature'."
    }

    Assert-Equal -Name "$Signature root" -Actual $row[0] -Expected '2'
    Assert-Equal -Name "$Signature key" -Actual $row[1] -Expected $Key
    Assert-Equal -Name "$Signature name" -Actual $row[2] -Expected $Name
    Assert-Equal -Name "$Signature type" -Actual $row[3] -Expected '18'
}

function Assert-PropertyRestoreAction {
    param(
        [Parameter(Mandatory)]
        [string]$Action,

        [Parameter(Mandatory)]
        [string]$Property,

        [Parameter(Mandatory)]
        [string]$Value,

        [Parameter(Mandatory)]
        [string]$Condition
    )

    $customAction = Read-MsiRow `
        -Sql (
            'SELECT `Type`,`Source`,`Target` FROM `CustomAction` ' +
            "WHERE ``Action`` = '$Action'") `
        -FieldCount 3
    if ($null -eq $customAction) {
        throw "The MSI is missing property action '$Action'."
    }

    Assert-Equal -Name "$Action type" -Actual $customAction[0] `
        -Expected '307'
    Assert-Equal -Name "$Action property" -Actual $customAction[1] `
        -Expected $Property
    Assert-Equal -Name "$Action value" -Actual $customAction[2] `
        -Expected $Value

    foreach ($sequenceTable in @(
            'InstallExecuteSequence',
            'InstallUISequence')) {
        $scheduled = Read-MsiRow `
            -Sql (
                "SELECT ``Condition`` FROM ``$sequenceTable`` " +
                "WHERE ``Action`` = '$Action'") `
            -FieldCount 1
        if ($null -eq $scheduled) {
            throw "$Action is not scheduled in $sequenceTable."
        }

        Assert-Equal -Name "$Action condition" -Actual $scheduled[0] `
            -Expected $Condition
    }
}

function Assert-LaunchCondition {
    param([Parameter(Mandatory)][string]$Condition)

    $row = Read-MsiRow `
        -Sql (
            'SELECT `Condition` FROM `LaunchCondition` ' +
            "WHERE ``Condition`` = '$Condition'") `
        -FieldCount 1
    if ($null -eq $row) {
        throw "The MSI is missing launch condition '$Condition'."
    }
}

function Assert-LaunchConditionScenario {
    param(
        [Parameter(Mandatory)]
        [__ComObject]$Session,

        [Parameter(Mandatory)]
        [string]$Condition,

        [Parameter(Mandatory)]
        [string]$Scenario,

        [Parameter(Mandatory)]
        [hashtable]$Properties,

        [Parameter(Mandatory)]
        [ValidateSet(0, 1)]
        [int]$Expected
    )

    $scenarioProperties = @(
        'Installed',
        'REMOVE',
        'LEGACY_AGENT_PRODUCT_FOUND',
        'LEGACY_AGENT_SERVICE_FOUND',
        'LEGACY_AGENT_MARKER_FOUND')
    foreach ($propertyName in $scenarioProperties) {
        $Session.Property($propertyName) = ''
    }

    foreach ($entry in $Properties.GetEnumerator()) {
        if ($scenarioProperties -notcontains $entry.Key) {
            throw "Unexpected simulated MSI property '$($entry.Key)'."
        }

        $Session.Property($entry.Key) = [string]$entry.Value
    }

    $actual = $Session.EvaluateCondition($Condition)
    if ($actual -ne $Expected) {
        throw (
            "Legacy collision condition failed scenario '$Scenario'. " +
            "Expected '$Expected'; found '$actual'.")
    }
}

Assert-Equal `
    -Name 'Product name' `
    -Actual (Read-Property -Name 'ProductName') `
    -Expected 'Inventory Endpoint Agent'
Assert-Equal `
    -Name 'Product version' `
    -Actual (Read-Property -Name 'ProductVersion') `
    -Expected $ExpectedVersion
Assert-Equal `
    -Name 'Install scope' `
    -Actual (Read-Property -Name 'ALLUSERS') `
    -Expected '1'

$secureProperties = Read-Property -Name 'SecureCustomProperties'
foreach ($requiredProperty in @(
        'ALLOW_SOFTWARE_KEY',
        'API_BASE_URL',
        'ENROLLMENT_TOKEN',
        'ENROLLMENT_TOKEN_FILE')) {
    if (($secureProperties -split ';') -notcontains $requiredProperty) {
        throw "SecureCustomProperties is missing '$requiredProperty'."
    }
}

$hiddenProperties = Read-Property -Name 'MsiHiddenProperties'
foreach ($hiddenName in @(
        'DeleteEnrollmentToken',
        'ENROLLMENT_TOKEN',
        'RollbackEnrollmentToken',
        'StageEnrollmentToken')) {
    if (($hiddenProperties -split ';') -notcontains $hiddenName) {
        throw "MsiHiddenProperties is missing $hiddenName."
    }
}

$tokenAction = Read-MsiRow `
    -Sql (
        'SELECT `Type`,`Source`,`Target` FROM `CustomAction` ' +
        "WHERE ``Action`` = 'StageEnrollmentToken'") `
    -FieldCount 3
if ($null -eq $tokenAction) {
    throw 'The MSI is missing the enrollment-token staging action.'
}
Assert-Equal -Name 'Enrollment-token action type' `
    -Actual $tokenAction[0] -Expected '11282'
Assert-Equal -Name 'Enrollment-token action executable' `
    -Actual $tokenAction[1] -Expected 'InventoryAgentExecutable'
Assert-Equal -Name 'Enrollment-token action arguments' `
    -Actual $tokenAction[2] `
    -Expected (
        '--stage-enrollment-token "[ENROLLMENT_TOKEN]" ' +
        '--data-directory "[AGENTDATADIR]"')

$rollbackTokenAction = Read-MsiRow `
    -Sql (
        'SELECT `Type`,`Source`,`Target` FROM `CustomAction` ' +
        "WHERE ``Action`` = 'RollbackEnrollmentToken'") `
    -FieldCount 3
if ($null -eq $rollbackTokenAction) {
    throw 'The MSI is missing enrollment-token rollback cleanup.'
}
Assert-Equal -Name 'Enrollment-token rollback type' `
    -Actual $rollbackTokenAction[0] -Expected '11538'
Assert-Equal -Name 'Enrollment-token rollback executable' `
    -Actual $rollbackTokenAction[1] -Expected 'InventoryAgentExecutable'
Assert-Equal -Name 'Enrollment-token rollback arguments' `
    -Actual $rollbackTokenAction[2] `
    -Expected (
        '--delete-enrollment-token ' +
        '--data-directory "[AGENTDATADIR]"')

$deleteTokenAction = Read-MsiRow `
    -Sql (
        'SELECT `Type`,`Source`,`Target` FROM `CustomAction` ' +
        "WHERE ``Action`` = 'DeleteEnrollmentToken'") `
    -FieldCount 3
if ($null -eq $deleteTokenAction) {
    throw 'The MSI is missing enrollment-token uninstall cleanup.'
}
Assert-Equal -Name 'Enrollment-token uninstall cleanup type' `
    -Actual $deleteTokenAction[0] -Expected '11282'
Assert-Equal -Name 'Enrollment-token uninstall cleanup executable' `
    -Actual $deleteTokenAction[1] -Expected 'InventoryAgentExecutable'
Assert-Equal -Name 'Enrollment-token uninstall cleanup arguments' `
    -Actual $deleteTokenAction[2] `
    -Expected (
        '--delete-enrollment-token ' +
        '--data-directory "[AGENTDATADIR]"')

$tokenActionSequence = Read-MsiRow `
    -Sql (
        'SELECT `Condition`,`Sequence` FROM `InstallExecuteSequence` ' +
        "WHERE ``Action`` = 'StageEnrollmentToken'") `
    -FieldCount 2
if ($null -eq $tokenActionSequence) {
    throw 'The enrollment-token staging action is not scheduled.'
}
Assert-Equal -Name 'Enrollment-token action condition' `
    -Actual $tokenActionSequence[0] `
    -Expected 'ENROLLMENT_TOKEN AND NOT REMOVE~="ALL"'
$installFilesSequence = Read-MsiRow `
    -Sql (
        'SELECT `Sequence` FROM `InstallExecuteSequence` ' +
        "WHERE ``Action`` = 'InstallFiles'") `
    -FieldCount 1
$rollbackTokenSequence = Read-MsiRow `
    -Sql (
        'SELECT `Condition`,`Sequence` FROM `InstallExecuteSequence` ' +
        "WHERE ``Action`` = 'RollbackEnrollmentToken'") `
    -FieldCount 2
$deleteTokenSequence = Read-MsiRow `
    -Sql (
        'SELECT `Condition`,`Sequence` FROM `InstallExecuteSequence` ' +
        "WHERE ``Action`` = 'DeleteEnrollmentToken'") `
    -FieldCount 2
$removeFilesSequence = Read-MsiRow `
    -Sql (
        'SELECT `Sequence` FROM `InstallExecuteSequence` ' +
        "WHERE ``Action`` = 'RemoveFiles'") `
    -FieldCount 1
$startServicesSequence = Read-MsiRow `
    -Sql (
        'SELECT `Sequence` FROM `InstallExecuteSequence` ' +
    "WHERE ``Action`` = 'StartServices'") `
    -FieldCount 1
if ($null -eq $installFilesSequence -or
    $null -eq $rollbackTokenSequence -or
    $null -eq $deleteTokenSequence -or
    $null -eq $removeFilesSequence -or
    $null -eq $startServicesSequence -or
    [int]$rollbackTokenSequence[1] -le [int]$installFilesSequence[0] -or
    [int]$rollbackTokenSequence[1] -ge [int]$tokenActionSequence[1] -or
    [int]$tokenActionSequence[1] -le [int]$installFilesSequence[0] -or
    [int]$tokenActionSequence[1] -ge [int]$startServicesSequence[0] -or
    [int]$deleteTokenSequence[1] -ge [int]$removeFilesSequence[0]) {
    throw (
        'Enrollment-token cleanup/staging actions are incorrectly ordered.')
}
Assert-Equal -Name 'Enrollment-token rollback condition' `
    -Actual $rollbackTokenSequence[0] `
    -Expected 'ENROLLMENT_TOKEN AND NOT REMOVE~="ALL"'
Assert-Equal -Name 'Enrollment-token uninstall cleanup condition' `
    -Actual $deleteTokenSequence[0] `
    -Expected 'REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE'

$service = Read-MsiRow `
    -Sql (
        'SELECT `Name`,`DisplayName`,`ServiceType`,`StartType`,' +
        '`StartName` FROM `ServiceInstall`') `
    -FieldCount 5
if ($null -eq $service) {
    throw 'The MSI does not install a Windows service.'
}

Assert-Equal -Name 'Service name' -Actual $service[0] `
    -Expected 'InventoryEndpointAgent'
Assert-Equal -Name 'Service display name' -Actual $service[1] `
    -Expected 'Inventory Endpoint Agent'
Assert-Equal -Name 'Service type' -Actual $service[2] -Expected '16'
Assert-Equal -Name 'Service start type' -Actual $service[3] -Expected '2'
Assert-Equal -Name 'Service account' -Actual $service[4] `
    -Expected 'NT SERVICE\InventoryEndpointAgent'

$recovery = Read-MsiRow `
    -Sql (
        'SELECT `FirstFailureActionType`,`SecondFailureActionType`,' +
        '`ThirdFailureActionType`,`RestartServiceDelayInSeconds` ' +
        'FROM `Wix4ServiceConfig`') `
    -FieldCount 4
if ($null -eq $recovery) {
    throw 'The MSI does not configure service recovery.'
}

Assert-Equal -Name 'First recovery action' -Actual $recovery[0] `
    -Expected 'restart'
Assert-Equal -Name 'Second recovery action' -Actual $recovery[1] `
    -Expected 'restart'
Assert-Equal -Name 'Third recovery action' -Actual $recovery[2] `
    -Expected 'restart'
Assert-Equal -Name 'Recovery delay' -Actual $recovery[3] -Expected '60'

Assert-Equal `
    -Name 'API registry value' `
    -Actual (Read-RegistryValue `
        -Key 'SOFTWARE\Inventory\Agent' `
        -Name 'ServerBaseUrl') `
    -Expected '[API_BASE_URL]'
Assert-Equal `
    -Name 'Enrollment-token path registry value' `
    -Actual (Read-RegistryValue `
        -Key 'SOFTWARE\Inventory\Agent' `
        -Name 'EnrollmentTokenFilePath') `
    -Expected '[ENROLLMENT_TOKEN_FILE]'
Assert-Equal `
    -Name 'Software-key registry value' `
    -Actual (Read-RegistryValue `
        -Key 'SOFTWARE\Inventory\Agent' `
        -Name 'AllowSoftwareKey') `
    -Expected '#[ALLOW_SOFTWARE_KEY]'
Assert-Equal `
    -Name 'Event-log message file' `
    -Actual (Read-RegistryValue `
        -Key (
            'SYSTEM\CurrentControlSet\Services\EventLog\Application\' +
            'Inventory Endpoint Agent') `
        -Name 'EventMessageFile') `
    -Expected '#%[INSTALLFOLDER]Inventory.Agent.exe'
Assert-Equal `
    -Name 'Event-log source types' `
    -Actual (Read-RegistryValue `
        -Key (
            'SYSTEM\CurrentControlSet\Services\EventLog\Application\' +
            'Inventory Endpoint Agent') `
        -Name 'TypesSupported') `
    -Expected '#7'

Assert-RegistrySearch `
    -Signature 'ExistingApiBaseUrlSearch' `
    -Key 'SOFTWARE\Inventory\Agent' `
    -Name 'ServerBaseUrl'
Assert-RegistrySearch `
    -Signature 'ExistingEnrollmentTokenFileSearch' `
    -Key 'SOFTWARE\Inventory\Agent' `
    -Name 'EnrollmentTokenFilePath'
Assert-RegistrySearch `
    -Signature 'ExistingAllowSoftwareKeySearch' `
    -Key 'SOFTWARE\Inventory\Agent' `
    -Name 'AllowSoftwareKey'
Assert-RegistrySearch `
    -Signature 'LegacyAgentServiceSearch' `
    -Key 'SYSTEM\CurrentControlSet\Services\InventoryTpmAgent' `
    -Name 'ImagePath'
Assert-RegistrySearch `
    -Signature 'LegacyAgentMarkerSearch' `
    -Key 'SOFTWARE\Inventory\Agent' `
    -Name 'Installed'

Assert-PropertyRestoreAction `
    -Action 'RestoreApiBaseUrl' `
    -Property 'API_BASE_URL' `
    -Value '[EXISTING_API_BASE_URL]' `
    -Condition 'NOT API_BASE_URL AND EXISTING_API_BASE_URL'
Assert-PropertyRestoreAction `
    -Action 'RestoreEnrollmentTokenFile' `
    -Property 'ENROLLMENT_TOKEN_FILE' `
    -Value '[EXISTING_ENROLLMENT_TOKEN_FILE]' `
    -Condition (
        'NOT ENROLLMENT_TOKEN AND NOT ENROLLMENT_TOKEN_FILE AND ' +
        'EXISTING_ENROLLMENT_TOKEN_FILE')
Assert-PropertyRestoreAction `
    -Action 'RestoreAllowSoftwareKeyTrue' `
    -Property 'ALLOW_SOFTWARE_KEY' `
    -Value '1' `
    -Condition (
        'NOT ALLOW_SOFTWARE_KEY AND ' +
        '(EXISTING_ALLOW_SOFTWARE_KEY = "#1" OR ' +
        'EXISTING_ALLOW_SOFTWARE_KEY = "1" OR ' +
        'EXISTING_ALLOW_SOFTWARE_KEY = "true")')
Assert-PropertyRestoreAction `
    -Action 'RestoreAllowSoftwareKeyFalse' `
    -Property 'ALLOW_SOFTWARE_KEY' `
    -Value '0' `
    -Condition (
        'NOT ALLOW_SOFTWARE_KEY AND ' +
        '(EXISTING_ALLOW_SOFTWARE_KEY = "#0" OR ' +
        'EXISTING_ALLOW_SOFTWARE_KEY = "0" OR ' +
        'EXISTING_ALLOW_SOFTWARE_KEY = "false")')
Assert-PropertyRestoreAction `
    -Action 'DefaultAllowSoftwareKey' `
    -Property 'ALLOW_SOFTWARE_KEY' `
    -Value '0' `
    -Condition 'NOT ALLOW_SOFTWARE_KEY'

$legacyUpgrade = Read-MsiRow `
    -Sql (
        'SELECT `VersionMin`,`VersionMax`,`Attributes`,`ActionProperty` ' +
        'FROM `Upgrade` WHERE `UpgradeCode` = ' +
        "'{A170CC8E-A7F1-4FDA-B66B-A9DB8642F6EF}'") `
    -FieldCount 4
if ($null -eq $legacyUpgrade) {
    throw 'The MSI does not detect the legacy Inventory TPM Agent product.'
}

Assert-Equal -Name 'Legacy version minimum' -Actual $legacyUpgrade[0] `
    -Expected '0.0.0'
Assert-Equal -Name 'Legacy detection attributes' -Actual $legacyUpgrade[2] `
    -Expected '258'
Assert-Equal -Name 'Legacy detection property' -Actual $legacyUpgrade[3] `
    -Expected 'LEGACY_AGENT_PRODUCT_FOUND'

Assert-LaunchCondition -Condition 'Installed OR API_BASE_URL'
Assert-LaunchCondition -Condition (
    'NOT (ENROLLMENT_TOKEN AND ENROLLMENT_TOKEN_FILE)')
Assert-LaunchCondition -Condition (
    'Installed OR ALLOW_SOFTWARE_KEY = "0" OR ' +
    'ALLOW_SOFTWARE_KEY = "1"')
Assert-LaunchCondition -Condition (
    'REMOVE~="ALL" OR (NOT LEGACY_AGENT_PRODUCT_FOUND AND ' +
    'NOT LEGACY_AGENT_SERVICE_FOUND AND ' +
    'NOT LEGACY_AGENT_MARKER_FOUND)')

$legacyCollisionCondition =
    'REMOVE~="ALL" OR (NOT LEGACY_AGENT_PRODUCT_FOUND AND ' +
    'NOT LEGACY_AGENT_SERVICE_FOUND AND ' +
    'NOT LEGACY_AGENT_MARKER_FOUND)'
$validationSession = $installer.OpenPackage($resolvedMsiPath, 1)
Assert-LaunchConditionScenario `
    -Session $validationSession `
    -Condition $legacyCollisionCondition `
    -Scenario 'clean repair' `
    -Properties @{ Installed = '1' } `
    -Expected 1
Assert-LaunchConditionScenario `
    -Session $validationSession `
    -Condition $legacyCollisionCondition `
    -Scenario 'repair with legacy product' `
    -Properties @{
        Installed = '1'
        LEGACY_AGENT_PRODUCT_FOUND = '1'
    } `
    -Expected 0
Assert-LaunchConditionScenario `
    -Session $validationSession `
    -Condition $legacyCollisionCondition `
    -Scenario 'repair with legacy service' `
    -Properties @{
        Installed = '1'
        LEGACY_AGENT_SERVICE_FOUND = '1'
    } `
    -Expected 0
Assert-LaunchConditionScenario `
    -Session $validationSession `
    -Condition $legacyCollisionCondition `
    -Scenario 'repair with legacy marker' `
    -Properties @{
        Installed = '1'
        LEGACY_AGENT_MARKER_FOUND = '1'
    } `
    -Expected 0
Assert-LaunchConditionScenario `
    -Session $validationSession `
    -Condition $legacyCollisionCondition `
    -Scenario 'partial feature removal with legacy collision' `
    -Properties @{
        Installed = '1'
        REMOVE = 'MainFeature'
        LEGACY_AGENT_PRODUCT_FOUND = '1'
    } `
    -Expected 0
Assert-LaunchConditionScenario `
    -Session $validationSession `
    -Condition $legacyCollisionCondition `
    -Scenario 'full uninstall with all legacy signals' `
    -Properties @{
        Installed = '1'
        REMOVE = 'ALL'
        LEGACY_AGENT_PRODUCT_FOUND = '1'
        LEGACY_AGENT_SERVICE_FOUND = '1'
        LEGACY_AGENT_MARKER_FOUND = '1'
    } `
    -Expected 1
Assert-LaunchConditionScenario `
    -Session $validationSession `
    -Condition $legacyCollisionCondition `
    -Scenario 'case-insensitive full uninstall' `
    -Properties @{
        Installed = '1'
        REMOVE = 'all'
        LEGACY_AGENT_SERVICE_FOUND = '1'
    } `
    -Expected 1

$expectedAcl =
    'D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)' +
    '(A;OICI;FA;;;S-1-5-80-2609206542-791502006-4278732640-' +
    '3842405600-3050437999)'
$acl = Read-MsiRow `
    -Sql (
        'SELECT `SDDLText` FROM `MsiLockPermissionsEx` ' +
        "WHERE ``LockObject`` = 'AGENTDATADIR' " +
        "AND ``Table`` = 'CreateFolder'") `
    -FieldCount 1
if ($null -eq $acl) {
    throw 'The MSI does not apply the endpoint-agent data ACL.'
}

Assert-Equal -Name 'Agent data ACL' -Actual $acl[0] -Expected $expectedAcl

Write-Output (
    "Agent MSI verification passed for '$resolvedMsiPath' " +
    "(version $ExpectedVersion).")
