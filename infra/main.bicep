// Appointments API on Azure Container Apps with Azure SQL, all connected by managed identity.
// No passwords, keys or connection secrets exist anywhere: SQL accepts Entra ID only, ACR pulls use the identity,
// and telemetry goes to Application Insights.
//
//   az deployment group create -g <rg> -f infra/main.bicep -p authAudience=<api-client-id>

targetScope = 'resourceGroup'

@description('Short name used in resource names.')
@maxLength(16)
param appName string = 'appointments'

param location string = resourceGroup().location

@description('Image to run. The deploy workflow passes the running image here, then rolls out the new build.')
param containerImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@description('Entra tenant that issues access tokens for the API.')
param authTenantId string = tenant().tenantId

@description('Application (client) ID of the API app registration. Access tokens must carry it as their audience.')
param authAudience string

@description('Load the demo branches and services on startup.')
param seedDemoData bool = true

var suffix = uniqueString(resourceGroup().id)
var tags = { app: appName, source: 'github.com/coldworld22/appointments-api' }

// ---------- Observability ----------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${appName}-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${appName}-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------- Identity and registry ----------

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}'
  location: location
  tags: tags
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'cr${replace(appName, '-', '')}${suffix}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Azure SQL (Entra-only authentication) ----------

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${appName}-${suffix}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // The app's identity is the Entra admin, so DbUp can apply migrations at startup with no SQL logins at all.
    // In a larger estate: an Entra group as admin, migrations run from the pipeline, and the app granted
    // only db_datareader/db_datawriter. See README "Production notes".
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: identity.name
      sid: identity.properties.principalId
      principalType: 'Application'
      tenantId: subscription().tenantId
    }
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'appointments'
  location: location
  tags: tags
  // Serverless: scales between 0.5 and 1 vCore and pauses after an hour idle, so a demo costs close to nothing.
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
  }
}

// ---------- Container Apps ----------

resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${appName}-${suffix}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;Connect Timeout=60'

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${appName}'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  dependsOn: [
    acrPull
  ]
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ConnectionStrings__Appointments', value: sqlConnectionString }
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
            { name: 'Auth__Mode', value: 'Entra' }
            { name: 'Auth__TenantId', value: authTenantId }
            { name: 'Auth__Audience', value: authAudience }
            { name: 'Database__SeedDemoData', value: string(seedDemoData) }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output containerAppName string = app.name
output apiUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output identityClientId string = identity.properties.clientId
