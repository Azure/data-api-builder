<#
.SYNOPSIS
    Deploy Data API builder (DAB) to Azure Container Instances with MS SQL Server and test data.

.DESCRIPTION
    This script automates the deployment of Data API builder to Azure Container Instances.
    It creates all required Azure resources including:
    - Resource Group
    - Azure Container Registry (ACR)
    - MS SQL Server and Database
    - Container Instance with DAB
    - Loads test data from DatabaseSchema-MsSql.sql
    - Configures firewall rules for secure access
    - Generates DAB configuration file

.PARAMETER SubscriptionId
    Azure subscription ID. If not provided, uses the current active subscription.

.PARAMETER ResourceGroup
    Name of the resource group. Auto-generates if not provided.

.PARAMETER Location
    Azure region (e.g., eastus, westus2). Default: eastus

.PARAMETER ResourcePrefix
    Prefix for resource names. Auto-generates if not provided.

.PARAMETER SqlAdminPassword
    SQL Server admin password. Auto-generates a secure password if not provided.

.PARAMETER SkipCleanup
    If set, resources won't be deleted on errors.

.PARAMETER ContainerPort
    Port for the DAB container. Default: 5000

.PARAMETER SqlServiceTier
    SQL Database service tier. Default: S0

.PARAMETER ContainerCpu
    Number of CPU cores for the container. Default: 1

.PARAMETER ContainerMemory
    Memory in GB for the container. Default: 1.5

.PARAMETER DabConfigFile
    Path to DAB configuration file. Default: src/Service.Tests/dab-config.MsSql.json

.EXAMPLE
    .\azure-container-instances-dab-starter.ps1
    Runs with auto-generated values and prompts for confirmation

.EXAMPLE
    .\azure-container-instances-dab-starter.ps1 -ResourcePrefix "mydab" -Location "westus2"
    Deploys to West US 2 with custom prefix

.EXAMPLE
    .\azure-container-instances-dab-starter.ps1 -ContainerCpu 2 -ContainerMemory 3
    Deploys with custom CPU and memory settings

.NOTES
    Prerequisites:
    - Azure CLI installed and logged in
    - Docker Desktop installed and running
    - sqlcmd utility installed (SQL Server Command Line Tools)
    - PowerShell 5.1 or higher
    - Sufficient permissions in Azure subscription
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroup,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet('eastus', 'eastus2', 'westus', 'westus2', 'westus3', 'centralus', 'northeurope', 'westeurope', 'uksouth', 'southeastasia')]
    [string]$Location = "eastus",
    
    [Parameter(Mandatory=$true)]
    [string]$ResourcePrefix,
    
    [Parameter(Mandatory=$true)]
    [SecureString]$SqlAdminPassword,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipCleanup,
    
    [Parameter(Mandatory=$false)]
    [int]$ContainerPort = 5000,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet('Basic', 'S0', 'S1', 'S2', 'P1', 'P2')]
    [string]$SqlServiceTier = "S0",
    
    [Parameter(Mandatory=$false)]
    [ValidateRange(1, 4)]
    [int]$ContainerCpu = 1,
    
    [Parameter(Mandatory=$false)]
    [ValidateRange(0.5, 16)]
    [double]$ContainerMemory = 1.5,
    
    [Parameter(Mandatory=$false)]
    [string]$DabConfigFile
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-ErrorMessage {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Test-Prerequisites {
    Write-Step "Checking prerequisites..."
    
    # Check Azure CLI
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-ErrorMessage "Azure CLI is not installed. Please install from: https://aka.ms/InstallAzureCLIDirect"
        exit 1
    }
    Write-Success "Azure CLI installed"
    
    # Check Docker
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-ErrorMessage "Docker is not installed. Please install Docker Desktop from: https://www.docker.com/products/docker-desktop"
        exit 1
    }
    
    # Check if Docker is running
    try {
        $dockerOutput = docker ps 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMessage "Docker is not running. Please start Docker Desktop."
            Write-Host "Error: $dockerOutput" -ForegroundColor Red
            exit 1
        }
        Write-Success "Docker is running"
    }
    catch {
        Write-ErrorMessage "Docker is not running or not responding. Please start Docker Desktop."
        Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    
    # Check sqlcmd
    if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
        Write-Warning "sqlcmd is not installed. Installing SQL Server Command Line Tools..."
        Write-Host "Please install from: https://aka.ms/sqlcmd" -ForegroundColor Yellow
        Write-Host "Or run: winget install Microsoft.SqlServer.SqlCmd" -ForegroundColor Yellow
        exit 1
    }
    Write-Success "sqlcmd installed"
    
    # Check if logged into Azure
    $account = az account show 2>$null | ConvertFrom-Json
    if (-not $account) {
        Write-ErrorMessage "Not logged into Azure. Please run: az login"
        exit 1
    }
    Write-Success "Logged into Azure as $($account.user.name)"
}

