# Running DAB Locally with MCP Support

This guide demonstrates how to run Data API Builder (DAB) locally with MCP (Model Context Protocol) support to enable database operations through AI agents.

> **Note**: This guide assumes you are running commands from the `samples/aspire` directory unless otherwise specified.

## Prerequisites

- .NET 8.0 SDK or later
- Docker (for SQL Server)
- A terminal/command prompt

## Quick Start

### 1. Start SQL Server in Docker

```bash
docker run -d --name sqlserver \
  -e 'ACCEPT_EULA=Y' \
  -e 'MSSQL_SA_PASSWORD=YourStrong!Passw0rd' \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
```

### 2. Initialize the Database

```bash
# Wait for SQL Server to start
sleep 20

# Run the database init script from this directory
docker exec -i sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'YourStrong!Passw0rd' -C \
  < database.sql
```

Alternatively, use the Aspire AppHost init script from the repository root:
```bash
docker exec -i sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'YourStrong!Passw0rd' -C \
  < ../../src/Aspire.AppHost/init-scripts/sql/create-database.sql
```

This creates a "Trek" database with Star Trek themed tables:
- Series
- Actor
- Character  
- Species
- Series_Character
- Character_Species

### 3. Build DAB

```bash
# From repository root
dotnet build ../../src/Service/Azure.DataApiBuilder.Service.csproj -c Release
```

### 4. Use the Existing Configuration File

This directory includes a pre-configured `dab-config.json` file with MCP support already enabled. You can use it as-is or customize it for your needs.

The key configuration for MCP is:
```json
"mcp": {
  "enabled": true,
  "path": "/mcp"
}
```

If you need to modify the configuration, see the existing `dab-config.json` file in this directory for a complete example.

### 5. Run DAB

```bash
# From repository root
cd ../../src/out/engine/net8.0
ASPNETCORE_URLS="http://localhost:5002" \
  dotnet Azure.DataApiBuilder.Service.dll \
  --ConfigFileName=../../../samples/aspire/dab-config.json \
  --verbose
```

Or if running from the samples/aspire directory:
```bash
dotnet run --project ../../src/Cli/Cli.csproj -c Release -f net8.0 -- \
  start --config ./dab-config.json --no-https-redirect --verbose
```

## Testing MCP Integration

### 1. Check Health

```bash
curl http://localhost:5002/health
```

Expected response:
```json
{
  "status": "Healthy",
  "version": "1.7.0",
  "configuration": {
    "rest": true,
    "graphql": true,
    "mcp": true,
    ...
  }
}
```

### 2. List Available MCP Tools

```bash
curl -X POST http://localhost:5002/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/list",
    "id": 1
  }'
```

Returns tools like:
- `create_record` - Create new database records
- `read_records` - Query database records  
- `update_record` - Update existing records
- `delete_record` - Delete records
- `execute_entity` - Execute stored procedures
- `describe_entities` - Get database schema information

### 3. Describe Entities

```bash
curl -X POST http://localhost:5002/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/call",
    "params": {
      "name": "describe_entities",
      "arguments": {}
    },
    "id": 2
  }'
```

### 4. Read Records

```bash
curl -X POST http://localhost:5002/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/call",
    "params": {
      "name": "read_records",
      "arguments": {
        "entity": "Series"
      }
    },
    "id": 3
  }'
```

Expected response:
```json
{
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{
          \"entity\": \"Series\",
          \"result\": {
            \"value\": [
              {\"Id\": 1, \"Name\": \"Star Trek\"},
              {\"Id\": 2, \"Name\": \"Star Trek: The Next Generation\"},
              ...
            ]
          }
        }"
      }
    ]
  }
}
```

### 5. Create a New Record

```bash
curl -X POST http://localhost:5002/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/call",
    "params": {
      "name": "create_record",
      "arguments": {
        "entity": "Series",
        "data": {
          "Id": 6,
          "Name": "Star Trek: Discovery"
        }
      }
    },
    "id": 4
  }'
```

## Accessing Other Endpoints

### REST API

- Browse API: `http://localhost:5002/api/swagger`
- Get all series: `http://localhost:5002/api/Series`
- Get series by ID: `http://localhost:5002/api/Series/id/1`

### GraphQL

- GraphQL Playground: `http://localhost:5002/graphql`
- Example query:
  ```graphql
  query {
    series {
      items {
        Id
        Name
      }
    }
  }
  ```

## Using with Aspire (Advanced)

For running with Aspire orchestration and dashboard:

1. Navigate to `samples/aspire/`
2. Follow the README in that directory
3. Run with `aspire run` or through the AppHost project

This provides:
- Aspire Dashboard with telemetry
- OpenTelemetry integration
- Container orchestration
- MCP Inspector for debugging

## Troubleshooting

### Port Already in Use

If port 5002 is in use, change `ASPNETCORE_URLS` to a different port:
```bash
ASPNETCORE_URLS="http://localhost:5003" dotnet Azure.DataApiBuilder.Service.dll ...
```

### SQL Server Connection Issues

Verify SQL Server is running:
```bash
docker ps | grep sqlserver
```

Test connection:
```bash
docker exec -it sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'YourStrong!Passw0rd' -C \
  -Q "SELECT 1 AS Test"
```

### DAB Not Starting

Check the logs for detailed error messages. Common issues:
- Invalid config file path
- Database connection string incorrect
- Port conflicts
- Missing .NET SDK

## Next Steps

- Explore the MCP tools with AI agents (Claude, GPT, etc.)
- Add more entities to the configuration
- Enable OpenTelemetry for telemetry and logging
- Deploy to Azure or other cloud platforms
- Integrate with your own databases

## References

- [DAB Documentation](https://learn.microsoft.com/azure/data-api-builder/)
- [MCP Specification](https://modelcontextprotocol.io/)
- [Aspire Documentation](https://learn.microsoft.com/dotnet/aspire/)
