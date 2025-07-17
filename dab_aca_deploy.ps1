# === CONFIGURABLE VARIABLES ===
$subscriptionId   = "f33eb08a-3fe1-40e6-a9b6-2a9c376c616f"
$resourceGroup    = "soghdabdevrg"
$location         = "eastus"
$acrName          = "dabdevacr"
$acaName          = "dabdevaca"
$acrImageName     = "dabdevacrimg"
$acrImageTag      = "latest"
$dockerfile       = "Dockerfile"
$containerPort    = 1234
$sqlServerName    = "dabdevsqlserver"
$sqlAdminUser     = "dabdevsqluser"
$sqlAdminPassword = "DabUserAdmin1$"
$sqlDbName        = "dabdevsqldb"

# === Authenticate and Setup ===
az account set --subscription $subscriptionId
az group create --name $resourceGroup --location $location

# === Create ACR ===
az acr create `
  --resource-group $resourceGroup `
  --name $acrName `
  --sku Basic `
  --admin-enabled true

$acrLoginServer = az acr show --name $acrName --query "loginServer" -o tsv
$acrPassword    = az acr credential show --name $acrName --query "passwords[0].value" -o tsv

# === Build and Push Docker Image ===
docker build -f $dockerfile -t "${acrImageName}:${acrImageTag}" .
docker tag "${acrImageName}:${acrImageTag}" "${acrLoginServer}/${acrImageName}:${acrImageTag}"
az acr login --name $acrName
docker push "${acrLoginServer}/${acrImageName}:${acrImageTag}"

# === Create SQL Server and Database ===
az sql server create `
  --name $sqlServerName `
  --resource-group $resourceGroup `
  --location $location `
  --admin-user $sqlAdminUser `
  --admin-password $sqlAdminPassword

az sql db create `
  --resource-group $resourceGroup `
  --server $sqlServerName `
  --name $sqlDbName `
  --service-objective S0

az sql server firewall-rule create `
  --resource-group $resourceGroup `
  --server $sqlServerName `
  --name "AllowAzureServices" `
  --start-ip-address 0.0.0.0 `
  --end-ip-address 255.255.255.255

# === Create ACA Environment ===
$envName = "${acaName}Env"
az containerapp env create `
  --name $envName `
  --resource-group $resourceGroup `
  --location $location

# === Deploy Container App with Fixed Port ===
az containerapp create `
  --name $acaName `
  --resource-group $resourceGroup `
  --environment $envName `
  --image "${acrLoginServer}/${acrImageName}:${acrImageTag}" `
  --target-port $containerPort `
  --ingress external `
  --transport auto `
  --registry-server $acrLoginServer `
  --registry-username $acrName `
  --registry-password $acrPassword `
  --env-vars `
      ASPNETCORE_URLS="http://+:$containerPort" `
      SQL_SERVER="$sqlServerName.database.windows.net" `
      SQL_DATABASE="$sqlDbName" `
      SQL_USER="$sqlAdminUser" `
      SQL_PASSWORD="$sqlAdminPassword"

# === Output Public App URL ===
$appUrl = az containerapp show `
  --name $acaName `
  --resource-group $resourceGroup `
  --query "properties.configuration.ingress.fqdn" -o tsv

Write-Host "`n✅ Deployment complete."
Write-Host "🌐 DAB accessible at: https://$appUrl"
Write-Host "🩺 Health check endpoint: https://$appUrl/health"