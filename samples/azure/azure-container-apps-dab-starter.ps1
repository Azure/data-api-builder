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
    [Required] Azure subscription ID where all resources will be deployed.
    You can find this with: az account show --query id -o tsv

.PARAMETER ResourceGroup
    [Required] Name of the resource group to create or use for all deployed resources.
    If the resource group exists, resources will be added to it. If not, it will be created.

.PARAMETER Location
    [Optional] Azure region where resources will be deployed. Default: eastus
    Supported regions: eastus, eastus2, westus, westus2, westus3, centralus, 
    northeurope, westeurope, uksouth, southeastasia

.PARAMETER ResourcePrefix
    [Optional] Prefix used to generate unique names for all Azure resources. Default: ACA
    Example: 'mydab' creates resources like mydab-acr-a1b2c3, mydab-sql-a1b2c3, mydab-aca-a1b2c3
    If not provided, uses 'ACA' as the prefix for Azure Container Apps deployment.

.PARAMETER SqlAdminPassword
    [Required] SQL Server administrator password as a SecureString.
    Must meet Azure SQL Server password requirements: 8-128 characters, mix of uppercase, 
    lowercase, numbers, and special characters.
    Create with: $password = ConvertTo-SecureString "YourPassword" -AsPlainText -Force

.PARAMETER SkipCleanup
    [Optional] Switch to keep resources even if deployment fails.
    By default, the script cleans up resources on errors. Use this flag to preserve 
    resources for debugging.

.PARAMETER ContainerPort
    [Optional] Port number where DAB will listen for HTTP requests. Default: 5000
    This is the internal container port. External access is via HTTPS on port 443.

.PARAMETER SqlServiceTier
    [Optional] Azure SQL Database service tier/SKU. Default: S0
    Options: Basic, S0, S1, S2, P1, P2
    - Basic: 5 DTUs, up to 2GB storage
    - S0: 10 DTUs, up to 250GB storage
    - S1: 20 DTUs, up to 250GB storage
    - S2: 50 DTUs, up to 250GB storage
    - P1: 125 DTUs, up to 500GB storage
    - P2: 250 DTUs, up to 500GB storage

.PARAMETER MinReplicas
    [Optional] Minimum number of container replicas to maintain. Default: 1, Range: 0-30
    Set to 0 to scale to zero when idle (saves costs but adds cold start latency).
    Container Apps will automatically scale between MinReplicas and MaxReplicas based on load.

.PARAMETER MaxReplicas
    [Optional] Maximum number of container replicas for auto-scaling. Default: 3, Range: 1-30
    Container Apps will scale up to this number based on CPU, memory, or HTTP request load.
    Must be greater than or equal to MinReplicas.

.PARAMETER ContainerCpu
    [Optional] Number of CPU cores allocated to each container replica. Default: 0.5, Range: 0.25-4
    Options: 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 3.5, 4.0
    Higher values provide better performance but increase costs.

.PARAMETER ContainerMemory
    [Optional] Memory in GB allocated to each container replica. Default: 1.0, Range: 0.5-8
    Must be paired appropriately with CPU (e.g., 0.5 CPU can use 0.5-4 GB memory).
    Minimum ratio: 0.5 GB per 0.25 CPU cores.

.PARAMETER DabConfigFile
    [Optional] Path to the DAB configuration JSON file. Default: src/Service.Tests/dab-config.MsSql.json
    The script will replace the connection string placeholder with actual SQL Server credentials.
    Use a custom config file if you need different entity mappings or security settings.

.EXAMPLE
    $password = ConvertTo-SecureString "YourPassword123!" -AsPlainText -Force
    .\azure-container-apps-dab-starter.ps1 -SubscriptionId "abc123" -ResourceGroup "rg-dab" -SqlAdminPassword $password
    Basic deployment using default 'ACA' prefix

.EXAMPLE
    .\azure-container-apps-dab-starter.ps1 -SubscriptionId "abc123" -ResourceGroup "rg-dab" -ResourcePrefix "mydab" -SqlAdminPassword $password -Location "westus2"
    Deploys to West US 2 with custom prefix