function Get-UniqueResourceName {
    param([string]$Prefix, [string]$Type)
    $random = -join ((48..57) + (97..122) | Get-Random -Count 6 | ForEach-Object {[char]$_})
    return "$Prefix-$Type-$random".ToLower()
}

function New-RandomPassword {
    param(
        [int]$Length = 16
    )
    $chars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()'
    $password = -join ((1..$Length) | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })
    # Ensure complexity requirements
    $password = 'A1!' + $password
    return $password
}

function Update-DabConfigFile {
    param(
        [string]$SourceConfigPath,
        [string]$ConnectionString,
        [string]$OutputPath
    )
    
    # Read the source config file
    $configContent = Get-Content -Path $SourceConfigPath -Raw
    
    # Parse JSON
    $config = $configContent | ConvertFrom-Json
    
    # Update the connection string
    $config.'data-source'.'connection-string' = $ConnectionString
    
    # Ensure production mode for Container Instances
    if ($config.runtime.host.mode -eq "development") {
        $config.runtime.host.mode = "production"
    }
    
    # Convert back to JSON and save
    $configContent = $config | ConvertTo-Json -Depth 100
    $configContent | Set-Content -Path $OutputPath -Encoding UTF8
    
    Write-Success "Updated DAB config file: $OutputPath"
}

# ============================================================================
# MAIN SCRIPT
# ============================================================================

Write-Host "`n" -ForegroundColor Blue
Write-Host "==========================================================================" -ForegroundColor Blue
Write-Host "  Data API Builder - Azure Container Instances Deployment" -ForegroundColor Blue
Write-Host "==========================================================================" -ForegroundColor Blue
Write-Host ""

# Check prerequisites
Test-Prerequisites

# Find repo root
$repoPath = $PSScriptRoot
while ($repoPath -and -not (Test-Path (Join-Path $repoPath "Dockerfile"))) {
    $repoPath = Split-Path $repoPath -Parent
}

if (-not $repoPath -or -not (Test-Path (Join-Path $repoPath "Dockerfile"))) {
    Write-ErrorMessage "Could not find data-api-builder repository root (looking for Dockerfile)"
    exit 1
}

$sqlScriptPath = Join-Path $repoPath "src\Service.Tests\DatabaseSchema-MsSql.sql"
if (-not (Test-Path $sqlScriptPath)) {
    Write-ErrorMessage "Could not find SQL script at: $sqlScriptPath"
    exit 1
}

# Set default DAB config file path if not provided
if (-not $DabConfigFile) {
    $DabConfigFile = Join-Path $repoPath "src\Service.Tests\dab-config.MsSql.json"
}

if (-not (Test-Path $DabConfigFile)) {
    Write-ErrorMessage "DAB config file not found at: $DabConfigFile"
    exit 1
}

Write-Success "Repository root: $repoPath"
Write-Success "Using DAB config: $DabConfigFile"

# Generate or validate parameters
if (-not $ResourcePrefix) {
    $ResourcePrefix = "dab$(Get-Random -Maximum 9999)"
    Write-Host "Generated resource prefix: $ResourcePrefix" -ForegroundColor Yellow
}

if (-not $ResourceGroup) {
    $ResourceGroup = "rg-$ResourcePrefix"
    Write-Host "Generated resource group: $ResourceGroup" -ForegroundColor Yellow
}

