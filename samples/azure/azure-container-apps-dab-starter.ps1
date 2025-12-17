<#
.SYNOPSIS
    Deploy Data API builder (DAB) to Azure Container Apps with MS SQL Server and test data.

.DESCRIPTION
    This script automates the deployment of Data API builder to Azure Container Apps.
    It creates all required Azure resources including:
    - Resource Group
    - Azure Container Registry (ACR)
    - MS SQL Server and Database
    - Container Apps Environment
    - Container App with DAB
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

.PARAMETER MinReplicas
    Minimum number of container replicas. Default: 1

.PARAMETER MaxReplicas
    Maximum number of container replicas. Default: 3

.EXAMPLE
    .\azure-container-apps-dab-starter.ps1
    Runs with auto-generated values and prompts for confirmation

.EXAMPLE
    .\azure-container-apps-dab-starter.ps1 -ResourcePrefix "mydab" -Location "westus2"
    Deploys to West US 2 with custom prefix

.EXAMPLE
    .\azure-container-apps-dab-starter.ps1 -SkipCleanup
    Deploys and keeps resources even if errors occur

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
    [Parameter(Mandatory=$false)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory=$false)]
    [string]$ResourceGroup,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet('eastus', 'eastus2', 'westus', 'westus2', 'westus3', 'centralus', 'northeurope', 'westeurope', 'uksouth', 'southeastasia')]
    [string]$Location = "eastus",
    
    [Parameter(Mandatory=$false)]
    [string]$ResourcePrefix,
    
    [Parameter(Mandatory=$false)]
    [SecureString]$SqlAdminPassword,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipCleanup,
    
    [Parameter(Mandatory=$false)]
    [int]$ContainerPort = 5000,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet('Basic', 'S0', 'S1', 'S2', 'P1', 'P2')]
    [string]$SqlServiceTier = "S0",
    
    [Parameter(Mandatory=$false)]
    [ValidateRange(0, 30)]
    [int]$MinReplicas = 1,
    
    [Parameter(Mandatory=$false)]
    [ValidateRange(1, 30)]
    [int]$MaxReplicas = 3
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

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Test-Prerequisites {
    Write-Step "Checking prerequisites..."
    
    # Check Azure CLI
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Error "Azure CLI is not installed. Please install from: https://aka.ms/InstallAzureCLIDirect"
        exit 1
    }
    Write-Success "Azure CLI installed"
    
    # Check Docker
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Error "Docker is not installed. Please install Docker Desktop from: https://www.docker.com/products/docker-desktop"
        exit 1
    }
    
    # Check if Docker is running
    try {
        docker ps | Out-Null
        Write-Success "Docker is running"
    }
    catch {
        Write-Error "Docker is not running. Please start Docker Desktop."
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
        Write-Error "Not logged into Azure. Please run: az login"
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
    $length = 16
    $chars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()'
    $password = -join ((1..$length) | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })
    # Ensure complexity requirements
    $password = 'A1!' + $password
    return $password
}

function New-DabConfigFile {
    param(
        [string]$SqlServer,
        [string]$SqlDatabase,
        [string]$SqlUser,
        [string]$SqlPassword,
        [string]$OutputPath
    )
    
    # Build config as JSON string directly to avoid PowerShell hashtable issues
    $configJson = @'
{
  "$schema": "https://github.com/Azure/data-api-builder/releases/latest/download/dab.draft.schema.json",
  "data-source": {
    "database-type": "mssql",
    "connection-string": "@env('DATABASE_CONNECTION_STRING')"
  },
  "runtime": {
    "rest": {
      "enabled": true,
      "path": "/api"
    },
    "graphql": {
      "enabled": true,
      "path": "/graphql",
      "allow-introspection": true
    },
    "host": {
      "mode": "production",
      "cors": {
        "origins": ["*"],
        "allow-credentials": false
      }
    }
  },
  "entities": {
    "Book": {
      "source": "books",
      "permissions": [
        {
          "role": "anonymous",
          "actions": ["create", "read", "update", "delete"]
        }
      ]
    },
    "Publisher": {
      "source": "publishers",
      "permissions": [
        {
          "role": "anonymous",
          "actions": ["create", "read", "update", "delete"]
        }
      ]
    },
    "Author": {
      "source": "authors",
      "permissions": [
        {
          "role": "anonymous",
          "actions": ["create", "read", "update", "delete"]
        }
      ]
    }
  }
}
'@
    
    $configJson | Set-Content -Path $OutputPath
    Write-Success "Generated DAB config file: $OutputPath"
}

# ============================================================================
# MAIN SCRIPT
# ============================================================================

Write-Host "`n" -ForegroundColor Blue
Write-Host "==========================================================================" -ForegroundColor Blue
Write-Host "  Data API Builder - Azure Container Apps Deployment" -ForegroundColor Blue
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
    Write-Error "Could not find data-api-builder repository root (looking for Dockerfile)"
    exit 1
}

$sqlScriptPath = Join-Path $repoPath "src\Service.Tests\DatabaseSchema-MsSql.sql"
if (-not (Test-Path $sqlScriptPath)) {
    Write-Error "Could not find SQL script at: $sqlScriptPath"
    exit 1
}

