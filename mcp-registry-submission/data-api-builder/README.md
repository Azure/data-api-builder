# Data API Builder (DAB) MCP Server

<!-- mcp-name: io.github.azure/data-api-builder -->

A Model Context Protocol (MCP) server that provides AI assistants with the ability to configure, introspect, and interact with database-backed APIs through Azure Data API Builder (DAB). This C#-based MCP server enables dynamic database schema discovery, configuration generation, and CRUD operations for Microsoft SQL Server and Azure SQL Database.

## 🌟 Overview

**Data API Builder (DAB)** is an open-source tool that creates secure, production-ready REST and GraphQL endpoints for your database. The DAB MCP Server extends this capability to AI assistants, allowing them to:

- 🔍 **Discover database schemas** - Introspect tables, columns, relationships, and stored procedures
- ⚙️ **Generate DAB configurations** - Automatically create `dab-config.json` files based on database metadata
- 📝 **Perform CRUD operations** - Create, read, update, and delete records with permission-aware access
- 🔐 **Manage permissions** - Understand and respect entity-level authorization rules
- 🚀 **Execute stored procedures** - Invoke database procedures and functions

This MCP server empowers AI agents to build data-driven applications by bridging the gap between natural language instructions and database operations.

## 🎯 Key Features

### Database Support
- **Microsoft SQL Server** (2016 and later)
- **Azure SQL Database**

> **Note**: Support for additional databases (PostgreSQL, MySQL, Cosmos DB) is planned for future releases.

### MCP Tools Provided

#### 1. `describe_entities`
Lists all entities configured in DAB with their metadata, including:
- Entity names and descriptions
- Field definitions with aliases
- Parameter specifications (for stored procedures)
- Permission mappings per role
- Entity types (table, view, stored procedure)

**Use Case**: Always call this tool first to understand what data structures are available and what operations are permitted for the current user role.

#### 2. `create_record`
Creates a new record in a database table with validation and permission checking.

**Use Case**: Insert new data into tables while respecting field constraints and CREATE permissions.

#### 3. `read_records`
Retrieves records from tables or views with support for:
- Filtering by field values
- Pagination (skip/take)
- Sorting (orderby)
- Field selection

**Use Case**: Query and retrieve data from the database with flexible filtering options.

#### 4. `update_record`
Updates existing records in database tables by primary key.

**Use Case**: Modify existing data while maintaining referential integrity and permission boundaries.

#### 5. `delete_record`
Removes records from database tables by primary key.

**Use Case**: Delete data with proper authorization checks and cascading awareness.

#### 6. `execute_entity`
Executes stored procedures and functions with parameter binding.

**Use Case**: Invoke complex database logic encapsulated in stored procedures.

### Configuration Management

The MCP server integrates with DAB's configuration system:
- Reads existing `dab-config.json` files
- Validates entity definitions
- Respects permission and authorization settings
- Supports role-based access control (RBAC)

## 📋 Prerequisites

- **.NET 8.0 Runtime** or later
- **Microsoft SQL Server** (2016+) or **Azure SQL Database**
- **DAB Configuration File** (`dab-config.json`)
- **Database Connection String**

## 🚀 Installation

### Option 1: Using NuGet Package

The DAB MCP server is distributed as part of the `Microsoft.DataApiBuilder` NuGet package.

```bash
dotnet tool install --global Microsoft.DataApiBuilder
```

### Option 2: Building from Source

```bash
# Clone the repository
git clone https://github.com/Azure/data-api-builder.git
cd data-api-builder

# Build the solution
dotnet build src/Azure.DataApiBuilder.sln

# Run the MCP server
dotnet run --project src/Service/Azure.DataApiBuilder.Service.csproj
```

## ⚙️ Configuration

### 1. Create a DAB Configuration File

Use the DAB CLI to initialize your configuration:

```bash
# Initialize configuration for SQL Server
dab init \
  --database-type mssql \
  --connection-string "@env('DATABASE_CONNECTION_STRING')" \
  --host-mode development

# Add entities to the configuration
dab add Product \
  --source "dbo.Products" \
  --permissions "anonymous:*"

dab add Customer \
  --source "dbo.Customers" \
  --permissions "authenticated:read,create,update"
```

