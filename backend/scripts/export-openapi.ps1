[CmdletBinding()]
param(
    [int]$Port = 5097,
    [string]$OutputPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateNotNullOrEmpty()]
    [string]$DotNetPath = 'dotnet',
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$rewriteRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $rewriteRoot 'src\Inventory.Api\Inventory.Api.csproj'
$apiAssembly = Join-Path `
    $rewriteRoot `
    "src\Inventory.Api\bin\$Configuration\net10.0\Inventory.Api.dll"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $rewriteRoot 'docs\api\openapi-v1.json'
}

& $DotNetPath build $apiProject -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw 'The API build failed; the OpenAPI document was not changed.'
}

$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousUrls = $env:ASPNETCORE_URLS
$previousRoleReconciliation = $env:Identity__ReconcileRolePresetsOnStartup
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:Identity__ReconcileRolePresetsOnStartup = 'false'

$process = $null
try {
    $process = Start-Process `
        -FilePath $DotNetPath `
        -ArgumentList @($apiAssembly) `
        -WorkingDirectory (Split-Path -Parent $apiAssembly) `
        -WindowStyle Hidden `
        -PassThru

    $openApiUri = "http://localhost:$Port/openapi/v1.json"
    $response = $null
    foreach ($attempt in 1..30) {
        try {
            $response = Invoke-WebRequest -Uri $openApiUri -UseBasicParsing
            break
        }
        catch {
            if ($process.HasExited) {
                throw "The API exited with code $($process.ExitCode)."
            }
            Start-Sleep -Milliseconds 250
        }
    }

    if ($null -eq $response -or $response.StatusCode -ne 200) {
        throw "The API did not publish OpenAPI at $openApiUri."
    }

    $document = $response.Content | ConvertFrom-Json
    if ($document.openapi -notlike '3.1.*') {
        throw "Expected OpenAPI 3.1 but received '$($document.openapi)'."
    }

    $generatedContent = ($response.Content -replace "`r`n", "`n").TrimEnd() + "`n"
    if ($Verify) {
        if (-not (Test-Path -LiteralPath $OutputPath)) {
            throw "OpenAPI drift check failed because '$OutputPath' does not exist."
        }

        $checkedInContent = (
            (Get-Content -LiteralPath $OutputPath -Raw) -replace "`r`n", "`n"
        ).TrimEnd() + "`n"
        if ($generatedContent -cne $checkedInContent) {
            throw (
                "OpenAPI drift detected. Run .\scripts\export-openapi.ps1 " +
                "-Configuration $Configuration and commit docs\api\openapi-v1.json."
            )
        }

        Write-Output "OpenAPI drift check passed for $OutputPath"
    }
    else {
        $targetDirectory = Split-Path -Parent $OutputPath
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        [System.IO.File]::WriteAllText(
            $OutputPath,
            $generatedContent,
            [System.Text.UTF8Encoding]::new($false))
        Write-Output "Generated $OutputPath"
    }
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }

    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:ASPNETCORE_URLS = $previousUrls
    $env:Identity__ReconcileRolePresetsOnStartup = $previousRoleReconciliation
}
