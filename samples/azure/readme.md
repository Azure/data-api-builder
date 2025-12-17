# Azure Deployment Samples for Data API Builder

This directory contains scripts and samples for deploying Data API builder (DAB) to various Azure services.

## Quick Start Scripts (PowerShell)

### 🚀 New: Automated Starter Scripts

We provide two comprehensive PowerShell starter scripts that create a complete DAB environment from scratch, including infrastructure, database, and test data:

#### **[azure-container-apps-dab-starter.ps1](./azure-container-apps-dab-starter.ps1)** ⭐ Recommended
Deploys DAB to **Azure Container Apps** with auto-scaling, HTTPS, and health monitoring.

```powershell
# Quick start with auto-generated names
.\azure-container-apps-dab-starter.ps1

# Custom deployment
.\azure-container-apps-dab-starter.ps1 -ResourcePrefix "mydab" -Location "westus2"
```

**Features:**
- ✅ Auto-scaling (configurable min/max replicas)
- ✅ HTTPS enabled by default
- ✅ Built-in health probes
- ✅ Managed environment
- ✅ Better for production workloads

#### **[azure-container-instances-dab-starter.ps1](./azure-container-instances-dab-starter.ps1)**
Deploys DAB to **Azure Container Instances** for simpler, single-instance deployments.

```powershell
# Quick start
.\azure-container-instances-dab-starter.ps1

# With custom resources
.\azure-container-instances-dab-starter.ps1 -ContainerCpu 2 -ContainerMemory 3
```

**Features:**
- ✅ Simpler architecture
- ✅ Faster startup
- ✅ Lower cost for testing
- ✅ Good for development/testing

### What These Scripts Do

Both starter scripts automatically:

1. **Validate Prerequisites** - Check for Azure CLI, Docker, sqlcmd
2. **Create Azure Resources**:
   - Resource Group
   - Azure Container Registry (ACR)
   - MS SQL Server & Database
   - Container deployment (Apps or Instances)
3. **Build & Deploy** - Build DAB Docker image and push to ACR
4. **Configure Security** - Set up SQL firewall rules (Azure services + your IP)
5. **Load Test Data** - Import sample database schema and data from `src/Service.Tests/DatabaseSchema-MsSql.sql`
6. **Generate Config** - Create a working DAB configuration file
7. **Provide Endpoints** - Display all connection details and example commands

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

# 2. Clone the repository (if not already done)
git clone https://github.com/Azure/data-api-builder.git
cd data-api-builder/samples/azure

# 3. Run the script
.\azure-container-apps-dab-starter.ps1

# The script will:
# - Auto-generate resource names
# - Show a deployment summary
# - Ask for confirmation before proceeding
# - Display connection details after deployment
```

### Script Parameters

Both scripts support extensive customization:

```powershell
# Common Parameters
-SubscriptionId         # Azure subscription (uses current if not specified)
-ResourceGroup          # Resource group name (auto-generated if not provided)
-Location               # Azure region (default: eastus)
-ResourcePrefix         # Prefix for all resource names (auto-generated if not provided)
-SqlAdminPassword       # SQL admin password (auto-generated if not provided)
-ContainerPort          # DAB container port (default: 5000)
-SqlServiceTier         # SQL DB tier: Basic, S0, S1, S2, P1, P2 (default: S0)
-SkipCleanup            # Keep resources even if deployment fails

# Container Apps Specific
-MinReplicas            # Minimum replicas (default: 1)
-MaxReplicas            # Maximum replicas (default: 3)

# Container Instances Specific
-ContainerCpu           # CPU cores: 1-4 (default: 1)
-ContainerMemory        # Memory in GB: 0.5-16 (default: 1.5)
```

### Examples

```powershell
# Minimal - uses all defaults with auto-generation
.\azure-container-apps-dab-starter.ps1

# Custom prefix and location
.\azure-container-apps-dab-starter.ps1 -ResourcePrefix "mydab" -Location "westus2"

# Specific subscription and resource group
.\azure-container-apps-dab-starter.ps1 -SubscriptionId "xxx-xxx" -ResourceGroup "my-rg"

# Production configuration with scaling
.\azure-container-apps-dab-starter.ps1 -SqlServiceTier "S2" -MinReplicas 2 -MaxReplicas 10

# High-performance Container Instance
.\azure-container-instances-dab-starter.ps1 -ContainerCpu 4 -ContainerMemory 8

# Keep resources on failure for debugging
.\azure-container-apps-dab-starter.ps1 -SkipCleanup
```

### After Deployment

Once deployed, you'll receive a summary with:

- **App URLs**: REST API, GraphQL, and health check endpoints
- **Database credentials**: Server, database, username, password
- **Sample commands**: Ready-to-use curl commands to test your API
- **Cleanup command**: How to delete all resources

Example output:
```
DAB Endpoints:
  App URL:            https://dab-aca-abc123.eastus.azurecontainerapps.io
  REST API:           https://dab-aca-abc123.eastus.azurecontainerapps.io/api
  GraphQL:            https://dab-aca-abc123.eastus.azurecontainerapps.io/graphql
  Health Check:       https://dab-aca-abc123.eastus.azurecontainerapps.io/health

Try these commands:
  # List all publishers
  curl https://dab-aca-abc123.eastus.azurecontainerapps.io/api/Publisher

  # GraphQL query
  curl https://dab-aca-abc123.eastus.azurecontainerapps.io/graphql \
    -H 'Content-Type: application/json' \
    -d '{"query":"{publishers{items{id name}}}"}'
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
Deploy Data API builder to Azure Container Instance as described in [Running Data API Builder in Azure](https://learn.microsoft.com/azure/data-api-builder/running-in-azure)

### [azure-container-apps-deploy.sh](./azure-container-apps-deploy.sh)
Deploy Data API builder to Azure Container Apps as described in [Running Data API Builder in Azure](https://learn.microsoft.com/azure/data-api-builder/running-in-azure)

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

- [Data API Builder Documentation](https://learn.microsoft.com/azure/data-api-builder/)
- [Running DAB in Azure](https://learn.microsoft.com/azure/data-api-builder/running-in-azure)
- [Azure Container Apps Documentation](https://learn.microsoft.com/azure/container-apps/)
- [Azure Container Instances Documentation](https://learn.microsoft.com/azure/container-instances/)

## Contributing

If you encounter issues or have suggestions for improving these scripts, please [open an issue](https://github.com/Azure/data-api-builder/issues) or submit a pull request.