This generates a `dab-config.json` file that the MCP server will use.

### 2. Set Environment Variables

Create a `.env` file or set environment variables:

```bash
DATABASE_CONNECTION_STRING="Server=localhost;Database=MyDB;User Id=sa;Password=YourPassword;TrustServerCertificate=True"
DAB_CONFIG_PATH="./dab-config.json"
X_MS_API_ROLE="authenticated"
```

### 3. Configure MCP Client

#### For Claude Desktop

Add to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "data-api-builder": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/data-api-builder/src/Service/Azure.DataApiBuilder.Service.csproj"
      ],
      "env": {
        "DATABASE_CONNECTION_STRING": "Your-Connection-String",
        "DAB_CONFIG_PATH": "/path/to/dab-config.json",
        "X_MS_API_ROLE": "authenticated"
      }
    }
  }
}
```

#### For Other MCP Clients

The DAB MCP server uses **stdio transport**, making it compatible with any MCP client supporting this standard.

## 📖 Usage Examples

### Example 1: Discover Available Entities

**User Request**: "What tables are available in my database?"

**MCP Tool Call**:
```json
{
  "tool": "describe_entities",
  "arguments": {
    "nameOnly": true
  }
}
```

**Response**:
```json
{
  "entities": [
    {"name": "Product", "description": "Product catalog"},
    {"name": "Customer", "description": "Customer information"},
    {"name": "Order", "description": "Sales orders"}
  ],
  "count": 3
}
```

### Example 2: Get Detailed Entity Metadata

**User Request**: "Show me the fields in the Product table"

**MCP Tool Call**:
```json
{
  "tool": "describe_entities",
  "arguments": {
    "nameOnly": false,
    "entities": ["Product"]
  }
}
```

**Response**:
```json
{
  "entities": [
    {
      "name": "Product",
      "description": "Product catalog",
      "fields": [
        {"name": "id", "description": "Product identifier"},
        {"name": "name", "description": "Product name"},
        {"name": "price", "description": "Unit price"},
        {"name": "category", "description": "Product category"}
      ],
      "permissions": ["CREATE", "READ", "UPDATE", "DELETE"]
    }
  ]
}
```

### Example 3: Create a New Record

**User Request**: "Add a new product called 'Laptop' with price 999"

**MCP Tool Call**:
```json
{
  "tool": "create_record",
  "arguments": {
    "entityName": "Product",
    "item": {
      "name": "Laptop",
      "price": 999,
      "category": "Electronics"
    }
  }
}
```

**Response**:
```json
{
  "success": true,
  "message": "Record created successfully",
  "id": 42
}
```

### Example 4: Query Records with Filtering

**User Request**: "Show me all products in the Electronics category with price under 1000"

**MCP Tool Call**:
```json
{
  "tool": "read_records",
  "arguments": {
    "entityName": "Product",
    "filter": {
      "category": "Electronics",
      "price": {"$lt": 1000}
    },
    "orderby": "price",
    "take": 10
  }
}
```

### Example 5: Execute a Stored Procedure

**User Request**: "Get the top selling products for this month"

**MCP Tool Call**:
```json
{
  "tool": "execute_entity",
  "arguments": {
    "entityName": "GetTopSellingProducts",
    "parameters": {
      "month": 12,
      "year": 2024,
      "topN": 10
    }
  }
}
```

## 🔐 Security & Permissions

### Role-Based Access Control

The DAB MCP Server enforces permissions defined in `dab-config.json`:

```json
{
  "entities": {
    "Product": {
      "permissions": [
        {
          "role": "anonymous",
          "actions": ["read"]
        },
        {
          "role": "authenticated",
          "actions": ["read", "create", "update"]
        },
        {
          "role": "admin",
          "actions": ["*"]
        }
      ]
    }
  }
}
```

The MCP server reads the `X-MS-API-ROLE` header to determine which role context to use for permission evaluation.

### Security Best Practices

1. **Never commit connection strings** to source control
2. **Use environment variables** for sensitive data
3. **Configure least-privilege roles** in DAB configuration
4. **Enable authentication** in production environments
5. **Use TLS/SSL** for database connections
6. **Audit tool usage** via DAB's logging capabilities

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    MCP Client (Claude, etc.)            │
└────────────────────────┬────────────────────────────────┘
                         │ MCP Protocol (stdio)
┌────────────────────────▼───────────────────────────────┐
│              DAB MCP Server (C#)                       │
│  ┌─────────────────────────────────────────────────┐   │
│  │  Tool Registry                                  │   │
│  │  • describe_entities                            │   │
│  │  • create_record / read_records                 │   │
│  │  • update_record / delete_record                │   │
│  │  • execute_entity                               │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │  DAB Core Engine                                │   │
│  │  • Config Management                            │   │
│  │  • Authorization Resolver                       │   │
│  │  • Query Builder                                │   │
│  │  • Metadata Provider                            │   │
│  └─────────────────────────────────────────────────┘   │
└────────────────────────┬───────────────────────────────┘
                         │ ADO.NET / SQL Server Driver
┌────────────────────────▼────────────────────────────────┐
│   Database (Microsoft SQL Server / Azure SQL Database)  │
└─────────────────────────────────────────────────────────┘
```

