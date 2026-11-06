[CmdletBinding()]
param(
    [string] $Environment,
    [string] $SubscriptionId,
    [string] $ExpectedIdentityObjectId,
    [string] $CostApprovalReference,
    [string] $RecoveryEvidenceReference
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Environment -notin @('Staging', 'Production')) {
    throw 'Environment must be Staging or Production.'
}
if ([string]::IsNullOrWhiteSpace($SubscriptionId) -or
    [string]::IsNullOrWhiteSpace($ExpectedIdentityObjectId) -or
    [string]::IsNullOrWhiteSpace($CostApprovalReference) -or
    [string]::IsNullOrWhiteSpace($RecoveryEvidenceReference)) {
    throw 'Subscription, managed identity, cost approval, and recovery evidence are required.'
}

$parsedGuid = [guid]::Empty
if (-not [guid]::TryParse($SubscriptionId, [ref] $parsedGuid)) {
    throw 'SubscriptionId must be a GUID.'
}
if (-not [guid]::TryParse($ExpectedIdentityObjectId, [ref] $parsedGuid)) {
    throw 'ExpectedIdentityObjectId must be a GUID.'
}

$expectedResources = switch ($Environment) {
    'Staging' {
        @{
            WebApp = 'cekide-api-staging'
            Database = 'cekide-db-staging'
        }
    }
    'Production' {
        @{
            WebApp = 'cekide-api'
            Database = 'cekide-db'
        }
    }
}

$accountId = & az account show --query id --output tsv 2>$null
if ($LASTEXITCODE -ne 0 -or $accountId -ne $SubscriptionId) {
    throw 'Azure CLI is not authenticated to the expected subscription.'
}

$webAppId = "/subscriptions/$SubscriptionId/resourceGroups/CEKIDE/providers/Microsoft.Web/sites/$($expectedResources.WebApp)"
$databaseId = "/subscriptions/$SubscriptionId/resourceGroups/CEKIDE/providers/Microsoft.Sql/servers/cekide/databases/$($expectedResources.Database)"

$webAppJson = & az resource show --ids $webAppId `
    --query '{name:name, principalId:identity.principalId, state:properties.state}' `
    --output json 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($webAppJson -join ''))) {
    throw "Unable to verify the expected $Environment Web App in resource group CEKIDE."
}
$webApp = $webAppJson | ConvertFrom-Json
if ($webApp.name -ne $expectedResources.WebApp -or
    $webApp.principalId -ne $ExpectedIdentityObjectId -or
    $webApp.state -notin @('Running', 'Stopped')) {
    throw "The $Environment Web App is missing, has an unexpected identity, or is in a transition state."
}

$databaseJson = & az resource show --ids $databaseId `
    --query '{name:name, status:properties.status}' `
    --output json 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($databaseJson -join ''))) {
    throw "Unable to verify the expected $Environment database in resource group CEKIDE."
}
$database = $databaseJson | ConvertFrom-Json
if ($database.name -ne $expectedResources.Database -or $database.status -ne 'Online') {
    throw "The $Environment database name or state does not match the approved inventory."
}

Write-Output "Read-only preflight passed for $Environment. No Azure resources were changed."
