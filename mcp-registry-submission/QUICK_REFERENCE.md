# Data API Builder MCP Server - Quick Reference

## 🚀 Quick Start

### Installation
```bash
dotnet tool install --global Microsoft.DataApiBuilder
```

### Configuration
```bash
# 1. Initialize DAB config
dab init --database-type mssql --connection-string "@env('DB_CONNECTION')" --host-mode development

# 2. Add entities
dab add Product --source "dbo.Products" --permissions "anonymous:*"

# 3. Start the server
dab start
```

### MCP Client Setup (Claude Desktop)
```json
{
  "mcpServers": {
    "data-api-builder": {
      "command": "dab",
      "args": ["start"],
      "env": {
        "DATABASE_CONNECTION_STRING": "your-connection-string",
        "X_MS_API_ROLE": "authenticated"
      }
    }
  }
}
```

## 🛠️ Available MCP Tools

| Tool | Purpose | Permission Required |
|------|---------|-------------------|
| `describe_entities` | List all entities and metadata | Always available |
| `create_record` | Insert new records | CREATE |
| `read_records` | Query with filters | READ |
| `update_record` | Modify existing records | UPDATE |
| `delete_record` | Remove records | DELETE |
| `execute_entity` | Run stored procedures | EXECUTE |

## 📝 Common Workflows

### 1. Discovery First
```
Step 1: describe_entities (nameOnly: true)
Step 2: describe_entities (entities: ["TableName"])
Step 3: Perform operations
```

### 2. Create Operation
```json
{
  "tool": "create_record",
  "arguments": {
    "entityName": "Product",
    "item": {"name": "...", "price": 99.99}
  }
}
```

### 3. Query with Filters
```json
{
  "tool": "read_records",
  "arguments": {
    "entityName": "Product",
    "filter": {"category": "Electronics", "price": {"$lte": 100}},
    "orderby": "price asc",
    "take": 10
  }
}
```

## 🔐 Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `DATABASE_CONNECTION_STRING` | Yes | Database connection string |
| `DAB_CONFIG_PATH` | No | Path to dab-config.json |
| `X_MS_API_ROLE` | No | Role for permissions (default: anonymous) |

## 🗄️ Database Support

✅ Microsoft SQL Server (2016+)  
✅ Azure SQL Database  
⏳ PostgreSQL (Coming Soon)  
⏳ MySQL (Coming Soon)  
⏳ Azure Cosmos DB (Coming Soon)

## 📚 Key Resources

- **Docs**: https://learn.microsoft.com/azure/data-api-builder/
- **GitHub**: https://github.com/Azure/data-api-builder
- **MCP Registry**: (Pending submission)
- **Issues**: https://github.com/Azure/data-api-builder/issues

## 🔍 Troubleshooting

**Problem**: Tool not available  
**Solution**: Check permissions in dab-config.json and X_MS_API_ROLE header

**Problem**: Connection fails  
**Solution**: Verify DATABASE_CONNECTION_STRING and database accessibility

**Problem**: Permission denied  
**Solution**: Ensure role has required action (CREATE/READ/UPDATE/DELETE/EXECUTE)

## 📦 Submission Package Location

```
mcp-registry-submission/
  ├── data-api-builder/
  │   ├── server.json
  │   ├── README.md
  │   └── examples/
  ├── SUBMISSION_GUIDE.md
  └── README.md
```

## ✅ Pre-Submission Checklist

- [ ] server.json validated
- [ ] README.md comprehensive
- [ ] Examples tested
- [ ] mcp-publisher installed
- [ ] GitHub authentication ready
- [ ] NuGet package published

## 🎯 Registry Information

**Server Name**: `io.github.azure/data-api-builder`  
**Package**: `Microsoft.DataApiBuilder` (NuGet)  
**Transport**: stdio  
**License**: MIT  

---

**Quick Reference v1.0** | December 10, 2024