## 🔧 Advanced Configuration

### Enabling/Disabling MCP Tools

Control which tools are available via `dab-config.json`:

```json
{
  "runtime": {
    "mcp": {
      "tools": {
        "describeEntities": true,
        "createRecord": true,
        "readRecords": true,
        "updateRecord": true,
        "deleteRecord": false,
        "executeEntity": true
      }
    }
  }
}
```

### Custom Transport Configuration

While stdio is default, DAB MCP Server also supports HTTP with SSE for remote scenarios:

```bash
# Run with HTTP transport on port 5000
dotnet run --project src/Service/Azure.DataApiBuilder.Service.csproj --urls "http://localhost:5000"
```

Then configure MCP client to use HTTP endpoint: `http://localhost:5000/mcp`

## 🧪 Testing

### Using MCP Inspector

```bash
# Install MCP Inspector
npm install -g @modelcontextprotocol/inspector

# Set bypass for local TLS (development only)
set NODE_TLS_REJECT_UNAUTHORIZED=0

# Open inspector
npx @modelcontextprotocol/inspector
```

Configure inspector:
- Transport: Streamable HTTP
- URL: `http://localhost:5000/mcp`
- Auth Token: (if configured)

See [MCP Inspector Testing Guide](../../docs/Testing/mcp-inspector-testing.md) for detailed instructions.

## 📚 Resources

- **DAB Documentation**: https://learn.microsoft.com/azure/data-api-builder/
- **GitHub Repository**: https://github.com/Azure/data-api-builder
- **MCP Specification**: https://modelcontextprotocol.io/
- **Getting Started Guide**: https://learn.microsoft.com/azure/data-api-builder/get-started/get-started-with-data-api-builder
- **DAB CLI Reference**: https://learn.microsoft.com/azure/data-api-builder/cli-reference

## 🤝 Contributing

Contributions are welcome! Please see:
- [Code of Conduct](https://github.com/Azure/data-api-builder/blob/main/CODE_OF_CONDUCT.md)
- [Contributing Guide](https://github.com/Azure/data-api-builder/blob/main/CONTRIBUTING.md)
- [Security Policy](https://github.com/Azure/data-api-builder/blob/main/SECURITY.md)

## 📄 License

This project is licensed under the [MIT License](https://github.com/Azure/data-api-builder/blob/main/LICENSE.txt).

Copyright (c) Microsoft Corporation. All rights reserved.

## 🐛 Known Issues & Limitations

- MCP tools are currently in **preview** status
- **Database Support**: Currently only Microsoft SQL Server and Azure SQL Database are supported. Support for PostgreSQL, MySQL, and Cosmos DB  currently not planned.
- Large result sets should be paginated for performance

For the full list, see: https://learn.microsoft.com/azure/data-api-builder/known-issues

## 💬 Support

- **GitHub Issues**: https://github.com/Azure/data-api-builder/issues
- **Discussions**: https://github.com/Azure/data-api-builder/discussions

---

**Built with ❤️ by Microsoft Azure**
