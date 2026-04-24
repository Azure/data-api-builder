param(
    [string] $ResourceGroup = "rg-dab-aca-mcp-auth-demo",
    [string] $Location = "westus3",
    [string] $Suffix,
    [string] $SqlAdminLogin = "sqladminuser",
    [string] $DatabaseName = "dabdemo"
)

$ErrorActionPreference = "Stop"

function New-LowerSuffix {
    -join ((48..57) + (97..122) | Get-Random -Count 6 | ForEach-Object { [char] $_ })
}

function New-SqlPassword {
    "Dab!" + [Guid]::NewGuid().ToString("N") + "9"
}

function Convert-GuidToSqlSidHex {
    param([string] $Guid)
    $bytes = ([Guid] $Guid).ToByteArray()
    "0x" + (($bytes | ForEach-Object { $_.ToString("X2") }) -join "")
}

function Escape-SqlIdentifier {
    param([string] $Value)
    $Value.Replace("]", "]]")
}

function Escape-SqlLiteral {
    param([string] $Value)
    $Value.Replace("'", "''")
}

function Invoke-AzCli {
    param([scriptblock] $Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: $Command"
    }
}

if ([string]::IsNullOrWhiteSpace($Suffix)) {
    $Suffix = New-LowerSuffix
}

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$TemplatePath = Join-Path $PSScriptRoot "dab-config.template.json"
$GeneratedConfigPath = Join-Path $PSScriptRoot "dab-config.generated.json"
$SchemaPath = Join-Path $PSScriptRoot "schema.sql"
$OutputsPath = Join-Path $PSScriptRoot "deployment.outputs.json"

$suffixCompact = ($Suffix -replace "[^a-z0-9]", "").ToLowerInvariant()
if ($suffixCompact.Length -lt 4) {
    throw "Suffix must contain at least four lowercase letters or numbers."
}

$acrName = "acrdabmcp$suffixCompact"
$keyVaultName = "kv-dabmcp-$suffixCompact"
$sqlServerName = "sql-dabmcp-$suffixCompact"
$identityName = "id-dabmcp-$suffixCompact"
$logAnalyticsName = "log-dabmcp-$suffixCompact"
$containerEnvName = "cae-dabmcp-$suffixCompact"
$containerAppName = "ca-dabmcp-$suffixCompact"
$appDisplayName = "dab-aca-mcp-demo-$suffixCompact"
$imageName = "dab-aca-mcp-demo:$suffixCompact"
$sqlAdminPassword = New-SqlPassword
$azureSqlAppId = "022907d3-0f1b-48f7-badc-1ba6abab6d66"
$azureSqlUserImpersonationScopeId = "c39ef2d1-04ce-46dc-8b5f-e9a5c60f0fc9"
$azureCliClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46"

Write-Host "Using suffix '$suffixCompact' in resource group '$ResourceGroup' ($Location)."

$account = az account show -o json | ConvertFrom-Json
if (-not $account) {
    throw "Azure CLI is not logged in. Run 'az login' and rerun this script."
}

$tenantId = $account.tenantId
$subscriptionId = $account.id
$signedInUser = az ad signed-in-user show --query "{id:id,userPrincipalName:userPrincipalName,displayName:displayName}" -o json | ConvertFrom-Json
if (-not $signedInUser.id) {
    throw "Could not resolve the signed-in Microsoft Entra user."
}

Write-Host "Creating Microsoft Entra application for DAB JWT validation and OBO..."
$app = az ad app create --display-name $appDisplayName --sign-in-audience AzureADMyOrg -o json | ConvertFrom-Json
$apiClientId = $app.appId
$apiIdentifierUri = "api://$apiClientId"
$apiAudience = $apiClientId
$apiScope = "$apiIdentifierUri/access_as_user"
$scopeId = [Guid]::NewGuid().ToString()

Invoke-AzCli { az ad app update --id $apiClientId --identifier-uris $apiIdentifierUri --requested-access-token-version 2 }

$apiScopePatch = @{
    api = @{
        requestedAccessTokenVersion = 2
        oauth2PermissionScopes = @(
            @{
                adminConsentDescription = "Allow signed-in users to call the DAB MCP demo API."
                adminConsentDisplayName = "Access DAB MCP demo"
                id = $scopeId
                isEnabled = $true
                type = "User"
                userConsentDescription = "Allow this client to call the DAB MCP demo API on your behalf."
                userConsentDisplayName = "Access DAB MCP demo"
                value = "access_as_user"
            }
        )
    }
} | ConvertTo-Json -Depth 10