# Set subscription
if ($SubscriptionId) {
    Write-Step "Setting Azure subscription..."
    az account set --subscription $SubscriptionId
    Write-Success "Subscription set to: $SubscriptionId"
}
else {
    $currentSub = az account show | ConvertFrom-Json
    $SubscriptionId = $currentSub.id
    Write-Host "Using current subscription: $($currentSub.name) ($SubscriptionId)" -ForegroundColor Yellow
}

# Generate resource names
$acrName = Get-UniqueResourceName -Prefix $ResourcePrefix -Type "acr"
$acrName = $acrName -replace "[^a-zA-Z0-9]", "" # ACR names must be alphanumeric
$aciName = Get-UniqueResourceName -Prefix $ResourcePrefix -Type "aci"
$sqlServerName = Get-UniqueResourceName -Prefix $ResourcePrefix -Type "sql"
$sqlDbName = "dabdb"
$sqlAdminUser = "sqladmin"
$dnsLabel = Get-UniqueResourceName -Prefix $ResourcePrefix -Type "dab"
$dnsLabel = $dnsLabel -replace "[^a-zA-Z0-9-]", "" # DNS labels must be alphanumeric with hyphens
$acrImageName = "dab"
$acrImageTag = "latest"

# Generate SQL password if not provided
if (-not $SqlAdminPassword) {
    $generatedPassword = New-RandomPassword
    $SqlAdminPassword = ConvertTo-SecureString -String $generatedPassword -AsPlainText -Force
    
    # Store password to file for secure retrieval
    $passwordFile = Join-Path $env:TEMP "dab-sql-password-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"
    $generatedPassword | Out-File -FilePath $passwordFile -NoNewline
    
    # Restrict file permissions to current user only (requires elevated privileges)
    try {
        $acl = Get-Acl $passwordFile
        $acl.SetAccessRuleProtection($true, $false)
        $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($env:USERNAME, "FullControl", "Allow")
        $acl.SetAccessRule($accessRule)
        Set-Acl $passwordFile $acl
    }
    catch {
        Write-Warning "Could not restrict password file ACL permissions (requires admin rights)."
    }
    
    Write-Warning "Auto-generated SQL password has been saved to: $passwordFile"
    Write-Host "Please store this password securely and delete the file after saving it to your password manager." -ForegroundColor Yellow
}

# Convert SecureString to plain text with proper memory management
$bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlAdminPassword)
try {
    $sqlAdminPasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
}
finally {
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}

# Summary
Write-Host "`nDeployment Configuration:" -ForegroundColor Cyan
Write-Host "  Subscription:     $SubscriptionId"
Write-Host "  Resource Group:   $ResourceGroup"
Write-Host "  Location:         $Location"
Write-Host "  ACR Name:         $acrName"
Write-Host "  Container Name:   $aciName"
Write-Host "  DNS Label:        $dnsLabel"
Write-Host "  SQL Server:       $sqlServerName"
Write-Host "  SQL Database:     $sqlDbName"
Write-Host "  SQL Admin User:   $sqlAdminUser"
Write-Host "  Container Port:   $ContainerPort"
Write-Host "  Container CPU:    $ContainerCpu cores"
Write-Host "  Container Memory: $ContainerMemory GB"

$confirmation = Read-Host "`nProceed with deployment? (y/N)"
if ($confirmation -ne 'y') {
    Write-Host "Deployment cancelled." -ForegroundColor Yellow
    exit 0
}

