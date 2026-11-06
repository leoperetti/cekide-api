[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $CompiledTemplatePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$template = Get-Content -LiteralPath $CompiledTemplatePath -Raw | ConvertFrom-Json
$resources = @($template.resources)
$roles = @($resources | Where-Object { $_.type -eq 'Microsoft.Authorization/roleDefinitions' })
$assignments = @($resources | Where-Object { $_.type -eq 'Microsoft.Authorization/roleAssignments' })
if ($roles.Count -ne 1 -or $assignments.Count -ne 1 -or $resources.Count -ne 2) {
    throw 'Expected only a custom role and looped role assignments; resource changes are prohibited.'
}
$permission = $roles[0].properties.permissions[0]
$expected = @('read', 'write', 'delete', 'add/action') | ForEach-Object {
    "Microsoft.Storage/storageAccounts/blobServices/containers/blobs/$_"
}
if (@($permission.actions).Count -ne 0 -or @($permission.notActions).Count -ne 0 -or
    @($permission.notDataActions).Count -ne 0 -or
    @(Compare-Object @($permission.dataActions) $expected).Count -ne 0) {
    throw 'Custom role must contain exactly the four approved Blob data actions and no management actions.'
}
$assignment = $assignments[0]
if ($assignment.properties.conditionVersion -ne '2.0' -or
    $assignment.properties.principalType -ne 'ServicePrincipal' -or
    $assignment.scope -notmatch "containers', 'cekideblobaccount', 'default', 'cekidecontainer'") {
    throw 'Assignments must target service principals at the existing container with ABAC 2.0.'
}
$condition = $template.variables.conditionTemplate
if ($assignment.properties.condition -notmatch "replace\(variables\('conditionTemplate'\)" -or
    $assignment.properties.condition -notmatch "environments.*\.name" -or
    $condition.Contains('${')) {
    throw 'The prefix must be dynamically replaced with the fixed environment name, not left as literal interpolation.'
}
foreach ($required in @(
    "StringEquals 'cekidecontainer'",
    "SubOperationMatches{'Blob.List'}",
    'containers/blobs:prefix] StringStartsWith',
    'containers/blobs:path] StringStartsWith'
)) {
    if (-not $condition.Contains($required)) { throw "Required ABAC guard missing: $required" }
}
$environments = @($template.variables.environments)
if (($environments.name -join ',') -cne 'development,staging,production') {
    throw 'Expected exactly the three fixed environments.'
}
if ($template.parameters.stagingPrincipalId.defaultValue -ne '446b2b59-9468-4814-b273-a15a8b44bcbc' -or
    $template.parameters.productionPrincipalId.defaultValue -ne 'a71db939-b848-448f-b0e1-f11adfb616d0') {
    throw 'Unexpected runtime identities.'
}
Write-Output 'PASS: compiled Blob access template has only four data actions, container scope, three environment prefixes and separate list/path guards.'
Write-Output 'This structural check does not prove Azure authorization. Real identity probes remain mandatory.'
