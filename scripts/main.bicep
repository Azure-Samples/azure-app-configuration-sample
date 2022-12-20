@description('SKU size')
@allowed([
  'S1'
  'F1'
])
param skuSize string = 'S1'

param location string

var names = loadJsonContent('names.json')
var conf = loadJsonContent('./config.json')
var roles = conf.roles

var baseName = substring(uniqueString(resourceGroup().id), 0, 4)


var environments = [
  'Development'
  'Test'
  'Production'
]

@description('Specifies the names of the key-value resources. The name is a combination of key and label with $ as delimiter. The label is optional.')
param keyValueNames array = [
  'API:Settings'
  'API:Settings$Development'
  'API:Settings$Production'
]

@description('Array holding settings to be loaed into app confiuration')
param keyValueValues array = [
  loadTextContent('conf/API_Settings.json')
  loadTextContent('conf/API_Settings_Development.json')
  loadTextContent('conf/API_Settings_Production.json')
]

//============================= User assigned managed identity  =============================
resource webappUamis 'Microsoft.ManagedIdentity/userAssignedIdentities@2022-01-31-preview' = {
  name: '${names.uamis.appconfigDemo}-${baseName}'
  location: location
}

//============================= Web app  =============================
resource serverFarm 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: names.website.appServiceplan
  location: location
  sku: {
    size: skuSize
    name: skuSize
    capacity: 1
  }
  kind: 'windows'

}

resource webApp 'Microsoft.Web/sites@2022-03-01' = {
  name: '${names.website.webapp}-${baseName}'
  location: location
  kind: 'app'
  properties: {

    serverFarmId: serverFarm.id
    clientAffinityEnabled: false
    /*siteConfig: {
      netFrameworkVersion: 'v6.0'
    }*/

  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${webappUamis.id}': {} }
  }
  resource appsettings 'config@2022-03-01' = {
    name: 'appsettings'
    properties: {
      'ApplicationInsights:ConnectionString': applicationInsights.properties.ConnectionString
      KeyVaultName: keyVault.name
      ASPNETCORE_ENVIRONMENT: 'Production'
      UserAssignedManagedIdentityClientId: webappUamis.properties.clientId
      AppConfiguration: configurationStore.properties.endpoint
      'ChangeSubscription:ServiceBusTopic': serviceBusTopicForChangeNotification.name
      'ChangeSubscription:ServiceBusNamespace': serviceBusNamespace.properties.serviceBusEndpoint
    }
  }

  resource web 'config' = {
    name: 'web'
    properties: {

      netFrameworkVersion: 'v6.0'
      use32BitWorkerProcess: false
      loadBalancing:  'PerSiteRoundRobin'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled' 
    }

  }
}

//============================= App Configuration =============================

resource configurationStore 'Microsoft.AppConfiguration/configurationStores@2022-05-01' = {
  location: location
  name: '${names.appConfiguration.name}-${baseName}'
  sku: {
    name: 'standard'
  }
  properties: {

  }
}

resource managedIdentityCanReadConfigurationStore 'Microsoft.Authorization/roleAssignments@2022-04-01' ={
  name: guid(roles['App Configuration Data Reader'], webappUamis.id)
  scope: configurationStore
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles['App Configuration Data Reader'])
    principalId: webappUamis.properties.principalId
    principalType: 'ServicePrincipal'
  }
}



module AddEnvironmentSettings 'modules/addAppConfiguration.bicep' = [for (item, i) in keyValueNames: {
  name: replace(replace('Adding-${item}',':',''),'$','')
  params: {
    keyName: item
    managedIdentityWithAccessToAppConfiguration: webappUamis.name
    value: keyValueValues[i]
    appConfigName : configurationStore.name
  }
}]

module SomeTestValue 'modules/addAppConfiguration.bicep' =  {
  name: 'SomeTestValue'
  params: {
    keyName: 'API:Settings:SomeDemoString'
    managedIdentityWithAccessToAppConfiguration: webappUamis.name
    value: '{"test":"test"}'
    appConfigName : configurationStore.name
  }
}
module SomeSecretTestValue 'modules/addAppConfiguration.bicep' =  {
  name: 'SomeSecretTestValue'
  params: {
    keyName: 'API:Settings:Secrets:SecretString'
    managedIdentityWithAccessToAppConfiguration: webappUamis.name
    value: 'test value in kv'
    appConfigName : configurationStore.name
    keyVaultName: keyVault.name //when provided, it will store the value in KV
  }
}

