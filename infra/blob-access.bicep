targetScope = 'resourceGroup'

@minLength(36)
@maxLength(36)
param developmentPrincipalId string

module storage './modules/storage.bicep' = {
  name: 'cekide-shared-blob-storage'
}

module access './modules/environment-access.bicep' = {
  name: 'cekide-blob-prefix-access'
  params: {
    developmentPrincipalId: developmentPrincipalId
  }
  dependsOn: [storage]
}

output assignments array = access.outputs.assignmentIds
output accountUrl string = storage.outputs.accountUrl
