[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$LoginEmail,

    [SecureString]$LoginPassword,

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string]$DotNetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$passwordPointer = [IntPtr]::Zero
$plainPassword = $null
$environmentNames = @(
    'Storage__RehearsalLoginEmail',
    'Storage__RehearsalLoginPassword'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

try {
    if ($null -eq $LoginPassword) {
        $LoginPassword = Read-Host 'Restore rehearsal login password' -AsSecureString
    }

    $passwordPointer =
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($LoginPassword)
    $plainPassword =
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw 'The restore rehearsal login password cannot be empty.'
    }

    $env:Storage__RehearsalLoginEmail = $LoginEmail
    $env:Storage__RehearsalLoginPassword = $plainPassword

    $apiProject = Join-Path $PSScriptRoot '..\src\Inventory.Api\Inventory.Api.csproj'
    & $DotNetPath run --no-launch-profile --configuration $Configuration --project $apiProject -- --rehearse-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Restore rehearsal failed with exit code $LASTEXITCODE."
    }
}
finally {
    $plainPassword = $null
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }

    foreach ($name in $environmentNames) {
        $previousValue = $previousEnvironment[$name]
        if ($null -eq $previousValue) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item "Env:$name" $previousValue
        }
    }
}
