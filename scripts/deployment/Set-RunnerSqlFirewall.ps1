[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Open', 'Close')]
    [string] $Operation,

    [Parameter(Mandatory = $true)]
    [ValidateSet('staging', 'production')]
    [string] $Environment,

    [Parameter(Mandatory = $true)]
    [ValidateSet('948a8f49-fd90-40d9-8fa9-12f722768a7a')]
    [string] $SubscriptionId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[1-9][0-9]{0,19}$')]
    [string] $RunId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[1-9][0-9]{0,9}$')]
    [string] $RunAttempt,

    [string] $RunnerIp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Environment = $Environment.ToLowerInvariant()
$ruleName = "gha-$RunId-$RunAttempt-$Environment"
$expectedClientId = if ($Environment -eq 'staging') {
    '592310f3-be13-498a-bf41-eda2d8762698'
} else {
    'acd40a92-c0c7-4bd7-bca0-60ae8b31bb1b'
}

function Invoke-AzureJson([string[]] $Arguments, [string] $Context) {
    $result = & az @Arguments --only-show-errors --output json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed while $Context. Check Azure authentication and firewall permissions."
    }
    if ([string]::IsNullOrWhiteSpace(($result -join "`n"))) {
        throw "Azure CLI returned no JSON while $Context."
    }
    return ($result -join "`n") | ConvertFrom-Json
}

function Get-RunnerRule {
    $rules = Invoke-AzureJson -Arguments @(
        'sql', 'server', 'firewall-rule', 'list',
        '--subscription', $SubscriptionId, '--resource-group', 'CEKIDE', '--server', 'cekide'
    ) -Context "reading firewall rule $ruleName"
    $matching = @(foreach ($rule in $rules) {
        if ($rule.name -ceq $ruleName) { $rule }
    })
    if ($matching.Count -gt 1) { throw "Duplicate firewall rule $ruleName." }
    if ($matching.Count -eq 1) { return $matching[0] }
}

function Assert-ExactRule($Rule) {
    if ($Rule.name -cne $ruleName -or
        $Rule.startIpAddress -cne $RunnerIp -or
        $Rule.endIpAddress -cne $RunnerIp) {
        throw "Firewall rule $ruleName does not match the exact runner IP."
    }
}

if ($Operation -eq 'Open') {
    if (-not $PSBoundParameters.ContainsKey('RunnerIp')) {
        $RunnerIp = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30).Trim()
    }
    if ($RunnerIp -notmatch '^(0|[1-9][0-9]{0,2})(\.(0|[1-9][0-9]{0,2})){3}$') {
        throw 'RunnerIp must be a canonical single IPv4 address, not a range.'
    }
    $address = $null
    if (-not [System.Net.IPAddress]::TryParse($RunnerIp, [ref] $address) -or
        $address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw 'RunnerIp must be a single IPv4 address.'
    }
    $bytes = $address.GetAddressBytes()
    if ($bytes[0] -in @(0, 10, 127) -or $bytes[0] -ge 224 -or
        ($bytes[0] -eq 100 -and $bytes[1] -ge 64 -and $bytes[1] -le 127) -or
        ($bytes[0] -eq 169 -and $bytes[1] -eq 254) -or
        ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or
        ($bytes[0] -eq 192 -and $bytes[1] -eq 168)) {
        throw 'RunnerIp must be a public unicast IPv4 address.'
    }
}

$account = Invoke-AzureJson -Arguments @(
    'account', 'show', '--query',
    '{subscription:id,tenant:tenantId,clientId:user.name,identityType:user.type}'
) -Context 'verifying the environment deployment identity'
if ($account.subscription -ne $SubscriptionId -or
    $account.tenant -ne 'b7bd0c07-88dc-4c47-81ba-608fd7607a82' -or
    $account.clientId -ne $expectedClientId -or $account.identityType -ne 'servicePrincipal') {
    throw "Azure CLI must use the approved $Environment deployment identity, tenant and subscription."
}

$existing = Get-RunnerRule
if ($Operation -eq 'Open') {
    if ($null -ne $existing) {
        Assert-ExactRule $existing
        Write-Output "Runner firewall rule $ruleName already matches; no write performed."
        return
    }
    $created = Invoke-AzureJson -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--subscription', $SubscriptionId, '--resource-group', 'CEKIDE', '--server', 'cekide',
        '--name', $ruleName, '--start-ip-address', $RunnerIp, '--end-ip-address', $RunnerIp
    ) -Context "creating firewall rule $ruleName"
    Assert-ExactRule $created
    $persisted = Get-RunnerRule
    if ($null -eq $persisted) { throw "Firewall rule $ruleName was not persisted." }
    Assert-ExactRule $persisted
    Write-Output "Runner firewall rule $ruleName created and verified."
    return
}

if ($null -eq $existing) {
    Write-Output "Runner firewall rule $ruleName is already absent."
    return
}
$deleteArguments = @(
    'sql', 'server', 'firewall-rule', 'delete',
    '--subscription', $SubscriptionId, '--resource-group', 'CEKIDE', '--server', 'cekide',
    '--name', $ruleName, '--only-show-errors', '--output', 'none'
)
$null = & az @deleteArguments 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Firewall cleanup failed. Remove only rule $ruleName from server cekide manually."
}
if ($null -ne (Get-RunnerRule)) {
    throw "Firewall cleanup verification failed. Rule $ruleName is still present."
}
Write-Output "Runner firewall rule $ruleName removed; absence verified."
