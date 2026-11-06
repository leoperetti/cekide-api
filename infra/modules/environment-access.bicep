targetScope = 'resourceGroup'

@minLength(36)
@maxLength(36)
param developmentPrincipalId string

param stagingPrincipalId string = '446b2b59-9468-4814-b273-a15a8b44bcbc'
param productionPrincipalId string = 'a71db939-b848-448f-b0e1-f11adfb616d0'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: 'cekideblobaccount'
}

resource service 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: storage
  name: 'default'
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' existing = {
  parent: service
  name: 'cekidecontainer'
}

var conditionTemplate = '''
  (
    (
      !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'} AND NOT SubOperationMatches{'Blob.List'})
      AND !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write'})
      AND !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete'})
      AND !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action'})
    )
    OR
    (
      @Resource[Microsoft.Storage/storageAccounts/blobServices/containers:name] StringEquals 'cekidecontainer'
      AND @Resource[Microsoft.Storage/storageAccounts/blobServices/containers/blobs:path] StringStartsWith '__PREFIX__'
    )
  )
  AND
  (
    (
      !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'} AND SubOperationMatches{'Blob.List'})
    )
    OR
    (
      @Resource[Microsoft.Storage/storageAccounts/blobServices/containers:name] StringEquals 'cekidecontainer'
      AND @Request[Microsoft.Storage/storageAccounts/blobServices/containers/blobs:prefix] StringStartsWith '__PREFIX__'
    )
  )
  '''
var environments = [
  { name: 'development', principalId: developmentPrincipalId }
  { name: 'staging', principalId: stagingPrincipalId }
  { name: 'production', principalId: productionPrincipalId }
]

resource dataRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(resourceGroup().id, 'cekide-blob-prefix-operator')
  properties: {
    roleName: 'Cekide Blob Prefix Operator'
    description: 'Blob read/list, write and delete only. No account keys, container management, delegation keys or role management.'
    type: 'CustomRole'
    assignableScopes: [resourceGroup().id]
    permissions: [
      {
        actions: []
        notActions: []
        dataActions: [
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action'
        ]
        notDataActions: []
      }
    ]
  }
}

resource assignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for environment in environments: {
  name: guid(container.id, environment.principalId, dataRole.id)
  scope: container
  properties: {
    principalId: environment.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: dataRole.id
    conditionVersion: '2.0'
    condition: replace(conditionTemplate, '__PREFIX__', '${environment.name}/')
  }
}]

output assignmentIds array = [for (environment, i) in environments: {
  environment: environment.name
  principalId: environment.principalId
  id: assignments[i].id
}]