.EXAMPLE
    .\azure-container-apps-dab-starter.ps1 -SubscriptionId "abc123" -ResourceGroup "rg-dab" -SqlAdminPassword $password -ContainerCpu 1 -ContainerMemory 2 -MinReplicas 2 -MaxReplicas 10
    Deploys with custom CPU, memory, and scaling configuration

.EXAMPLE
    .\azure-container-apps-dab-starter.ps1 -SubscriptionId "abc123" -ResourceGroup "rg-dab" -SqlAdminPassword $password -SkipCleanup
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
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroup,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet('eastus', 'eastus2', 'westus', 'westus2', 'westus3', 'centralus', 'northeurope', 'westeurope', 'uksouth', 'southeastasia')]
    [string]$Location = "eastus",
    
    [Parameter(Mandatory=$false)]
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
    [ValidateRange(0, 30)]
    [int]$MinReplicas = 1,
    
    [Parameter(Mandatory=$false)]
    [ValidateRange(1, 30)]
    [int]$MaxReplicas = 3,
    
    [Parameter(Mandatory=$false)]
    [ValidateRange(0.25, 4)]
    [double]$ContainerCpu = 0.5,
    
    [Parameter(Mandatory=$false)]
    [ValidateRange(0.5, 8)]
    [double]$ContainerMemory = 1.0,
    
    [Parameter(Mandatory=$false)]
    [string]$DabConfigFile
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Auto-generate ResourcePrefix if not provided
if ([string]::IsNullOrEmpty($ResourcePrefix)) {
    $ResourcePrefix = "ACA"
    Write-Host "[INFO] Using default ResourcePrefix: $ResourcePrefix" -ForegroundColor Yellow
}

# Validate replica configuration
if ($MaxReplicas -lt $MinReplicas) {
    Write-Host "[ERROR] MaxReplicas ($MaxReplicas) must be greater than or equal to MinReplicas ($MinReplicas)" -ForegroundColor Red
    exit 1
}

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
    
    # Skip containerapp extension check due to known Azure CLI metadata bug
    # The commands work despite the PermissionError on dist-info files
    Write-Host "[INFO] Skipping containerapp extension check (Azure CLI metadata issue)" -ForegroundColor Yellow
    
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
    
    # Check if logged into Azure (workaround for containerapp extension errors)
    $oldErrorPref = $ErrorActionPreference
    $ErrorActionPreference = "SilentlyContinue"
    $accountJson = az account show --only-show-errors 2>$null | Out-String
    $ErrorActionPreference = $oldErrorPref
    
    if ($accountJson -and $accountJson -match '\{') {
        try {
            $account = $accountJson | ConvertFrom-Json
            Write-Success "Logged into Azure as $($account.user.name)"
        } catch {
            Write-Warning "Azure CLI working but cannot parse account info (continuing anyway)"
        }
    } else {
        Write-ErrorMessage "Not logged into Azure. Please run: az login"
        exit 1
    }
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
    
    # Ensure production mode for Container Apps
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
Write-Host "  Container App:    $acaName"
Write-Host "  SQL Server:       $sqlServerName"
Write-Host "  SQL Database:     $sqlDbName"
Write-Host "  SQL Admin User:   $sqlAdminUser"
Write-Host "  Container Port:   $ContainerPort"
Write-Host "  Min Replicas:     $MinReplicas"
Write-Host "  Max Replicas:     $MaxReplicas"
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
    
    # Create blob container
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
    $ErrorActionPreference = "Continue"
    $sasOutput = az storage blob generate-sas `
        --account-name $storageAccountName `
        --account-key $storageKey `
        --container-name "config" `
        --name "dab-config.json" `
        --permissions r `
        --expiry $expiryDate `
        --full-uri -o tsv 2>&1
    $ErrorActionPreference = "Stop"
    
    # Filter out Python warnings and get only the URL
    $configUrl = ($sasOutput | Where-Object { $_ -notmatch 'UserWarning' -and $_ -notmatch 'pkg_resources' -and $_ -notmatch 'Lib\\site-packages' -and $_.Length -gt 0 }) -join ''
    
    if (-not $configUrl -or $configUrl -notmatch '^https://') {
        Write-ErrorMessage "Failed to generate valid SAS URL"
        throw "SAS URL generation failed"
    }
    
    Write-Success "Generated SAS URL for config download"

    # Create Container Apps Environment
    Write-Step "Creating Container Apps Environment..."
    az containerapp env create `
        --name $envName `
        --resource-group $ResourceGroup `
        --location $Location | Out-Null
    Write-Success "Container Apps Environment created"

    # Deploy Container App with startup command to download config
    Write-Step "Deploying Container App..."
    Write-Host "Note: Container will download config from blob storage on startup" -ForegroundColor Yellow
    
    # Create complete YAML configuration file for container app
    # Using YAML ensures command, args, and env vars are properly formatted
    $containerYamlPath = Join-Path $env:TEMP "container-app.yaml"
    $containerYaml = @"