resource featureFlagDoMagic 'Microsoft.AppConfiguration/configurationStores/keyValues@2022-05-01' = [for env in environments: {
  name: '.appconfig.featureflag~2F${names.features.magic.name}$${env}'
  parent: configurationStore
  properties: {
    contentType: 'application/vnd.microsoft.appconfig.ff+json;charset=utf-8'
    tags: {}
    value: '{"id": "${names.features.magic.name}", "description": "", "enabled": ${names.features.magic.default}, "conditions": {"client_filters":[]}}'
  }
}]

resource featureFlagShowDebugView 'Microsoft.AppConfiguration/configurationStores/keyValues@2022-05-01' = [for env in environments: {
  name: '.appconfig.featureflag~2F${names.features.showDebugView.name}$${env}'
  parent: configurationStore
  properties: {
    contentType: 'application/vnd.microsoft.appconfig.ff+json;charset=utf-8'
    tags: {}
    value: '{"id": "${names.features.showDebugView.name}", "description": "", "enabled": ${names.features.showDebugView.default}, "conditions": {"client_filters":[]}}'
  }
}]

resource featureFlagProcessKeyVaultChangeEvents 'Microsoft.AppConfiguration/configurationStores/keyValues@2022-05-01' = [for env in environments: {
  name: '.appconfig.featureflag~2F${names.features.autoUpdateLatestVersionSecrets.name}$${env}'
  parent: configurationStore
  properties: {
    contentType: 'application/vnd.microsoft.appconfig.ff+json;charset=utf-8'
    tags: {}   
    value: '{"id": "${names.features.autoUpdateLatestVersionSecrets.name}", "description": "", "enabled": ${names.features.autoUpdateLatestVersionSecrets.default}, "conditions": {"client_filters":[]}}'
  }
}]

//============================= EventGrid and Service bus =============================

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  name: '${names.serviceBus.nameSpace}-${baseName}'
  location: location
  sku: {
    name: 'Standard'
  }
}

resource serviceBusTopicForChangeNotification 'Microsoft.ServiceBus/namespaces/topics@2021-11-01' = {
  name: 'sb-appconfigurationChangeTopic'
  parent: serviceBusNamespace
  properties: {
  }
}

resource eventGridSystemTopicForConfigurationStore 'Microsoft.EventGrid/systemTopics@2022-06-15' = {
  name: 'eg-systemChangeTopic'
  location: location
  properties: {
    source: configurationStore.id
    topicType: 'Microsoft.AppConfiguration.ConfigurationStores'
  }
}

resource eventGridSystemTopicForKeyVault 'Microsoft.EventGrid/systemTopics@2022-06-15' = {
  name: 'eg-keyVaultSystemChangeTopic'
  location: location
  properties: {
    source: keyVault.id
    topicType: 'Microsoft.KeyVault.Vaults'
  }
}

resource changeEventSubscriptionac 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2022-06-15' = {
  name: 'changeSubscription-kv'
  parent: eventGridSystemTopicForKeyVault
  properties: {
    destination: {
      endpointType: 'ServiceBusTopic'
      properties: {
        resourceId: serviceBusTopicForChangeNotification.id
      }
    }
    filter: {
      includedEventTypes: [
        'Microsoft.KeyVault.KeyNewVersionCreated'
        'Microsoft.KeyVault.KeyNearExpiry'
        'Microsoft.KeyVault.KeyExpired'
        'Microsoft.KeyVault.SecretNewVersionCreated'
        'Microsoft.KeyVault.SecretNearExpiry'
        'Microsoft.KeyVault.SecretExpired'
        // 'Microsoft.KeyVault.CertificateNewVersionCreated'
        // 'Microsoft.KeyVault.CertificateNearExpiry'
        // 'Microsoft.KeyVault.CertificateExpired'
      ]
    }
    eventDeliverySchema: 'EventGridSchema'
  }
}

