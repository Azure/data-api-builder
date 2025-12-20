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
```

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
  
## 5. Add all required entities (tables and stored procedures) to `dab-config.json` and enable MCP tools in the config

Here is how to add a table entity and a stored procedure to your `dab-config.json`, and ensure MCP tools are enabled:

1. **Open your `dab-config.json` file.**

2. **Add an entity (table) definition** under the `"entities"` section. For example, to expose a `Customers` table:
   ```
   "entities": {
     "Customers": {
       "source": "Customers",
       "rest": true,
       "graphql": true,
       "mcp": true,
       "permissions": [
         {
           "role": "anonymous",
           "actions": [ "read", "create", "update", "delete" ]
         }
       ]
     }
   }
   ```

3. **Add a stored procedure** under the "entities" section. For example, to expose a stored procedure called GetCustomerOrders:

  ```
  "GetCustomerOrders": {
    "source": {
      "object": "GetCustomerOrders",
      "type": "stored-procedure"
    },
    "rest": true,
    "graphql": true,
    "mcp": true,
    "permissions": [
      {
        "role": "anonymous",
        "actions": [ "execute" ]
      }
    ]
  }
  ```

Note: Make sure the "entities" section is a valid JSON object. If you have multiple entities, separate them with commas.

4. **Ensure MCP is enabled in the "runtime" section:**

```
"runtime": {
  "rest": { "enabled": true },
  "graphql": { "enabled": true },
  "mcp": {
    "enabled": true,
    "path": "/mcp"
  }
}
```

5. **Example dab-config.json structure:**

```
{
  "data-source": {
    "database-type": "mssql",
    "connection-string": "@env('DATABASE_CONNECTION_STRING')"
  },
  "entities": {
    "Customers": {
      "source": "Customers",
      "rest": true,
      "graphql": true,
      "mcp": true,
      "permissions": [
        {
          "role": "anonymous",
          "actions": [ "read", "create", "update", "delete" ]
        }
      ]
    },
    "GetCustomerOrders": {
      "source": {
        "object": "GetCustomerOrders",
        "type": "stored-procedure"
      },
      "rest": true,
      "graphql": true,
      "mcp": true,
      "permissions": [
        {
          "role": "anonymous",
          "actions": [ "execute" ]
        }
      ]
    }
  },
  "runtime": {
    "rest": { "enabled": true },
    "graphql": { "enabled": true },
    "mcp": {
      "enabled": true,
      "path": "/mcp"
    }
  }
}
```

6. **Save the file.**

## 6. Store dab-config.json in Azure Files

1. **Create a Storage Account** (if you don't have one):
az storage account create
--name
--resource-group
--location
--sku Standard_LRS


2. **Create a File Share**:
az storage share create
--name
--account-name


3. **Upload `dab-config.json` to the File Share**:
az storage file upload
--account-name
--share-name
--source ./dab-config.json
--path dab-config.json


4. **Retrieve the Storage Account key** (needed for mounting in ACI):
az storage account keys list
--account-name
--resource-group

Use the value of `key1` or `key2` as `<StorageAccountKey>` in the next step.


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
  --azure-file-volume-mount-path "/aci" \
  --os-type Linux \
  --cpu 1 \
  --memory 1.5 \
  --command-line "dotnet Azure.DataApiBuilder.Service.dll --ConfigFileName $DAB_CONFIG_PATH --LogLevel Debug"
```

## 8. Integrate with Azure AI Foundry

Follow these steps to connect your DAB MCP endpoint to Azure AI Foundry and test the integration:

1. **Create or Open a Project**
   - Navigate to the [Azure AI Foundry portal](https://ai.azure.com/foundry) and sign in.
   - On the dashboard, click **Projects** in the left navigation pane.
   - To create a new project, click **New Project**, enter a name (e.g., `DAB-MCP-Demo`), and click **Create**.
   - To use an existing project, select it from the list.

2. **Add an Agent**
   - Within your project, go to the **Agents** tab.
   - Click **Add Agent**.
   - Enter an agent name (e.g., `DAB-MCP-Agent`).
   - (Optional) Add a description.
   - Click **Create**.

3. **Configure the MCP Tool**
   - In the agent's configuration page, go to the **Tools** section.
   - Click **Add Tool** and select **MCP** from the tool type dropdown.
   - In the **MCP Endpoint URL** field, enter your DAB MCP endpoint, e.g., `http://<fqdn>/mcp`.
   - (Optional) Configure authentication if your endpoint requires it.
   - Click **Save** to add the tool.

4. **Test in Playground**
   - Go to the **Playground** tab in your project.
   - Select the agent you created from the agent dropdown.
   - In the input box, enter a prompt that will trigger the MCP tool, such as:
     ```
     Get all records from the Customers entity.
     ```
   - Click **Run**.
   - The agent should invoke the MCP tool, which will call your DAB MCP endpoint and return the results.
   - **Expected Result:** You should see the data returned from your DAB instance displayed in the Playground output panel.
   - If there are errors, check the DAB container logs and ensure the MCP endpoint is reachable from Azure AI Foundry.