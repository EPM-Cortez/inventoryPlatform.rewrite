[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.1',

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$CertificateThumbprint,

    [uri]$TimestampUrl = 'http://timestamp.digicert.com',

    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\agent'))
$publishDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot 'publish'))
$msiDirectory = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot 'msi'))

if (-not $artifactsRoot.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The agent artifacts directory resolved outside the rewrite workspace.'
}

if ($RequireSignature -and [string]::IsNullOrWhiteSpace(
        $CertificateThumbprint)) {
    throw 'CertificateThumbprint is required when RequireSignature is set.'
}

foreach ($directory in @($publishDirectory, $msiDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $directory | Out-Null
}

function Invoke-CodeSign {
    param([Parameter(Mandatory)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        return
    }

    $signTool = Get-Command 'signtool.exe' -ErrorAction Stop
    & $signTool.Source sign `
        /sha1 $CertificateThumbprint `
        /fd SHA256 `
        /tr $TimestampUrl.AbsoluteUri `
        /td SHA256 `
        $Path
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode signing failed for $Path."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode verification failed for ${Path}: $($signature.Status)."
    }
}

Push-Location $repositoryRoot
try {
    dotnet publish 'src\Inventory.Agent\Inventory.Agent.csproj' `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        --output $publishDirectory `
        -p:Version=$Version
    if ($LASTEXITCODE -ne 0) {
        throw 'Agent publish failed.'
    }

    $agentExecutable = Join-Path $publishDirectory 'Inventory.Agent.exe'
    if (-not (Test-Path -LiteralPath $agentExecutable)) {
        throw 'The published agent executable was not produced.'
    }

    Invoke-CodeSign -Path $agentExecutable

    dotnet build `
        'installer\Inventory.Agent\Inventory.Agent.Installer.wixproj' `
        --configuration $Configuration `
        -p:PublishDir=$publishDirectory `
        -p:InstallerVersion=$Version `
        -p:OutputPath=$msiDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'Agent MSI build failed.'
    }
}
finally {
    Pop-Location
}

$msi = Get-ChildItem -LiteralPath $msiDirectory -Filter '*.msi' -File |
    Select-Object -First 1
if ($null -eq $msi) {
    throw 'The agent MSI was not produced.'
}

Invoke-CodeSign -Path $msi.FullName

$files = @($agentExecutable, $msi.FullName) | ForEach-Object {
    $item = Get-Item -LiteralPath $_
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    [ordered]@{
        file = $item.Name
        path = $item.FullName
        bytes = $item.Length
        sha256 = $hash.Hash.ToLowerInvariant()
        signed = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
    }
}

$manifest = [ordered]@{
    version = $Version
    runtime = $Runtime
    configuration = $Configuration
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    files = $files
}
$manifestPath = Join-Path $artifactsRoot 'agent-artifacts.json'
$manifestJson = ($manifest | ConvertTo-Json -Depth 5).TrimEnd() + "`n"
[IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson,
    [Text.UTF8Encoding]::new($false))

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    dotnet run --project (Join-Path $repositoryRoot 'scripts\AgentReleaseSigner\AgentReleaseSigner.csproj') `
        --configuration Release -- `
        $CertificateThumbprint $manifestPath $artifactsRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Release-manifest attestation signing failed.'
    }
}

Write-Output "Agent MSI: $($msi.FullName)"
Write-Output "Artifact manifest: $manifestPath"