location: $Location
properties:
  managedEnvironmentId: /subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.App/managedEnvironments/$envName
  configuration:
    ingress:
      external: true
      targetPort: $ContainerPort
      transport: auto
    registries:
    - server: $acrLoginServer
      username: $acrName
      passwordSecretRef: acr-password
    secrets:
    - name: acr-password
      value: $acrPassword
  template:
    containers:
    - name: $acaName
      image: ${acrLoginServer}/${acrImageName}:${acrImageTag}
      command:
      - /bin/sh
      args:
      - -c
      - curl -o /App/dab-config.json "`$CONFIG_URL" && dotnet Azure.DataApiBuilder.Service.dll --ConfigFileName /App/dab-config.json
      env:
      - name: ASPNETCORE_URLS
        value: http://+:$ContainerPort
      - name: CONFIG_URL
        value: $configUrl
      resources:
        cpu: $ContainerCpu
        memory: ${ContainerMemory}Gi
    scale:
      minReplicas: $MinReplicas
      maxReplicas: $MaxReplicas
"@
    $containerYaml | Out-File -FilePath $containerYamlPath -Encoding UTF8
    
    # Create the container app with complete YAML configuration
    Write-Host "Creating container app with startup command..." -ForegroundColor Yellow
    az containerapp create `
        --name $acaName `
        --resource-group $ResourceGroup `
        --yaml $containerYamlPath | Out-Null
    
    Write-Success "Container App deployed"

    # Get app URL
    $appUrl = az containerapp show `
        --name $acaName `
        --resource-group $ResourceGroup `
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
    Write-Host "  Admin Password:     ********** (saved to file earlier)"
    Write-Host ""
    Write-Host "Container Resources:" -ForegroundColor Cyan
    Write-Host "  Min Replicas:       $MinReplicas"
    Write-Host "  Max Replicas:       $MaxReplicas"
    Write-Host "  CPU:                $ContainerCpu cores"
    Write-Host "  Memory:             $ContainerMemory GB"
    Write-Host ""
    Write-Host "Try these commands:" -ForegroundColor Cyan
    Write-Host "  # List all publishers"
    Write-Host "  curl https://$appUrl/api/Publisher"
    Write-Host ""
    Write-Host "  # GraphQL query"
    Write-Host "  curl https://$appUrl/graphql -H `"Content-Type: application/json`" -d `"{\`"query\`":\`"{publishers{items{id name}}}`\"}"
    Write-Host ""
    Write-Host "  # View container logs"
    Write-Host "  az containerapp logs show --name $acaName --resource-group $ResourceGroup --follow"
    Write-Host ""
    Write-Host "Storage Details:" -ForegroundColor Cyan
    Write-Host "  Storage Account:    $storageAccountName"
    Write-Host "  Blob Container:     config"
    Write-Host "  Config File:        dab-config.json"
    Write-Host ""
    Write-Host "Configuration file generated at: $dabConfigPath" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To delete all resources, run:" -ForegroundColor Yellow
    Write-Host "  az group delete --name $ResourceGroup --yes --no-wait"
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
