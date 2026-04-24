# Data API Builder on Azure Container Apps with Entra Auth, MCP, Key Vault, and Azure SQL OBO

This sample deploys Data API builder (DAB) to Azure Container Apps (ACA) for the customer scenario where a chat agent calls the DAB MCP endpoint, DAB validates Microsoft Entra ID tokens, and DAB connects to Azure SQL using user-delegated authentication.

The deployment intentionally keeps Azure resources in one resource group so you can show the full flow to a customer without hunting through the portal.

## What This Creates

The script creates:

- Resource group
- Azure Container Registry
- Azure Container Apps environment
- Azure Container App running DAB
- User-assigned managed identity for the Container App
- Azure Key Vault
- Azure SQL logical server and database
- Log Analytics workspace
- Microsoft Entra app registration for DAB JWT validation and OBO
- Sample `dbo.Todos` table

The Microsoft Entra app registration is not an Azure resource group resource because app registrations live at tenant scope. It is named with the same suffix as the resource group resources.

## Flow

1. A user or agent client gets a Microsoft Entra access token for the DAB API audience.
2. The client calls DAB REST, GraphQL, or MCP with `Authorization: Bearer <token>`.
3. DAB validates the JWT issuer and audience from `runtime.host.authentication`.
4. DAB resolves the request role to `authenticated`.
5. DAB uses OBO to exchange the incoming user token for an Azure SQL token.
6. DAB connects to Azure SQL as the delegated user and runs the generated SQL operation.
7. The DAB Container App uses its managed identity for startup metadata access and Key Vault reads.

## Why This Diff Exists

The repo already contains the DAB service, MCP runtime, and OBO code. The useful change here is a deployable ACA sample:

- `dab-config.template.json` mirrors the customer scenario with Key Vault, Entra JWT auth, MCP, and `user-delegated-auth`.
- `deploy.ps1` provisions the end-to-end Azure environment.
- `Dockerfile` builds DAB from this repo and copies the generated deployment config into `/App/dab-config.json`.
- `schema.sql` creates a tiny table so REST, GraphQL, and MCP have something real to expose.
- `.gitignore` excludes generated deployment outputs.

## Important Config Corrections

The customer-provided JSON is close, but two details matter for the current DAB schema:

- JWT authentication belongs under `runtime.host.authentication`, not directly under `runtime.authentication`.
- The runtime authentication provider value is `EntraID`. The data-source OBO provider remains `EntraId`.

The test command requests the Microsoft Entra v2 scope `api://<app-client-id>/access_as_user`. The resulting access token has the app client ID GUID as its `aud` claim, so this sample sets the DAB JWT audience to that GUID. The rule is simple: DAB's configured audience must match the token's `aud` claim.

## Deploy

From the repo root:

```powershell
.\samples\azure\container-apps-entra-mcp\deploy.ps1 `
  -ResourceGroup rg-dab-aca-mcp-auth-demo `
  -Location westus3
```

The script writes the real endpoints and IDs to:

```text
samples/azure/container-apps-entra-mcp/deployment.outputs.json
```

It also writes a generated config file used by the image build:

```text
samples/azure/container-apps-entra-mcp/dab-config.generated.json
```

That generated file is intentionally ignored by Git.

## Test REST

After deployment, open `deployment.outputs.json` and run the saved `tokenCommand`, or use this shape:

```powershell
$token = az account get-access-token `
  --tenant <tenant-id> `
  --scope "api://<app-client-id>/access_as_user" `
  --query accessToken -o tsv

Invoke-RestMethod `
  -Method Get `
  -Uri "https://<container-app-fqdn>/api/dbo_Todos?`$first=5" `
  -Headers @{ Authorization = "Bearer $token" }
```

Expected result: rows from `dbo.Todos`.

## Test GraphQL

```powershell
$body = @{
  query = "query { dbo_Todos(first: 5) { items { Id Title IsComplete CreatedAtUtc } } }"
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri "https://<container-app-fqdn>/graphql" `
  -ContentType "application/json" `
  -Headers @{ Authorization = "Bearer $token" } `
  -Body $body
```

## Test MCP

Use the MCP inspector:

```powershell
npx @modelcontextprotocol/inspector
```

In the inspector:

- Transport: `Streamable HTTP`
- URL: `https://<container-app-fqdn>/mcp`
- Header: `Authorization: Bearer <token>`

The autoentity configuration enables MCP DML tools for `dbo.Todos`.

## How Authentication Is Split

There are two separate authentication jobs:

- DAB API authentication validates the token sent by the agent or client. That is configured in `runtime.host.authentication.jwt`.
- SQL user-delegated authentication exchanges that same user token for an Azure SQL token. That is configured in `data-source.user-delegated-auth` and the `DAB_OBO_*` environment variables.

The Container App managed identity is a third identity. It is used for platform operations:

- Pull the image from ACR.
- Read Key Vault secrets.
- Let DAB read SQL metadata at startup before there is any user request.

## SQL Users

The script creates contained Azure SQL users for:

- The Container App user-assigned managed identity.
- The signed-in Azure CLI user running the deployment.

For the user-assigned managed identity, Azure SQL maps the login to the managed identity client/application ID in SQL GUID byte order. The script handles that when creating the startup metadata user with `WITH SID = ..., TYPE = E`.

That second user is what makes the local validation token work. In a customer tenant, create SQL users or group-based grants for the actual people or agents that will call DAB.

## OBO Consent

The DAB app registration needs delegated Azure SQL permission:

```powershell
az ad app permission add `
  --id <dab-app-client-id> `
  --api 022907d3-0f1b-48f7-badc-1ba6abab6d66 `
  --api-permissions c39ef2d1-04ce-46dc-8b5f-e9a5c60f0fc9=Scope

az ad app permission grant `
  --id <dab-app-client-id> `
  --api 022907d3-0f1b-48f7-badc-1ba6abab6d66 `
  --scope user_impersonation
```

Without this, DAB can validate the incoming JWT but OBO fails with `AADSTS65001`.

## Common Customer Failure Points

- The token audience does not match `runtime.host.authentication.jwt.audience`.
- The issuer is v1 but config expects v2, or the reverse.
- The auth block is under `runtime.authentication` instead of `runtime.host.authentication`.
- The app registration does not have delegated Azure SQL permission with admin consent.
- The incoming token is app-only instead of user-delegated. OBO needs a user assertion.
- The database does not contain a user or group matching the delegated user token.
- The Container App identity cannot read Key Vault or cannot connect for startup metadata.
- MCP clients forget to send the `Authorization` header to `/mcp`.

## Cleanup

Delete the Azure resources:

```powershell
az group delete --name rg-dab-aca-mcp-auth-demo --yes
```

Delete the app registration separately because it is tenant-scoped:

```powershell
az ad app list --display-name "dab-aca-mcp-demo-<suffix>" --query "[].appId" -o tsv
az ad app delete --id <app-client-id>
```

## Useful References

- DAB configuration schema: `schemas/dab.draft.schema.json`
- DAB MCP testing guide: `docs/testing-guide/mcp-inspector-testing.md`
- Azure SQL managed identity and Microsoft Entra users: https://learn.microsoft.com/azure/azure-sql/database/authentication-azure-ad-user-assigned-managed-identity
- `CREATE USER` for Microsoft Entra principals: https://learn.microsoft.com/sql/t-sql/statements/create-user-transact-sql
