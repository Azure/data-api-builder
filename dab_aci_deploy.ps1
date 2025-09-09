
# === CONFIGURABLE VARIABLES ===
$subscriptionId = "f33eb08a-3fe1-40e6-a9b6-2a9c376c616f"
$resourceGroup = "soghdabdevrg"
$location = "eastus"
$acrName = "dabdevacr"
$aciName = "dabdevaci"
$acrImageName = "dabdevacrimg"
$acrImageTag = "latest"
$dnsLabel = "dabaci"
$sqlServerName = "dabdevsqlserver"
$sqlAdminUser = "dabdevsqluser"
$sqlAdminPassword = "DabUserAdmin1$"
$sqlDbName = "dabdevsqldb"
$dockerfile = "Dockerfile"
$port = 5000

# === Set Azure Subscription ===
az account set --subscription $subscriptionId

# === Create Resource Group ===
az group create --name $resourceGroup --location $location

# === Create ACR ===
az acr create --resource-group $resourceGroup --name $acrName --sku Basic --admin-enabled true

# === Fetch ACR Credentials ===
$acrPassword = az acr credential show --name $acrName --query "passwords[0].value" -o tsv
$acrLoginServer = az acr show --name $acrName --query "loginServer" -o tsv

# === Build and Push Docker Image ===
docker build -f $dockerfile -t "$acrImageName`:$acrImageTag" .
docker tag "$acrImageName`:$acrImageTag" "$acrLoginServer/$acrImageName`:$acrImageTag"
az acr login --name $acrName
docker push "$acrLoginServer/$acrImageName`:$acrImageTag"

# === Create SQL Server and DB ===
az sql server create --name $sqlServerName `
  --resource-group $resourceGroup --location $location `
  --admin-user $sqlAdminUser --admin-password $sqlAdminPassword

az sql db create --resource-group $resourceGroup --server $sqlServerName --name $sqlDbName --service-objective S0

# === Allow Azure services to access SQL Server ===
az sql server firewall-rule create --resource-group $resourceGroup --server $sqlServerName `
  --name "AllowAzureServices" --start-ip-address 0.0.0.0 --end-ip-address 255.255.255.255

# === Create ACI Container ===
az container create `
  --resource-group $resourceGroup `
  --os-type "Linux" `
  --name $aciName `
  --image "$acrLoginServer/$acrImageName`:$acrImageTag" `
  --cpu 1 --memory 1.5 `
  --registry-login-server $acrLoginServer `
  --registry-username $acrName `
  --registry-password $acrPassword `
  --dns-name-label $dnsLabel `
  --environment-variables ASPNETCORE_URLS="http://+:$port" `
  --ports $port

Write-Host "Deployment complete. App should be accessible at: http://$dnsLabel.$location.azurecontainer.io:$port"

# az group delete --name $resourceGroup --yes --no-wait
