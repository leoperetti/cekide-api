[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Set-RunnerSqlFirewall.ps1'
$subscription = '948a8f49-fd90-40d9-8fa9-12f722768a7a'
$tenant = 'b7bd0c07-88dc-4c47-81ba-608fd7607a82'
$script:passed = 0

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Reset-FakeAzure {
    $script:calls = [System.Collections.Generic.List[object]]::new()
    $script:rules = @{}
    $script:identity = '592310f3-be13-498a-bf41-eda2d8762698'
    $script:accountSubscription = $subscription
    $script:accountTenant = $tenant
    $script:failCommand = ''
    $script:ignoreDelete = $false
    $script:invalidCreateResponse = $false
    $script:ipLookups = 0
    $script:runnerIp = '203.0.113.8'
    $script:rules['CekideOperatorPublicIp'] = @{
        name = 'CekideOperatorPublicIp'; startIpAddress = '192.0.2.1'; endIpAddress = '192.0.2.1'
    }
    $script:rules['AllowAllWindowsAzureIps'] = @{
        name = 'AllowAllWindowsAzureIps'; startIpAddress = '0.0.0.0'; endIpAddress = '0.0.0.0'
    }
}

function az {
    $arguments = @($args)
    $script:calls.Add($arguments)
    $global:LASTEXITCODE = 0
    $command = $arguments[0..([Math]::Min(3, $arguments.Count - 1))] -join ' '
    if ($script:failCommand -and $command.StartsWith($script:failCommand)) {
        $global:LASTEXITCODE = 1
        return 'Synthetic CLI failure'
    }
    if ($arguments[0] -eq 'account') {
        return (@{
            subscription = $script:accountSubscription
            tenant = $script:accountTenant
            clientId = $script:identity
            identityType = 'servicePrincipal'
        } | ConvertTo-Json)
    }
    Assert-True ($command.StartsWith('sql server firewall-rule')) 'Unexpected Azure command.'
    $scopeIndex = [Array]::IndexOf($arguments, '--subscription')
    Assert-True ($scopeIndex -ge 0 -and $arguments[$scopeIndex + 1] -eq $subscription) 'Wrong subscription.'
    $groupIndex = [Array]::IndexOf($arguments, '--resource-group')
    Assert-True ($groupIndex -ge 0 -and $arguments[$groupIndex + 1] -eq 'CEKIDE') 'Wrong resource group.'
    $serverIndex = [Array]::IndexOf($arguments, '--server')
    Assert-True ($serverIndex -ge 0 -and $arguments[$serverIndex + 1] -eq 'cekide') 'Wrong server.'
    $operation = $arguments[3]
    if ($operation -eq 'list') {
        return ConvertTo-Json -InputObject @($script:rules.Values) -Depth 5
    }
    $name = $arguments[[Array]::IndexOf($arguments, '--name') + 1]
    Assert-True ($name -match '^gha-\d+-\d+-(staging|production)$') 'Attempt to modify a permanent rule.'
    if ($operation -eq 'create') {
        $start = $arguments[[Array]::IndexOf($arguments, '--start-ip-address') + 1]
        $end = $arguments[[Array]::IndexOf($arguments, '--end-ip-address') + 1]
        Assert-True ($start -eq $end -and $start -ne '0.0.0.0') 'Not an exact single-IP rule.'
        $rule = @{ name = $name; startIpAddress = $start; endIpAddress = $end }
        $script:rules[$name] = $rule
        if ($script:invalidCreateResponse) { return '{}' }
        return $rule | ConvertTo-Json
    }
    if ($operation -eq 'delete') {
        if (-not $script:ignoreDelete) { $script:rules.Remove($name) }
        return ''
    }
    throw 'Unexpected firewall operation.'
}

function Invoke-RestMethod {
    param([string] $Uri, [int] $TimeoutSec)
    Assert-True ($Uri -eq 'https://api.ipify.org' -and $TimeoutSec -eq 30) 'Unexpected IP lookup.'
    $script:ipLookups++
    return $script:runnerIp
}

function Invoke-Firewall([string] $Operation, [string] $Environment = 'staging', [string] $Ip = '203.0.113.8') {
    $parameters = @{
        Operation = $Operation; Environment = $Environment; SubscriptionId = $subscription
        RunId = '12345'; RunAttempt = '1'
    }
    if ($Ip) { $parameters.RunnerIp = $Ip }
    . $scriptPath @parameters
}

function Assert-Fails([scriptblock] $Action, [string] $Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert-True $failed $Message
}

function Test-Case([string] $Name, [scriptblock] $Action) {
    Reset-FakeAzure
    & $Action
    Assert-True ($script:rules.ContainsKey('CekideOperatorPublicIp')) 'Permanent operator rule was changed.'
    Assert-True ($script:rules.ContainsKey('AllowAllWindowsAzureIps')) 'Azure services rule was changed.'
    $script:passed++
    Write-Output "PASS: $Name"
}

if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw 'Reusable firewall script does not exist yet.'
}