Write-Success "Repository root: $repoPath"

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
$acaName = Get-UniqueResourceName -Prefix $ResourcePrefix -Type "aca"
$sqlServerName = Get-UniqueResourceName -Prefix $ResourcePrefix -Type "sql"
$sqlDbName = "dabdb"
$sqlAdminUser = "sqladmin"
$envName = "${acaName}-env"
$acrImageName = "dab"
$acrImageTag = "latest"

# Generate SQL password if not provided
if (-not $SqlAdminPassword) {
    $generatedPassword = New-RandomPassword
    $SqlAdminPassword = ConvertTo-SecureString -String $generatedPassword -AsPlainText -Force
    Write-Host "Generated SQL Server admin password (save this): $generatedPassword" -ForegroundColor Yellow
}

$sqlAdminPasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlAdminPassword)
)

# Summary
Write-Host "`nDeployment Configuration:" -ForegroundColor Cyan
Write-Host "  Subscription:     $SubscriptionId"
Write-Host "  Resource Group:   $ResourceGroup"
Write-Host "  Location:         $Location"
Write-Host "  ACR Name:         $acrName"
Write-Host "  Container App:    $acaName"
Write-Host "  SQL Server:       $sqlServerName"
Write-Host "  SQL Database:     $sqlDbName"
Write-Host "  SQL Admin User:   $sqlAdminUser"
Write-Host "  Container Port:   $ContainerPort"
Write-Host "  Min Replicas:     $MinReplicas"
Write-Host "  Max Replicas:     $MaxReplicas"

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
    
    # Allow Azure services
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
    
    # Wait a moment for SQL server to be fully ready
    Start-Sleep -Seconds 10
    
    try {
        sqlcmd -S $sqlServer -U $sqlAdminUser -P $sqlAdminPasswordPlain -d $sqlDbName -i $sqlScriptPath -b
        Write-Success "Test data loaded successfully"
    }
    catch {
        Write-Warning "Failed to load test data. You may need to run it manually."
        Write-Host "Command: sqlcmd -S $sqlServer -U $sqlAdminUser -P *** -d $sqlDbName -i $sqlScriptPath" -ForegroundColor Yellow
    }

    # Generate DAB config
    Write-Step "Generating DAB configuration..."
    $dabConfigPath = Join-Path $env:TEMP "dab-config.json"
    New-DabConfigFile -SqlServer $sqlServer -SqlDatabase $sqlDbName -SqlUser $sqlAdminUser -SqlPassword $sqlAdminPasswordPlain -OutputPath $dabConfigPath

    # Create Container Apps Environment
    Write-Step "Creating Container Apps Environment..."
    az containerapp env create `
        --name $envName `
        --resource-group $ResourceGroup `
        --location $Location | Out-Null
    Write-Success "Container Apps Environment created"

    # Create connection string
    $connectionString = "Server=$sqlServer;Database=$sqlDbName;User ID=$sqlAdminUser;Password=$sqlAdminPasswordPlain;TrustServerCertificate=true;"

    # Deploy Container App
    Write-Step "Deploying Container App..."
    az containerapp create `
        --name $acaName `
        --resource-group $ResourceGroup `
        --environment $envName `
        --image "${acrLoginServer}/${acrImageName}:${acrImageTag}" `
        --target-port $ContainerPort `
        --ingress external `
        --transport auto `
        --min-replicas $MinReplicas `
        --max-replicas $MaxReplicas `
        --cpu 0.5 `
        --memory 1.0Gi `
        --registry-server $acrLoginServer `
        --registry-username $acrName `
        --registry-password $acrPassword `
        --secrets "db-connection-string=$connectionString" `
        --env-vars "ASPNETCORE_URLS=http://+:$ContainerPort" "DATABASE_CONNECTION_STRING=secretref:db-connection-string" | Out-Null
    
    Write-Success "Container App deployed"

    # Get app URL
    $appUrl = az containerapp show `
        --name $acaName `
        --resource-group $resourceGroup `
        --query "properties.configuration.ingress.fqdn" -o tsv

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
    Write-Host "  App URL:            https://$appUrl"
    Write-Host "  REST API:           https://$appUrl/api"
    Write-Host "  GraphQL:            https://$appUrl/graphql"
    Write-Host "  Health Check:       https://$appUrl/health"
    Write-Host ""
    Write-Host "Database Connection:" -ForegroundColor Cyan
    Write-Host "  Server:             $sqlServer"
    Write-Host "  Database:           $sqlDbName"
    Write-Host "  Admin User:         $sqlAdminUser"
    Write-Host "  Admin Password:     $sqlAdminPasswordPlain"
    Write-Host ""
    Write-Host "Try these commands:" -ForegroundColor Cyan
    Write-Host "  # List all publishers"
    Write-Host "  curl https://$appUrl/api/Publisher"
    Write-Host ""
    Write-Host "  # GraphQL query"
    $curlCmd = '  curl https://{0}/graphql -H "Content-Type: application/json" -d "{\"query\":\"{{publishers{{items{{id name}}}}}}\""' -f $appUrl
    Write-Host $curlCmd
    Write-Host ""
    Write-Host "Configuration file generated at: $dabConfigPath" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To delete all resources, run:" -ForegroundColor Yellow
    Write-Host "  az group delete --name $ResourceGroup --yes --no-wait"
    Write-Host ""

}
catch {
    Write-Error "Deployment failed: $_"
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