$apiScopePatchFile = New-TemporaryFile
$apiScopePatch | Set-Content -Path $apiScopePatchFile.FullName -Encoding utf8
Invoke-AzCli { az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$($app.id)" --headers "Content-Type=application/json" --body "@$($apiScopePatchFile.FullName)" }
Remove-Item -LiteralPath $apiScopePatchFile.FullName -Force

$apiPreAuthPatch = @{
    api = @{
        preAuthorizedApplications = @(
            @{
                appId = $azureCliClientId
                delegatedPermissionIds = @($scopeId)
            }
        )
    }
} | ConvertTo-Json -Depth 10

$apiPreAuthPatchFile = New-TemporaryFile
$apiPreAuthPatch | Set-Content -Path $apiPreAuthPatchFile.FullName -Encoding utf8
Invoke-AzCli { az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$($app.id)" --headers "Content-Type=application/json" --body "@$($apiPreAuthPatchFile.FullName)" }
Remove-Item -LiteralPath $apiPreAuthPatchFile.FullName -Force

Invoke-AzCli { az ad sp create --id $apiClientId | Out-Null }
Invoke-AzCli { az ad app permission add --id $apiClientId --api $azureSqlAppId --api-permissions "$azureSqlUserImpersonationScopeId=Scope" | Out-Null }

try {
    Invoke-AzCli { az ad app permission grant --id $apiClientId --api $azureSqlAppId --scope user_impersonation | Out-Null }
    $adminConsentGranted = $true
    try {
        Invoke-AzCli { az ad app permission admin-consent --id $apiClientId | Out-Null }
    }
    catch {
        Write-Warning "Delegated Azure SQL permission grant was created, but admin-consent returned a non-fatal warning: $($_.Exception.Message)"
    }
}
catch {
    $adminConsentGranted = $false
    Write-Warning "Admin consent for Azure SQL delegated access was not granted automatically. OBO requests will fail until an admin grants consent to '$appDisplayName'."
}

$credential = az ad app credential reset --id $apiClientId --append --display-name "dab-obo-client-secret" --years 1 -o json | ConvertFrom-Json
$oboClientSecret = $credential.password

Write-Host "Creating Azure resources..."
Invoke-AzCli { az group create --name $ResourceGroup --location $Location | Out-Null }
$identity = az identity create --name $identityName --resource-group $ResourceGroup --location $Location -o json | ConvertFrom-Json
$identityId = $identity.id
$identityClientId = $identity.clientId
$identityPrincipalId = $identity.principalId

Invoke-AzCli { az acr create --name $acrName --resource-group $ResourceGroup --location $Location --sku Basic --admin-enabled false | Out-Null }
for ($i = 0; $i -lt 30; $i++) {
    $acr = az acr show --name $acrName --resource-group $ResourceGroup -o json 2>$null | ConvertFrom-Json
    if ($acr -and $acr.id) {
        break
    }

    Start-Sleep -Seconds 10
}

if (-not $acr -or -not $acr.id) {
    throw "ACR '$acrName' was not queryable after creation."
}

$acrLoginServer = $acr.loginServer
Invoke-AzCli { az role assignment create --assignee $identityPrincipalId --role AcrPull --scope $acr.id | Out-Null }

Invoke-AzCli { az keyvault create --name $keyVaultName --resource-group $ResourceGroup --location $Location --enable-rbac-authorization false | Out-Null }
Invoke-AzCli { az keyvault set-policy --name $keyVaultName --resource-group $ResourceGroup --object-id $identityPrincipalId --secret-permissions get list | Out-Null }
Invoke-AzCli { az keyvault set-policy --name $keyVaultName --resource-group $ResourceGroup --object-id $signedInUser.id --secret-permissions get list set delete recover purge | Out-Null }

$keyVaultUri = "https://$keyVaultName.vault.azure.net/"
$sqlConnectionString = "Server=tcp:$sqlServerName.database.windows.net,1433;Database=$DatabaseName;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
Invoke-AzCli { az keyvault secret set --vault-name $keyVaultName --name "sql-connection-string" --value $sqlConnectionString | Out-Null }
Invoke-AzCli { az keyvault secret set --vault-name $keyVaultName --name "obo-client-secret" --value $oboClientSecret | Out-Null }
Invoke-AzCli { az keyvault secret set --vault-name $keyVaultName --name "sql-admin-password" --value $sqlAdminPassword | Out-Null }
$oboSecretId = az keyvault secret show --vault-name $keyVaultName --name "obo-client-secret" --query id -o tsv

Write-Host "Creating Azure SQL server and database..."
Invoke-AzCli { az sql server create --name $sqlServerName --resource-group $ResourceGroup --location $Location --admin-user $SqlAdminLogin --admin-password $sqlAdminPassword | Out-Null }
Invoke-AzCli { az sql server ad-admin create --resource-group $ResourceGroup --server-name $sqlServerName --display-name $signedInUser.userPrincipalName --object-id $signedInUser.id | Out-Null }
Invoke-AzCli { az sql db create --resource-group $ResourceGroup --server $sqlServerName --name $DatabaseName --edition Basic --capacity 5 | Out-Null }
Invoke-AzCli { az sql server firewall-rule create --resource-group $ResourceGroup --server $sqlServerName --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 | Out-Null }

try {
    $localIp = (Invoke-RestMethod -Uri "https://api.ipify.org" -TimeoutSec 20).Trim()
    Invoke-AzCli { az sql server firewall-rule create --resource-group $ResourceGroup --server $sqlServerName --name AllowLocalClient --start-ip-address $localIp --end-ip-address $localIp | Out-Null }
}
catch {
    Write-Warning "Could not discover local public IP. If sqlcmd cannot connect, add a client firewall rule manually."
}

Write-Host "Preparing sample schema and SQL users..."
$identitySid = Convert-GuidToSqlSidHex $identityClientId
$userSid = Convert-GuidToSqlSidHex $signedInUser.id
$identitySqlName = Escape-SqlIdentifier $identityName
$userSqlName = Escape-SqlIdentifier $signedInUser.userPrincipalName
$identitySqlLiteral = Escape-SqlLiteral $identityName
$userSqlLiteral = Escape-SqlLiteral $signedInUser.userPrincipalName

$principalSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$identitySqlLiteral')
BEGIN
    CREATE USER [$identitySqlName] WITH SID = $identitySid, TYPE = E;
END;

IF IS_ROLEMEMBER(N'db_datareader', N'$identitySqlLiteral') = 0
BEGIN
    ALTER ROLE db_datareader ADD MEMBER [$identitySqlName];
END;

IF IS_ROLEMEMBER(N'db_datawriter', N'$identitySqlLiteral') = 0
BEGIN
    ALTER ROLE db_datawriter ADD MEMBER [$identitySqlName];
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$userSqlLiteral')
BEGIN
    CREATE USER [$userSqlName] WITH SID = $userSid, TYPE = E;
END;

IF IS_ROLEMEMBER(N'db_datareader', N'$userSqlLiteral') = 0
BEGIN
    ALTER ROLE db_datareader ADD MEMBER [$userSqlName];
END;

IF IS_ROLEMEMBER(N'db_datawriter', N'$userSqlLiteral') = 0
BEGIN
    ALTER ROLE db_datawriter ADD MEMBER [$userSqlName];
END;
"@

$principalSqlFile = New-TemporaryFile
$principalSql | Set-Content -Path $principalSqlFile.FullName -Encoding utf8
sqlcmd -S "$sqlServerName.database.windows.net" -d $DatabaseName -U $SqlAdminLogin -P $sqlAdminPassword -N -C -b -i $SchemaPath
if ($LASTEXITCODE -ne 0) { throw "Failed to apply sample schema." }
sqlcmd -S "$sqlServerName.database.windows.net" -d $DatabaseName -U $SqlAdminLogin -P $sqlAdminPassword -N -C -b -i $principalSqlFile.FullName
if ($LASTEXITCODE -ne 0) { throw "Failed to create SQL users for managed identity and signed-in user." }
Remove-Item -LiteralPath $principalSqlFile.FullName -Force

Write-Host "Generating ACA-specific dab-config.generated.json..."
$config = Get-Content -Path $TemplatePath -Raw
$config = $config.Replace("__KEY_VAULT_ENDPOINT__", $keyVaultUri)
$config = $config.Replace("__TENANT_ID__", $tenantId)
$config = $config.Replace("__DAB_API_AUDIENCE__", $apiAudience)
$config | Set-Content -Path $GeneratedConfigPath -Encoding utf8

Write-Host "Building DAB image in Azure Container Registry..."
Push-Location $RepoRoot
try {
    Invoke-AzCli { az acr build --registry $acrName --resource-group $ResourceGroup --image $imageName --file "samples/azure/container-apps-entra-mcp/Dockerfile" . }
}
finally {
    Pop-Location
}

Write-Host "Creating Container Apps environment and app..."
$workspace = az monitor log-analytics workspace create --resource-group $ResourceGroup --workspace-name $logAnalyticsName --location $Location -o json | ConvertFrom-Json
$workspaceCustomerId = $workspace.customerId
$workspaceKey = az monitor log-analytics workspace get-shared-keys --resource-group $ResourceGroup --workspace-name $logAnalyticsName --query primarySharedKey -o tsv
Invoke-AzCli { az containerapp env create --name $containerEnvName --resource-group $ResourceGroup --location $Location --logs-workspace-id $workspaceCustomerId --logs-workspace-key $workspaceKey | Out-Null }

Invoke-AzCli {
    az containerapp create `
        --name $containerAppName `
        --resource-group $ResourceGroup `
        --environment $containerEnvName `
        --image "$acrLoginServer/$imageName" `
        --ingress external `
        --target-port 5000 `
        --min-replicas 1 `
        --max-replicas 1 `
        --user-assigned $identityId `
        --registry-server $acrLoginServer `
        --registry-identity $identityId `
        --secrets "obo-secret=keyvaultref:$oboSecretId,identityref:$identityId" `
        --env-vars "AZURE_CLIENT_ID=$identityClientId" "DAB_OBO_CLIENT_ID=$apiClientId" "DAB_OBO_TENANT_ID=$tenantId" "DAB_OBO_CLIENT_SECRET=secretref:obo-secret" | Out-Null
}

$fqdn = az containerapp show --name $containerAppName --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn -o tsv
$baseUrl = "https://$fqdn"
$tokenCommand = "az account get-access-token --tenant $tenantId --scope `"$apiScope`" --query accessToken -o tsv"

$outputs = [ordered]@{
    resourceGroup = $ResourceGroup
    location = $Location
    tenantId = $tenantId
    subscriptionId = $subscriptionId
    containerAppName = $containerAppName
    containerAppUrl = $baseUrl
    mcpEndpoint = "$baseUrl/mcp"
    restTodosEndpoint = "$baseUrl/api/dbo_Todos"
    graphqlEndpoint = "$baseUrl/graphql"
    keyVaultName = $keyVaultName
    sqlServerName = $sqlServerName
    sqlDatabaseName = $DatabaseName
    managedIdentityName = $identityName
    appRegistrationDisplayName = $appDisplayName
    appClientId = $apiClientId
    appAudience = $apiAudience
    tokenScope = $apiScope
    azureSqlAdminConsentGranted = $adminConsentGranted
    tokenCommand = $tokenCommand
}

$outputs | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputsPath -Encoding utf8

Write-Host ""
Write-Host "Deployment complete."
Write-Host "Container App: $baseUrl"
Write-Host "REST sample:   $baseUrl/api/dbo_Todos"
Write-Host "GraphQL:       $baseUrl/graphql"
Write-Host "MCP:           $baseUrl/mcp"
Write-Host "Outputs file:  $OutputsPath"
Write-Host ""
Write-Host "Get a user token with:"
Write-Host $tokenCommand

try {
    Write-Host ""
    Write-Host "Trying authenticated REST validation..."
    $token = az account get-access-token --tenant $tenantId --scope $apiScope --query accessToken -o tsv
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($token)) {
        Start-Sleep -Seconds 20
        $headers = @{ Authorization = "Bearer $token" }
        $result = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/dbo_Todos?`$first=5" -Headers $headers -TimeoutSec 60
        Write-Host "REST validation succeeded. Returned rows:"
        ($result.value | ConvertTo-Json -Depth 5) | Write-Host
    }
}
catch {
    Write-Warning "Deployment succeeded, but REST validation did not complete: $($_.Exception.Message)"
    Write-Warning "Check Container App logs and confirm admin consent if token or OBO acquisition failed."
}
