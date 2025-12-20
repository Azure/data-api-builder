# Deploying SQL MCP Server implemented in Data API builder and Integrating with Azure AI Foundry

This document provides an end‑to‑end guide to stand up a **SQL MCP Server** with **Model Context Protocol (MCP)** tools implemented in **Data API builder (DAB)** container that also exposes **REST** and **GraphQL** endpoints, and to integrate those MCP tools with an **Azure AI Foundry Agent**. 

## 1. Architecture Overview

**Components**
- **Azure SQL Database** hosting domain tables and stored procedures.
- **DAB container** (Azure Container Instances in this guide) that:
  - reads `dab-config.json` from an **Azure Files** share at startup,
  - exposes **REST**, **GraphQL**, and **MCP** endpoints.
- **Azure Storage (Files)** to store and version `dab-config.json`.
- **Azure AI Foundry Agent** configured with an **MCP tool** pointing to the SQL MCP Server endpoint.

**Flow**
1. DAB starts in ACI → reads `dab-config.json` from the mounted Azure Files share.  
2. DAB exposes `/api` (REST), `/graphql` (GraphQL), and `/mcp` (MCP).  
3. Azure AI Foundry Agent invokes MCP tools to read/update data via DAB’s surface (tables, views and stored procedures).


## 2. Prerequisites
- Azure Subscription with permissions for Resource Groups, Storage, ACI, and Azure SQL.
- Azure SQL Database provisioned and reachable from ACI.
- Azure CLI (`az`) and .NET SDK installed locally.
- DAB CLI version **1.7.81 or later**.
- Outbound network access from ACI to your Azure SQL server.


## 3. Prepare the Database

You need to create the necessary tables and stored procedures in your Azure SQL Database. Below is an example of how to create a simple `Products` table and a stored procedure to retrieve products by category.

**Example:**

1. Connect to your Azure SQL Database using Azure Data Studio, SQL Server Management Studio, or the Azure Portal's Query Editor.

2. Run the following SQL script to create a sample table and stored procedure:

```sql
-- Create Products table
CREATE TABLE Products (
    ProductID INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Category NVARCHAR(50) NOT NULL,
    Price DECIMAL(10,2) NOT NULL
);

-- Create stored procedure to get products by category
CREATE PROCEDURE GetProductsByCategory
    @Category NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ProductID, Name, Category, Price
    FROM Products
    WHERE Category = @Category;
END;
## 4. Install DAB CLI and Bootstrap Configuration
```
dotnet tool install --global Microsoft.DataApiBuilder --version 1.7.81
export DATABASE_CONNECTION_STRING="Server=<server>.database.windows.net;Database=<db>;User ID=<user>;Password=<pwd>;Encrypt=True;"

dab init \
  --database-type "mssql" \
  --connection-string "@env('DATABASE_CONNECTION_STRING')" \
  --host-mode "Development" \
  --rest.enabled true \
  --graphql.enabled true \
  --mcp.enabled true \
  --mcp.path "/mcp" 
 
```
  
## 5. Add entities and stored procedure to `dab-config.json` and enable MCP tools in the config.

## 6. Store dab-config.json in Azure Files
- Create a Storage Account and File Share.
- Upload dab-config.json.
- Record account name and key for mounting in ACI.

## 7. Deploy DAB to Azure Container Instances

```
az container create \
  --resource-group <RG> \
  --name dab-mcp-demo \
  --image mcr.microsoft.com/azure-databases/data-api-builder:1.7.81-rc \
  --dns-name-label <globally-unique-label> \
  --ports 5000 \
  --location <location> \
  --environment-variables DAB_CONFIG_PATH="/aci/dab-config.json" \
  --azure-file-volume-share-name <FileShareName> \
  --azure-file-volume-account-name <StorageAccountName> \
  --azure-file-volume-account-key <StorageAccountKey> \
  --azure-file-volume-mount-path "/aci"
  --os-type Linux \
  --cpu 1 \
  --memory 1.5 \
  --command-line "dotnet Azure.DataApiBuilder.Service.dll --ConfigFileName $configFile --LogLevel Debug"
```
REST: http://<fqdn>/api/<EntityName>
GraphQL: http://<fqdn>/graphql
MCP: http://<fqdn>/mcp

## 8. Integrate with Azure AI Foundry
- Create or open a Project.
- Add an Agent.
- Add MCP tool with URL: http://<fqdn>/mcp.
- Test in Playground