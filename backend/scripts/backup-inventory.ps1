[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string]$DotNetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$apiProject = Join-Path $PSScriptRoot '..\src\Inventory.Api\Inventory.Api.csproj'

& $DotNetPath run --no-launch-profile --configuration $Configuration --project $apiProject -- --backup
if ($LASTEXITCODE -ne 0) {
    throw "Inventory backup failed with exit code $LASTEXITCODE."
}