Test-Case 'Open creates and verifies an exact staging IP rule' {
    Invoke-Firewall Open | Out-Null
    Assert-True ($script:rules['gha-12345-1-staging'].startIpAddress -eq '203.0.113.8') 'Rule missing.'
}
Test-Case 'Production uses its own identity and distinct rule name' {
    $script:identity = 'acd40a92-c0c7-4bd7-bca0-60ae8b31bb1b'
    Invoke-Firewall Open production | Out-Null
    Assert-True ($script:rules.ContainsKey('gha-12345-1-production')) 'Production rule missing.'
}
Test-Case 'Close removes only its rule and is repeatable' {
    Invoke-Firewall Open | Out-Null
    Invoke-Firewall Close | Out-Null
    Invoke-Firewall Close | Out-Null
    Assert-True (-not $script:rules.ContainsKey('gha-12345-1-staging')) 'Rule remains.'
}
Test-Case 'Failed migration still removes firewall in finally' {
    Assert-Fails {
        try {
            Invoke-Firewall Open | Out-Null
            throw 'Synthetic migration failure'
        } finally {
            Invoke-Firewall Close | Out-Null
        }
    } 'Migration failure was hidden.'
    Assert-True (-not $script:rules.ContainsKey('gha-12345-1-staging')) 'Cleanup did not run.'
}
Test-Case 'Auto-discovered IP is validated and Close never queries external IP' {
    Invoke-Firewall Open staging '' | Out-Null
    Invoke-Firewall Close staging '' | Out-Null
    Assert-True ($script:ipLookups -eq 1) 'Cleanup queried an unstable external dependency.'
}
Test-Case 'Open and Close support an initially empty server rule list' {
    $permanentRules = $script:rules
    $script:rules = @{}
    try {
        Invoke-Firewall Open | Out-Null
        Invoke-Firewall Close | Out-Null
        Assert-True ($script:rules.Count -eq 0) 'Rule remains on an initially empty server.'
    } finally {
        $script:rules = $permanentRules
    }
}
Test-Case 'Auto-discovered invalid IP is rejected without writes' {
    $script:runnerIp = '0.0.0.0'
    Assert-Fails { Invoke-Firewall Open staging '' } 'Invalid discovered IP was accepted.'
    Assert-True ($script:rules.Count -eq 2) 'Discovered invalid IP reached a write.'
}
Test-Case 'Rejects unsafe IPs before Azure writes' {
    foreach ($ip in @('0.0.0.0', '::1', '127.0.0.1', '10.0.0.1', '169.254.1.1',
        '172.16.0.1', '192.168.1.1', '100.64.0.1', '224.0.0.1', '255.255.255.255',
        '203.0.113.0/24', '1.2.3', '010.0.0.1', 'invalid')) {
        Assert-Fails { Invoke-Firewall Open staging $ip } "Accepted unsafe IP: $ip"
    }
    Assert-True ($script:rules.Count -eq 2) 'Invalid IP reached a write.'
}
Test-Case 'Rejects wrong identity, subscription or tenant before writes' {
    $script:identity = 'acd40a92-c0c7-4bd7-bca0-60ae8b31bb1b'
    Assert-Fails { Invoke-Firewall Open } 'Accepted production identity in staging.'
    Reset-FakeAzure
    $script:accountSubscription = '00000000-0000-0000-0000-000000000001'
    Assert-Fails { Invoke-Firewall Open } 'Accepted wrong subscription.'
    Reset-FakeAzure
    $script:accountTenant = '00000000-0000-0000-0000-000000000001'
    Assert-Fails { Invoke-Firewall Close } 'Accepted wrong tenant during cleanup.'
    Assert-True ($script:rules.Count -eq 2) 'Wrong identity reached a write.'
}
Test-Case 'Refuses to overwrite an existing rule with another IP' {
    Invoke-Firewall Open | Out-Null
    Assert-Fails { Invoke-Firewall Open staging '203.0.113.9' } 'Overwrote a conflicting rule.'
    Assert-True ($script:rules['gha-12345-1-staging'].startIpAddress -eq '203.0.113.8') 'IP changed.'
}
Test-Case 'Repeated Open with the same IP does not create again' {
    Invoke-Firewall Open | Out-Null
    Invoke-Firewall Open | Out-Null
    $creates = @($script:calls | Where-Object { ($_ -join ' ') -match 'firewall-rule create' })
    Assert-True ($creates.Count -eq 1) 'Repeated Open performed an unnecessary write.'
}
Test-Case 'Azure CLI errors are explicit, not treated as missing rules' {
    foreach ($command in @('account show', 'sql server firewall-rule list', 'sql server firewall-rule create')) {
        Reset-FakeAzure
        $script:failCommand = $command
        Assert-Fails { Invoke-Firewall Open } "CLI failure hidden: $command"
    }
}
Test-Case 'Delete failure cannot report success' {
    Invoke-Firewall Open | Out-Null
    $script:failCommand = 'sql server firewall-rule delete'
    Assert-Fails { Invoke-Firewall Close } 'Delete failure hidden.'
}
Test-Case 'Verifies absence after deletion' {
    Invoke-Firewall Open | Out-Null
    $script:ignoreDelete = $true
    Assert-Fails { Invoke-Firewall Close } 'Stale rule was reported as removed.'
}
Test-Case 'Invalid create response fails but can still be cleaned up' {
    $script:invalidCreateResponse = $true
    Assert-Fails { Invoke-Firewall Open } 'Invalid create response accepted.'
    Invoke-Firewall Close | Out-Null
    Assert-True (-not $script:rules.ContainsKey('gha-12345-1-staging')) 'Cleanup lost the rule name.'
}
Test-Case 'Rejects invalid environment, operation, run identifiers and subscription' {
    foreach ($parameters in @(
        @{ Environment = 'development' }, @{ Operation = 'Overwrite' },
        @{ RunId = '../permanent' }, @{ RunAttempt = '0' },
        @{ SubscriptionId = '00000000-0000-0000-0000-000000000001' }
    )) {
        $inputParameters = @{
            Operation = 'Open'; Environment = 'staging'; SubscriptionId = $subscription
            RunId = '12345'; RunAttempt = '1'; RunnerIp = '203.0.113.8'
        }
        foreach ($key in $parameters.Keys) { $inputParameters[$key] = $parameters[$key] }
        Assert-Fails { . $scriptPath @inputParameters } 'Invalid scope was accepted.'
    }
    Assert-True ($script:calls.Count -eq 0) 'Invalid scope reached Azure CLI.'
}

Write-Output "$script:passed offline firewall tests passed. No Azure services were contacted."
