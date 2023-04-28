param appName string
param dabConfigFileName string = 'dab-config.json'
param mountedStorageName string = 'dabconfig'
param isExternalIngress bool = true
param location string = resourceGroup().location
param environmentId string
param tag string = 'latest'
param env array = [
  {
      name: 'DOTNET_ENVIRONMENT'
      value: 'Production'
  }
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
]

var dabConfigFilePath='--ConfigFileName=./${mountedStorageName}/${dabConfigFileName}'

resource containerApp 'Microsoft.App/containerApps@2022-10-01' = {
  name: appName
  location: location
  properties: {
    environmentId: environmentId
    configuration: {
      activeRevisionsMode:'Single'
      ingress: {
        allowInsecure:true
        external: isExternalIngress
        targetPort: 5000
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          image: 'mcr.microsoft.com/azure-databases/data-api-builder:${tag}'
          name: appName
          env: env
          args: [dabConfigFilePath]
          volumeMounts: [
            {
              volumeName: 'azure-file-volume'
              mountPath: '/App/${mountedStorageName}'
            }
          ]
          resources:{
            cpu: json('0.5')
            memory:'1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
        rules:[
          {
            name: 'http'
            http:{
              metadata:{
                concurrentRequests: '200'
              }
            }
          }
        ]
      }
      volumes:[
        {
          name:'azure-file-volume'
          storageType:'AzureFile'
          storageName: mountedStorageName
        }
      ]
    }
  }
}

output fqdn string = containerApp.properties.configuration.ingress.fqdn
