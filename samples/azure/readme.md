# Azure Deployment Samples for Data API builder

This directory contains scripts and samples for deploying Data API builder (DAB) to various Azure services.

## Quick Start Scripts (PowerShell)

### 🚀 New: Automated Starter Scripts

We provide two comprehensive PowerShell starter scripts that create a complete DAB environment from scratch, including infrastructure, database, and test data:

#### **[azure-container-apps-dab-starter.ps1](./azure-container-apps-dab-starter.ps1)** ⭐ Recommended
Deploys DAB to **Azure Container Apps** with auto-scaling, HTTPS, and health monitoring.

```powershell
# Basic deployment (required parameters)
$password = ConvertTo-SecureString "<YourSecurePassword>" -AsPlainText -Force
.\azure-container-apps-dab-starter.ps1 -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-aca" `
    -ResourcePrefix "mydab" `
    -SqlAdminPassword $password

# Custom location and resources
$password = ConvertTo-SecureString "<YourSecurePassword>" -AsPlainText -Force
.\azure-container-apps-dab-starter.ps1 -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-aca" `
    -ResourcePrefix "mydab" `
    -SqlAdminPassword $password `
    -Location "westus2" `
    -ContainerCpu 1 `
    -ContainerMemory 2
```

**Features:**
- ✅ Auto-scaling (configurable 0-30 replicas)
- ✅ HTTPS enabled by default with automatic certificate management
- ✅ Built-in health probes and monitoring
- ✅ Managed Container Apps Environment with Log Analytics
- ✅ Zero-downtime deployments with revision management
- ✅ Configurable CPU (0.25-4 cores) and memory (0.5-8 GB)
- ✅ Container startup command downloads config from Azure Blob Storage (SAS URL)
- ✅ Better for production workloads

#### **[azure-container-instances-dab-starter.ps1](./azure-container-instances-dab-starter.ps1)**
Deploys DAB to **Azure Container Instances** for simpler, single-instance deployments.

```powershell
# Basic deployment (required parameters)
$password = ConvertTo-SecureString "<YourSecurePassword>" -AsPlainText -Force
.\azure-container-instances-dab-starter.ps1 -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-aci" `
    -ResourcePrefix "mydab" `
    -SqlAdminPassword $password

# With custom CPU and memory
$password = ConvertTo-SecureString "<YourSecurePassword>" -AsPlainText -Force
.\azure-container-instances-dab-starter.ps1 -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-aci" `
    -ResourcePrefix "mydab" `
    -SqlAdminPassword $password `
    -ContainerCpu 2 `
    -ContainerMemory 3
```

**Features:**
- ✅ Simpler architecture (single container)
- ✅ Faster startup (~30 seconds)
- ✅ Lower cost for testing and development
- ✅ Configurable CPU (1-4 cores) and memory (0.5-16 GB)
- ✅ ARM template-based deployment
- ✅ Good for development, testing, and simple workloads

### What These Scripts Do

Both starter scripts automatically:

1. **Validate Prerequisites** - Check for Azure CLI, Docker, sqlcmd, and verify Docker is running
2. **Verify Subscription** - Set or confirm the Azure subscription to use
3. **Create Azure Resources**:
   - Resource Group
   - Azure Container Registry (ACR) with admin user enabled
   - MS SQL Server & Database (with configurable service tier)
   - **Container Apps**: Storage Account (for config file), Container Apps Environment with Log Analytics, Container App
   - **Container Instances**: Container Instance deployed via ARM template
4. **Build & Deploy** - Build DAB Docker image from repository root and push to ACR
5. **Configure Security** - Set up SQL firewall rules (Azure services + your public IP)
6. **Load Test Data** - Import sample database schema and data from `src/Service.Tests/DatabaseSchema-MsSql.sql` using sqlcmd
7. **Generate Config** - Create a working DAB configuration file with connection string and upload to blob storage (Container Apps) or embed in deployment (Container Instances)
8. **Deploy Container** - Start container with proper startup command and environment variables
9. **Verify Deployment** - Wait for resources to be ready and display status
10. **Provide Endpoints** - Display all connection details, URLs, and example curl commands

### Prerequisites

Before running these scripts, ensure you have:

- **Azure CLI** - [Install](https://aka.ms/InstallAzureCLIDirect)
- **Docker Desktop** - [Install](https://www.docker.com/products/docker-desktop) (must be running)
- **SQL Server Command Line Tools (sqlcmd)** - [Install](https://aka.ms/sqlcmd) or run: `winget install Microsoft.SqlServer.SqlCmd`
- **PowerShell 5.1 or higher** (included with Windows)
- **Azure Subscription** with sufficient permissions

### Quick Setup

```powershell
# 1. Login to Azure
az login

# 2. Get your subscription ID
az account show --query id -o tsv

# 3. Clone the repository
git clone https://github.com/Azure/data-api-builder.git
cd data-api-builder/samples/azure

# 4. Run the script with required parameters
$password = ConvertTo-SecureString "<YourSecurePassword>" -AsPlainText -Force
.\azure-container-apps-dab-starter.ps1 `
    -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-demo" `
    -SqlAdminPassword $password

# Or with custom prefix:
.\azure-container-apps-dab-starter.ps1 `
    -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-demo" `
    -ResourcePrefix "mydab" `
    -SqlAdminPassword $password

# The script will:
# - Validate prerequisites (Azure CLI, Docker, sqlcmd)
# - Show a deployment summary
# - Ask for confirmation before proceeding
# - Create all Azure resources
# - Build and deploy DAB container
# - Load test data from DatabaseSchema-MsSql.sql
# - Display connection details and example commands
```

### Script Parameters

#### Required Parameters (Both Scripts)
```powershell
-SubscriptionId         # Azure subscription ID
-ResourceGroup          # Resource group name
-SqlAdminPassword       # SQL Server admin password (SecureString)
```

#### Optional Parameters (Both Scripts)
```powershell
-ResourcePrefix         # Prefix for all resource names (default: 'ACA' for Container Apps, 'ACI' for Container Instances)
-Location               # Azure region (default: eastus)
                        # Options: eastus, eastus2, westus, westus2, westus3, 
                        #          centralus, northeurope, westeurope, uksouth, southeastasia
-ContainerPort          # DAB container port (default: 5000)
-SqlServiceTier         # SQL DB tier: Basic, S0, S1, S2, P1, P2 (default: S0)
-DabConfigFile          # Path to DAB config file (default: src/Service.Tests/dab-config.MsSql.json)
-SkipCleanup            # Keep resources even if deployment fails
```

#### Container Apps Specific Parameters
```powershell
-MinReplicas            # Minimum replicas: 0-30 (default: 1)
-MaxReplicas            # Maximum replicas: 1-30 (default: 3)
-ContainerCpu           # CPU cores: 0.25-4 (default: 0.5)
-ContainerMemory        # Memory in GB: 0.5-8 (default: 1.0)
```

#### Container Instances Specific Parameters
```powershell
-ContainerCpu           # CPU cores: 1-4 (default: 1)
-ContainerMemory        # Memory in GB: 0.5-16 (default: 1.5)
```

### Examples

```powershell
# Prepare password for all examples
$password = ConvertTo-SecureString "<YourSecurePassword>" -AsPlainText -Force

# Basic Container Apps deployment (uses default 'ACA' prefix)
.\azure-container-apps-dab-starter.ps1 `
    -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-aca" `
    -SqlAdminPassword $password

# Container Apps deployment with custom prefix
.\azure-container-apps-dab-starter.ps1 `
    -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-aca" `
    -ResourcePrefix "mydab" `
    -SqlAdminPassword $password

# Custom location and region
.\azure-container-apps-dab-starter.ps1 `
    -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-westus" `
    -ResourcePrefix "mydab" `
    -SqlAdminPassword $password `
    -Location "westus2"

# Production configuration with scaling and higher tier SQL
.\azure-container-apps-dab-starter.ps1 `
    -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-prod" `
    -ResourcePrefix "proddb" `
    -SqlAdminPassword $password `
    -SqlServiceTier "S2" `
    -MinReplicas 2 `
    -MaxReplicas 10 `
    -ContainerCpu 1 `
    -ContainerMemory 2

# Basic Container Instance deployment (uses default 'ACI' prefix)
.\azure-container-instances-dab-starter.ps1 `
    -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-aci" `
    -SqlAdminPassword $password

# High-performance Container Instance with custom prefix
.\azure-container-instances-dab-starter.ps1 `
    -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-aci" `
    -ResourcePrefix "mydab" `
    -SqlAdminPassword $password `
    -ContainerCpu 4 `
    -ContainerMemory 8

# Keep resources on failure for debugging
.\azure-container-apps-dab-starter.ps1 `
    -SubscriptionId "<your-subscription-id>" `
    -ResourceGroup "rg-dab-debug" `
    -ResourcePrefix "debug" `
    -SqlAdminPassword $password `
    -SkipCleanup
```

### After Deployment

Once deployed, you'll receive a summary with:

- **App URLs**: REST API, GraphQL, and health check endpoints
- **Database credentials**: Server, database, username, password
- **Sample commands**: Ready-to-use curl commands to test your API
- **Cleanup command**: How to delete all resources

Example output:
```
==========================================================================
  Deployment Summary
==========================================================================

DAB Endpoints:
  App URL:            https://mydab-aca-abc123.westus2.azurecontainerapps.io
  Health Check:       https://mydab-aca-abc123.westus2.azurecontainerapps.io/
  REST API:           https://mydab-aca-abc123.westus2.azurecontainerapps.io/api
  GraphQL:            https://mydab-aca-abc123.westus2.azurecontainerapps.io/graphql

Database Connection:
  Server:             mydab-sql-xyz789.database.windows.net
  Database:           dabdb
  Username:           sqladmin
  Password:           <stored in secure file>

Container Configuration:
  Image:              mydabacr123.azurecr.io/dab:latest
  CPU:                1 cores
  Memory:             2 GB
  Replicas:           1-3 (min-max)

Try these commands:
  # Test health endpoint
  curl https://mydab-aca-abc123.westus2.azurecontainerapps.io/

  # List all publishers
  curl https://mydab-aca-abc123.westus2.azurecontainerapps.io/api/Publisher

  # Get a specific book
  curl https://mydab-aca-abc123.westus2.azurecontainerapps.io/api/Book/id/1

  # GraphQL query
  curl https://mydab-aca-abc123.westus2.azurecontainerapps.io/graphql \
    -H 'Content-Type: application/json' \
    -d '{"query":"{books{items{id title year}}}"}'  

Cleanup:
  az group delete --name rg-dab-aca --yes --no-wait
```

### Cleanup

To delete all resources created by the scripts:

```powershell
# Use the cleanup command provided in the deployment output
az group delete --name <your-resource-group> --yes --no-wait
```

### Troubleshooting

**Docker not running:**
```
✗ Docker is not running. Please start Docker Desktop.
```
→ Start Docker Desktop and wait for it to be fully running.

**sqlcmd not found:**
```
✗ sqlcmd is not installed.
```
→ Install: `winget install Microsoft.SqlServer.SqlCmd` or download from [aka.ms/sqlcmd](https://aka.ms/sqlcmd)

**Azure login required:**
```
✗ Not logged into Azure. Please run: az login
```
→ Run `az login` and follow the authentication prompts.

**Test data loading failed:**
The script will continue even if test data loading fails. You can manually load it:
```powershell
sqlcmd -S <server>.database.windows.net -U sqladmin -P <password> -d dabdb -i src\Service.Tests\DatabaseSchema-MsSql.sql
```

## Manual Deployment Scripts (Bash)

For manual deployments with existing configurations:

### [azure-deploy.sh](./azure-deploy.sh)
Deploy Data API builder to Azure Container Instance as described in [Running Data API builder in Azure](https://learn.microsoft.com/azure/data-api-builder/running-in-azure)

### [azure-container-apps-deploy.sh](./azure-container-apps-deploy.sh)
Deploy Data API builder to Azure Container Apps as described in [Running Data API builder in Azure](https://learn.microsoft.com/azure/data-api-builder/running-in-azure)

**Note:** These scripts require a valid `dab-config.json` file in the same directory.

## Comparison: Container Apps vs Container Instances

| Feature | Container Apps | Container Instances |
|---------|---------------|---------------------|
| **Auto-scaling** | ✅ Yes (0-30 replicas) | ❌ No (single instance) |
| **HTTPS** | ✅ Automatic | ⚠️ Manual setup required |
| **Load Balancing** | ✅ Built-in | ❌ Single instance |
| **Health Probes** | ✅ Yes | ⚠️ Limited |
| **Managed Identity** | ✅ Yes | ⚠️ Limited |
| **Zero-downtime Deployments** | ✅ Yes | ❌ No |
| **Cost (idle)** | 💰 Can scale to 0 | 💰 Always running |
| **Best For** | Production workloads | Dev/test, simple workloads |
| **Startup Time** | ~1-2 min | ~30 sec |

**Recommendation:** Use **Container Apps** for production workloads and **Container Instances** for development/testing.

## Additional Resources

- [Data API builder Documentation](https://learn.microsoft.com/azure/data-api-builder/)
- [Running DAB in Azure](https://learn.microsoft.com/azure/data-api-builder/running-in-azure)
- [Azure Container Apps Documentation](https://learn.microsoft.com/azure/container-apps/)
- [Azure Container Instances Documentation](https://learn.microsoft.com/azure/container-instances/)

## Contributing

If you encounter issues or have suggestions for improving these scripts, please [open an issue](https://github.com/Azure/data-api-builder/issues) or submit a pull request.
