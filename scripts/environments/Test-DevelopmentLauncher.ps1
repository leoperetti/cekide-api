[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$launcher = Join-Path $PSScriptRoot 'Start-Development.ps1'
$names = @(
    'AZURE_TENANT_ID', 'AZURE_CLIENT_ID', 'AZURE_CLIENT_CERTIFICATE_PATH',
    'AZURE_CLIENT_SECRET', 'AZURE_CLIENT_CERTIFICATE_PASSWORD', 'ASPNETCORE_ENVIRONMENT',
    'Storage__AccountUrl'
)
$original = @{}
foreach ($name in $names) {
    $original[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$script:certificateExists = $true
$script:exitCode = 0
$script:calls = 0

function Test-Path {
    param([string] $LiteralPath, [string] $PathType)
    if ($LiteralPath -notlike '*CekideApi\BlobDevelopment\blob-development.pem' -or $PathType -ne 'Leaf') {
        throw 'Unexpected certificate lookup.'
    }
    return $script:certificateExists
}

function dotnet {
    $script:calls++
    if ($env:AZURE_CLIENT_ID -ne 'c16ef1d0-ae1b-4e2e-8794-af93f7841989' -or
        $env:AZURE_TENANT_ID -ne 'b7bd0c07-88dc-4c47-81ba-608fd7607a82' -or
        $env:ASPNETCORE_ENVIRONMENT -ne 'Development' -or
        $env:Storage__AccountUrl -ne 'https://cekideblobaccount.blob.core.windows.net' -or
        [Environment]::GetEnvironmentVariable('AZURE_CLIENT_SECRET', 'Process') -or
        [Environment]::GetEnvironmentVariable('AZURE_CLIENT_CERTIFICATE_PASSWORD', 'Process')) {
        throw 'Launcher did not select only the dedicated certificate identity.'
    }
    $script:lastArguments = @($args)
    $global:LASTEXITCODE = $script:exitCode
}

function Assert-Restored {
    foreach ($name in $names) {
        if ([Environment]::GetEnvironmentVariable($name, 'Process') -ne "synthetic-previous-$name") {
            throw "Launcher did not restore $name."
        }
    }
}

try {
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable($name, "synthetic-previous-$name", 'Process')
    }
    . $launcher
    Assert-Restored
    if ($script:lastArguments -notcontains '--no-launch-profile') { throw 'API launch profiles must not override the selected environment.' }
    Write-Output 'PASS: API launch selects the dedicated identity and restores previous environment.'

    . $launcher -TestBlobIsolation
    Assert-Restored
    if ($script:lastArguments[-1] -ne 'development') { throw 'Unexpected probe environment.' }
    Write-Output 'PASS: Blob probe launch uses the dedicated development identity.'

    $script:exitCode = 1
    $failed = $false
    try { . $launcher -TestBlobIsolation } catch { $failed = $true }
    if (-not $failed) { throw 'Failed child process was hidden.' }
    Assert-Restored
    Write-Output 'PASS: Failed child process reports an error and restores previous environment.'

    $script:certificateExists = $false
    $before = $script:calls
    $failed = $false
    try { . $launcher } catch { $failed = $true }
    if (-not $failed -or $script:calls -ne $before) { throw 'Missing certificate did not stop before launching.' }
    Assert-Restored
    Write-Output 'PASS: Missing certificate prevents administrator fallback.'
} finally {
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable($name, $original[$name], 'Process')
    }
}
Write-Output '4 offline development launcher tests passed. No certificate, Azure service or API process was accessed.'
