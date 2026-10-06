[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Email,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$DisplayName,

    [SecureString]$Password
)

$ErrorActionPreference = 'Stop'
$passwordPointer = [IntPtr]::Zero
$plainPassword = $null
$environmentNames = @(
    'BootstrapAdmin__Email',
    'BootstrapAdmin__DisplayName',
    'BootstrapAdmin__Password'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

try {
    if ($null -eq $Password) {
        $Password = Read-Host 'Bootstrap administrator password' -AsSecureString
    }

    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw 'The bootstrap administrator password cannot be empty.'
    }

    $env:BootstrapAdmin__Email = $Email
    $env:BootstrapAdmin__DisplayName = $DisplayName
    $env:BootstrapAdmin__Password = $plainPassword

    $apiProject = Join-Path $PSScriptRoot '..\src\Inventory.Api\Inventory.Api.csproj'
    & dotnet run --project $apiProject -- --seed-admin
    if ($LASTEXITCODE -ne 0) {
        throw "Administrator seed failed with exit code $LASTEXITCODE."
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