resource changeEventSubscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2022-06-15' = {
  name: 'changeSubscription'
  parent: eventGridSystemTopicForConfigurationStore
  properties: {
    destination: {
      endpointType: 'ServiceBusTopic'
      properties: {
        resourceId: serviceBusTopicForChangeNotification.id
      }
    }
    filter: {
      includedEventTypes: [
        'Microsoft.AppConfiguration.KeyValueModified'
      ]
    }
    eventDeliverySchema: 'EventGridSchema'
  }
}

//============================= Key Vault and secrect =============================
resource keyVault 'Microsoft.KeyVault/vaults@2022-07-01' = {
  name: '${names.keyVault.name}-${baseName}'
  location: location
  tags: {
    
  }

  properties: {
    createMode: 'default'
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    enableSoftDelete: true
    enableRbacAuthorization: true
    sku: {
      family: 'A'
      name: 'standard'
    }
    softDeleteRetentionInDays: 7
    tenantId: subscription().tenantId
  }
}



module AppInstighsConnectionStringSecret 'modules/addAppConfiguration.bicep' = {
  name: 'StoreAppInsightsConnections'
  params: {
    keyName: 'API:Settings:Secrets:ConnectionStrings:AppInsights'
    managedIdentityWithAccessToAppConfiguration: webappUamis.name
    value: applicationInsights.properties.ConnectionString
    appConfigName: configurationStore.name
    keyVaultName: keyVault.name
  }
}

module ServiceBusConnectionStringSecret 'modules/addAppConfiguration.bicep' = {
  name: 'ServiceBusConnectionStringSecret'
  params: {
    keyName: 'API:Settings:Secrets:ConnectionStrings:ServiceBus'
    managedIdentityWithAccessToAppConfiguration: webappUamis.name
    value: serviceBusConnectionString
    appConfigName: configurationStore.name
    keyVaultName: keyVault.name

  }
}

module AppConfigurationConnectionStringSecret 'modules/addAppConfiguration.bicep' = {
  name: 'AppConfigurationConnectionStringSecret'
  params: {
    keyName: 'API:Settings:Secrets:ConnectionStrings:AppConfig'
    managedIdentityWithAccessToAppConfiguration: webappUamis.name
    value: appConfigReadonlyConnectionString.connectionString
    appConfigName: configurationStore.name
    keyVaultName: keyVault.name
  }
}

//============================= Role assignments =============================


resource managedIdentityReadNotification 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, roles['Azure Service Bus Data Receiver'], webappUamis.id)
  scope: serviceBusTopicForChangeNotification
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', roles['Azure Service Bus Data Receiver'])
    principalId: webappUamis.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource managedIdentityCreateSubscription 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespace.id, roles.Contributor, webappUamis.id)
  scope: serviceBusTopicForChangeNotification
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', roles.Contributor)
    principalId: webappUamis.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

//============================= Log analytics =============================
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${names.monitor.appinsights}-${baseName}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    //DisableIpMasking: false
    //DisableLocalAuth: false
    //Flow_Type: 'Bluefield'
    //ForceCustomerStorageForProfiler: false
    //publicNetworkAccessForIngestion: 'Enabled'
    //publicNetworkAccessForQuery: 'Enabled'
    Request_Source: 'rest'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

param sku string = 'PerGB2018'
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${names.monitor.logAnalyticsWorkspace}-${baseName}'
  location: location
  properties: {
    sku: {
      name: sku
    }
    retentionInDays: 30
    features: {
      searchVersion: 1
      legacy: 0
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

var serviceBusEndpoint = '${serviceBusNamespace.id}/AuthorizationRules/RootManageSharedAccessKey'
var serviceBusConnectionString = listKeys(serviceBusEndpoint, serviceBusNamespace.apiVersion).primaryConnectionString

// resource appConfig 'Microsoft.AppConfiguration/configurationStores@2019-11-01-preview' existing = {
//   name: configurationStore.name
// }

var appConfigReadonlyConnectionString = filter(configurationStore.listKeys().value, k => k.name == 'Primary Read Only')[0]

output applicationInsights_ConnectionString string = applicationInsights.properties.ConnectionString
output changeSubscription_ServiceBusConnectionString string = serviceBusConnectionString
output connectionStrings_AppConfig string = appConfigReadonlyConnectionString.connectionString
output webappEndpoint string = webApp.properties.defaultHostName