try {
    # Create Resource Group
    Write-Step "Creating resource group..."
    az group create --name $ResourceGroup --location $Location | Out-Null
    Write-Success "Resource group created: $ResourceGroup"

    # Create ACR
    Write-Step "Creating Azure Container Registry..."
    az acr create `
        --resource-group $ResourceGroup `
        --name $acrName `
        --sku Basic `
        --admin-enabled true | Out-Null
    Write-Success "ACR created: $acrName"

    $acrLoginServer = az acr show --name $acrName --query "loginServer" -o tsv
    $acrPassword = az acr credential show --name $acrName --query "passwords[0].value" -o tsv

    # Build and Push Docker Image
    Write-Step "Building Docker image... (this may take a few minutes)"
    docker build -f "$repoPath\Dockerfile" -t "${acrImageName}:${acrImageTag}" $repoPath
    Write-Success "Docker image built"

    Write-Step "Pushing image to ACR..."
    docker tag "${acrImageName}:${acrImageTag}" "${acrLoginServer}/${acrImageName}:${acrImageTag}"
    az acr login --name $acrName | Out-Null
    docker push "${acrLoginServer}/${acrImageName}:${acrImageTag}"
    Write-Success "Image pushed to ACR"

    # Create SQL Server
    Write-Step "Creating SQL Server..."
    az sql server create `
        --name $sqlServerName `
        --resource-group $ResourceGroup `
        --location $Location `
        --admin-user $sqlAdminUser `
        --admin-password $sqlAdminPasswordPlain | Out-Null
    Write-Success "SQL Server created: $sqlServerName"

    # Create SQL Database
    Write-Step "Creating SQL Database..."
    az sql db create `
        --resource-group $ResourceGroup `
        --server $sqlServerName `
        --name $sqlDbName `
        --service-objective $SqlServiceTier | Out-Null
    Write-Success "SQL Database created: $sqlDbName"

    # Configure firewall rules
    Write-Step "Configuring SQL Server firewall rules..."
    
    # Allow Azure services (correct range for Azure services)
    az sql server firewall-rule create `
        --resource-group $ResourceGroup `
        --server $sqlServerName `
        --name "AllowAzureServices" `
        --start-ip-address 0.0.0.0 `
        --end-ip-address 0.0.0.0 | Out-Null
    
    # Allow client IP
    $myIp = (Invoke-WebRequest -Uri "https://api.ipify.org" -UseBasicParsing).Content.Trim()
    az sql server firewall-rule create `
        --resource-group $ResourceGroup `
        --server $sqlServerName `
        --name "ClientIP" `
        --start-ip-address $myIp `
        --end-ip-address $myIp | Out-Null
    
    Write-Success "Firewall rules configured (Azure services + your IP: $myIp)"

    # Load test data
    Write-Step "Loading test data into database..."
    $sqlServer = "$sqlServerName.database.windows.net"
    
    # Wait for firewall rules to propagate (Azure can take up to 5 minutes)
    Write-Host "Waiting for firewall rules to propagate..." -ForegroundColor Yellow
    Start-Sleep -Seconds 30
    
    $maxRetries = 3
    $retryCount = 0
    $dataLoaded = $false
    
    while (-not $dataLoaded -and $retryCount -lt $maxRetries) {
        try {
            if ($retryCount -gt 0) {
                Write-Host "Retry attempt $retryCount of $maxRetries..." -ForegroundColor Yellow
            }
            
$output = sqlcmd -S $sqlServer -U $sqlAdminUser -P $sqlAdminPasswordPlain -d $sqlDbName -i $sqlScriptPath -I -b 2>&1
            
            if ($LASTEXITCODE -eq 0) {
                $dataLoaded = $true
                Write-Success "Test data loaded successfully"
            }
            else {
                throw "sqlcmd returned exit code $LASTEXITCODE"
            }
        }
        catch {
            $errorMessage = $_.Exception.Message
            if ($output) { $errorMessage = $output | Out-String }
            
            # Check if error is due to IP not allowed
            if ($errorMessage -match "Client with IP address '([0-9.]+)' is not allowed") {
                $blockedIp = $matches[1]
                Write-Warning "Connection blocked from IP: $blockedIp (detected IP was: $myIp)"
                Write-Host "Adding blocked IP to firewall rules..." -ForegroundColor Yellow
                
                try {
                    az sql server firewall-rule create `
                        --resource-group $ResourceGroup `
                        --server $sqlServerName `
                        --name "ClientIP-Actual" `
                        --start-ip-address $blockedIp `
                        --end-ip-address $blockedIp | Out-Null
                    
                    Write-Host "Waiting for new firewall rule to propagate..." -ForegroundColor Yellow
                    Start-Sleep -Seconds 30
                }
                catch {
                    Write-Warning "Failed to add blocked IP to firewall: $_"
                }
            }
            elseif ($errorMessage -match "It may take up to five minutes") {
                Write-Host "Firewall rules still propagating. Waiting 60 seconds..." -ForegroundColor Yellow
                Start-Sleep -Seconds 60
            }
            else {
                Write-Warning "SQL connection error: $errorMessage"
                if ($retryCount -lt $maxRetries - 1) {
                    Write-Host "Waiting 30 seconds before retry..." -ForegroundColor Yellow
                    Start-Sleep -Seconds 30
                }
            }
            
            $retryCount++
        }
    }
    
    if (-not $dataLoaded) {
        Write-Warning "Failed to load test data after $maxRetries attempts. You may need to run it manually."
            Write-Host "Command: sqlcmd -S $sqlServer -U $sqlAdminUser -P *** -d $sqlDbName -i $sqlScriptPath -I" -ForegroundColor Yellow
    }

    # Prepare DAB config with actual connection string
    Write-Step "Preparing DAB configuration..."
    $dabConfigPath = Join-Path $env:TEMP "dab-config.json"
    $connectionString = "Server=$sqlServer,1433;Persist Security Info=False;User ID=$sqlAdminUser;Password=$sqlAdminPasswordPlain;Initial Catalog=$sqlDbName;MultipleActiveResultSets=False;Connection Timeout=30;TrustServerCertificate=True;"
    
    Update-DabConfigFile -SourceConfigPath $DabConfigFile -ConnectionString $connectionString -OutputPath $dabConfigPath

    # Upload config to Azure Storage Account for container to download
    Write-Step "Creating storage account for config file..."
    $storageAccountName = "dabstorage$(Get-Random -Minimum 10000 -Maximum 99999)"
    
    az storage account create `
        --name $storageAccountName `
        --resource-group $ResourceGroup `
        --location $Location `
        --sku Standard_LRS `
        --kind StorageV2 | Out-Null
    
    Write-Success "Storage account created: $storageAccountName"
    
    # Get storage account key
    $storageKey = az storage account keys list `
        --resource-group $ResourceGroup `
        --account-name $storageAccountName `
        --query "[0].value" -o tsv
    
    # Create container (blob container, not ACI)
    az storage container create `
        --name "config" `
        --account-name $storageAccountName `
        --account-key $storageKey | Out-Null
    
    # Upload config file
    az storage blob upload `
        --account-name $storageAccountName `
        --account-key $storageKey `
        --container-name "config" `
        --name "dab-config.json" `
        --file $dabConfigPath `
        --overwrite | Out-Null
    
    Write-Success "Config file uploaded to blob storage"
    
    # Generate SAS URL for the blob (valid for 1 year)
    $expiryDate = (Get-Date).AddYears(1).ToString("yyyy-MM-ddTHH:mm:ssZ")
    $configUrl = az storage blob generate-sas `
        --account-name $storageAccountName `
        --account-key $storageKey `
        --container-name "config" `
        --name "dab-config.json" `
        --permissions r `
        --expiry $expiryDate `
        --full-uri -o tsv
    
    Write-Success "Generated SAS URL for config download"
    
    # Deploy Container Instance with command to download config
    Write-Step "Deploying Container Instance..."
    Write-Host "Note: Container will download config from blob storage on startup" -ForegroundColor Yellow
    
    # Create ARM template to properly handle startup command with special characters
    $armTemplate = @{
        '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#'
        contentVersion = '1.0.0.0'
        resources = @(
            @{
                type = 'Microsoft.ContainerInstance/containerGroups'
                apiVersion = '2021-09-01'
                name = $aciName
                location = $Location
                properties = @{
                    containers = @(
                        @{
                            name = 'dab'
                            properties = @{
                                image = "${acrLoginServer}/${acrImageName}:${acrImageTag}"
                                resources = @{
                                    requests = @{
                                        cpu = $ContainerCpu
                                        memoryInGB = $ContainerMemory
                                    }
                                }
                                ports = @(
                                    @{
                                        port = $ContainerPort
                                        protocol = 'TCP'
                                    }
                                )
                                environmentVariables = @(
                                    @{
                                        name = 'ASPNETCORE_URLS'
                                        value = "http://+:$ContainerPort"
                                    }
                                    @{
                                        name = 'CONFIG_URL'
                                        value = $configUrl
                                    }
                                )
                                command = @('/bin/sh', '-c', 'curl -o /App/dab-config.json "$CONFIG_URL" && dotnet Azure.DataApiBuilder.Service.dll --ConfigFileName /App/dab-config.json')
                            }
                        }
                    )
                    imageRegistryCredentials = @(
                        @{
                            server = $acrLoginServer
                            username = $acrName
                            password = $acrPassword
                        }
                    )
                    ipAddress = @{
                        type = 'Public'
                        dnsNameLabel = $dnsLabel
                        ports = @(
                            @{
                                port = $ContainerPort
                                protocol = 'TCP'
                            }
                        )
                    }
                    osType = 'Linux'
                    restartPolicy = 'Always'
                }
            }
        )
    }
    
    $armTemplatePath = Join-Path $env:TEMP "aci-deployment-template.json"
    $armTemplate | ConvertTo-Json -Depth 10 | Set-Content -Path $armTemplatePath -Encoding UTF8
    
    az deployment group create `
        --resource-group $ResourceGroup `
        --template-file $armTemplatePath | Out-Null
    
    Remove-Item $armTemplatePath -ErrorAction SilentlyContinue
    
    Write-Success "Container Instance deployed"
    
    # Get container FQDN
    $containerFqdn = az container show `
        --resource-group $ResourceGroup `
        --name $aciName `
        --query "ipAddress.fqdn" -o tsv

    # Display summary
    Write-Host "`n" -ForegroundColor Green
    Write-Host "==========================================================================" -ForegroundColor Green
    Write-Host "  DEPLOYMENT SUCCESSFUL!" -ForegroundColor Green
    Write-Host "==========================================================================" -ForegroundColor Green
    Write-Host ""

    Write-Host "Resource Details:" -ForegroundColor Cyan
    Write-Host "  Resource Group:     $ResourceGroup"
    Write-Host "  Location:           $Location"
    Write-Host ""
    Write-Host "DAB Endpoints:" -ForegroundColor Cyan
    Write-Host "  App URL:            http://${containerFqdn}:$ContainerPort"
    Write-Host "  REST API:           http://${containerFqdn}:$ContainerPort/api"
    Write-Host "  GraphQL:            http://${containerFqdn}:$ContainerPort/graphql"
    Write-Host "  Health Check:       http://${containerFqdn}:$ContainerPort/health"
    Write-Host ""
    Write-Host "Database Connection:" -ForegroundColor Cyan
    Write-Host "  Server:             $sqlServer"
    Write-Host "  Database:           $sqlDbName"
    Write-Host "  Admin User:         $sqlAdminUser"
    Write-Host "  Admin Password:     ********** (saved to file earlier)"
    Write-Host ""
    Write-Host "Container Resources:" -ForegroundColor Cyan
    Write-Host "  CPU:                $ContainerCpu cores"
    Write-Host "  Memory:             $ContainerMemory GB"
    Write-Host ""
    Write-Host "Try these commands:" -ForegroundColor Cyan
    Write-Host "  # List all publishers"
    Write-Host "  curl http://${containerFqdn}:$ContainerPort/api/Publisher"
    Write-Host ""
    Write-Host "  # GraphQL query"
    Write-Host "  curl http://${containerFqdn}:$ContainerPort/graphql -H 'Content-Type: application/json' -d '{\"query\":\"{publishers{items{id name}}}\"}'
    Write-Host ""
    Write-Host "  # View container logs"
    Write-Host "  az container logs --resource-group $ResourceGroup --name $aciName"
    Write-Host ""
    Write-Host "Configuration file generated at: $dabConfigPath" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To delete all resources, run:" -ForegroundColor Yellow
    Write-Host "  az group delete --name $ResourceGroup --yes --no-wait"
    Write-Host ""
    Write-Host "NOTE: Container Instances use HTTP (not HTTPS). For production, consider using Azure Container Apps or App Service." -ForegroundColor Yellow
    Write-Host ""

}
catch {
    Write-ErrorMessage "Deployment failed: $_"
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    
    if (-not $SkipCleanup) {
        $cleanup = Read-Host "`nDelete created resources? (y/N)"
        if ($cleanup -eq 'y') {
            Write-Step "Cleaning up resources..."
            az group delete --name $ResourceGroup --yes --no-wait
            Write-Host "Cleanup initiated (running in background)" -ForegroundColor Yellow
        }
    }
    
    exit 1
}