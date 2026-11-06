[CmdletBinding()]
param(
    [switch] $TestBlobIsolation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$certificate = Join-Path $env:LOCALAPPDATA 'CekideApi\BlobDevelopment\blob-development.pem'
if (-not (Test-Path -LiteralPath $certificate -PathType Leaf)) {
    throw 'Dedicated Blob development certificate is missing. Do not fall back to an administrator identity.'
}
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$variables = @(
    'AZURE_TENANT_ID', 'AZURE_CLIENT_ID', 'AZURE_CLIENT_CERTIFICATE_PATH',
    'AZURE_CLIENT_SECRET', 'AZURE_CLIENT_CERTIFICATE_PASSWORD', 'ASPNETCORE_ENVIRONMENT',
    'Storage__AccountUrl'
)
$previous = @{}
foreach ($name in $variables) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:AZURE_TENANT_ID = 'b7bd0c07-88dc-4c47-81ba-608fd7607a82'
    $env:AZURE_CLIENT_ID = 'c16ef1d0-ae1b-4e2e-8794-af93f7841989'
    $env:AZURE_CLIENT_CERTIFICATE_PATH = $certificate
    [Environment]::SetEnvironmentVariable('AZURE_CLIENT_SECRET', $null, 'Process')
    [Environment]::SetEnvironmentVariable('AZURE_CLIENT_CERTIFICATE_PASSWORD', $null, 'Process')
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Storage__AccountUrl = 'https://cekideblobaccount.blob.core.windows.net'
    if ($TestBlobIsolation) {
        & dotnet run --project (Join-Path $repository 'scripts\diagnostics\BlobIsolation\BlobIsolation.csproj') --configuration Release -- development
    } else {
        & dotnet run --project (Join-Path $repository 'src\CekideApi\CekideApi.csproj') --no-launch-profile
    }
    if ($LASTEXITCODE -ne 0) { throw 'Development process failed. Review its preceding error.' }
} finally {
    foreach ($name in $variables) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
