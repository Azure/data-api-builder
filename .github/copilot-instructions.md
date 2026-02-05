# Data API builder (DAB) - Copilot Instructions

## Project Overview

Data API builder (DAB) is an open-source, no-code tool that creates secure, full-featured REST, GraphQL endpoints for databases. It's a CRUD data API engine that runs in a container—on Azure, any other cloud, or on-premises. It also supports creation of DML and Custom MCP tools to build a SQL MCP Server backed by a SQL database.

### Key Technologies
- **Language**: C# / .NET
- **.NET Version**: .NET 8.0 (see `global.json`)
- **Supported Databases**: Azure SQL, SQL Server, SQLDW, Cosmos DB, PostgreSQL, MySQL
- **API Types**: REST, GraphQL, MCP
- **Deployment**: Cross-platform (Azure, AWS, GCP, on-premises)

## Project Structure

```
data-api-builder/
├── src/
│   ├── Auth/                        # Authentication logic
│   ├── Cli/                         # Command-line interface (dab CLI)
│   ├── Cli.Tests/                   # CLI tests
│   ├── Config/                      # Configuration handling
│   ├── Core/                        # Core engine components
│   ├── Service/                     # Main DAB service/runtime
│   ├── Service.GraphQLBuilder/      # GraphQL schema builder
│   ├── Service.Tests/               # Integration tests
│   └── Azure.DataApiBuilder.sln     # Main solution file
├── config-generators/               # Config file generation helpers
├── docs/                            # Documentation
├── samples/                         # Sample configurations and projects
├── schemas/                         # JSON schemas for config validation
├── scripts/                         # Build and utility scripts
└── templates/                       # Project templates
```

## Building and Testing

### Prerequisites
- .NET 8.0 SDK or later
- Database server for testing (SQL Server, PostgreSQL, MySQL, or Cosmos DB)

### Building the Project

```bash
# Build the entire solution
dotnet build src/Azure.DataApiBuilder.sln

# Clean and rebuild
dotnet clean src/Azure.DataApiBuilder.sln
dotnet build src/Azure.DataApiBuilder.sln
```

### Running Tests

DAB uses integration tests that require database instances with proper schemas.

**SQL-based tests:**
```bash
# MsSql tests
dotnet test --filter "TestCategory=MsSql"

# PostgreSQL tests
dotnet test --filter "TestCategory=PostgreSql"

# MySQL tests
dotnet test --filter "TestCategory=MySql"
```

**CosmosDB tests:**
```bash
dotnet test --filter "TestCategory=CosmosDb_NoSql"
```

**Test Configuration:**
- Test database schemas are in `src/Service.Tests/DatabaseSchema-<engine>.sql`
- Config files are `src/Service.Tests/dab-config.<engine>.json`
- Connection strings should use `@env('variable_name')` syntax - never commit connection strings

### Running Locally

1. Open the solution: `src/Azure.DataApiBuilder.sln`
2. Copy a config file from `src/Service.Tests/dab-config.<engine>.json` to `src/Service/`
3. Update connection string (use environment variables)
4. Set `Azure.DataApiBuilder.Service` as startup project
5. Select debug profile: `MsSql`, `PostgreSql`, `CosmosDb_NoSql`, or `MySql`
6. Build and run

## Code Style and Conventions

### Formatting
- **Tool**: `dotnet format` (enforced in CI)
- **Indentation**: 4 spaces for C# code, 2 spaces for YAML/JSON
- **Line endings**: LF (Unix-style)
- **Character encoding**: UTF-8
- **Trailing whitespace**: Removed
- **Final newline**: Required

### Running Code Formatter

```bash
# Format all files
dotnet format src/Azure.DataApiBuilder.sln

# Verify formatting (CI check)
dotnet format src/Azure.DataApiBuilder.sln --verify-no-changes
```

### C# Conventions
- **Usings**: Sort system directives first, no separation between groups
- **Type preferences**: Use language keywords (`int`, `string`) over BCL types (`Int32`, `String`)
- **Naming**: Follow standard .NET naming conventions
- **`this.` qualifier**: Not used unless necessary

### SQL Query Formatting
When adding or modifying generated SQL queries in tests:
- **PostgreSQL**: Use https://sqlformat.org/ (remove unnecessary double quotes)
- **SQL Server**: Use https://poorsql.com/ (enable "trailing commas", indent string: `\s\s\s\s`)
- **MySQL**: Use https://poorsql.com/ (same as SQL Server, max line width: 100)

## Testing Guidelines

### Test Organization
- Integration tests validate the engine's query generation and database operations
- Tests are organized by database type using TestCategory attributes
- Each database type has its own config file and schema

### Adding New Tests
- Work within the existing database schema (SQL) or GraphQL schema (CosmosDB)
- Add tests to the appropriate test class
- Use base class methods and helpers for engine operations
- Format any generated SQL queries using the specified formatters
- Do not commit connection strings to the repository

### Test Database Setup
1. Create database using the appropriate server
2. Run the schema script: `src/Service.Tests/DatabaseSchema-<engine>.sql`
3. Set connection string in config using `@env()` syntax
4. Run tests for that specific database type

## Configuration Files

### DAB Configuration
- Config files use JSON format with schema validation
- Schema files are in the `schemas/` directory
- Use `@env('variable_name')` to reference environment variables
- Never commit connection strings or secrets

### Config Generation
Use the config-generators directory for automated config file creation:
```bash
# Build with config generation
dotnet build -p:generateConfigFileForDbType=<database_type>
```
Supported types: `mssql`, `postgresql`, `cosmosdb_nosql`, `mysql`

## Security Practices

- **Never commit secrets**: Use environment variables with `@env()` syntax
- **Connection strings**: Always use `.env` files (add to `.gitignore`)
- **Authentication**: Supports AppService, EasyAuth, StaticWebApps, JWT
- **Authorization**: Role-based permissions in config
- **set-session-context**: Available for SQL Server row-level security

## API Development

### REST API
- Base path: `/api` (configurable)
- Follows Microsoft REST API Guidelines
- Request body validation available
- Health endpoint: `/health`
- Swagger UI in development mode: `/{REST_PATH}/openapi` (default: `/api/openapi`)

### GraphQL API
- Base path: `/graphql` (configurable)
- Introspection enabled in development mode
- Nitro UI in development mode: `/graphql`
- Schema generated from database metadata
### MCP Tools
- Base Path:  `/mcp` (configurable)
- Discover tools with MCP Inspector

## Common Commands

```bash
# Install DAB CLI globally
dotnet tool install microsoft.dataapibuilder -g

# Initialize a new config
dab init --database-type <type> --connection-string "@env('connection_string')" --host-mode development

# Add an entity to config
dab add <entity_name> --source <schema.table> --permissions "anonymous:*"

# Start DAB locally
dab start

# Validate a config file
dab validate
```

## Contributing

- Sign the Contributor License Agreement (CLA)
- Follow the Microsoft Open Source Code of Conduct
- Use issue templates when reporting bugs or requesting features
- Include configuration files, logs, and hosting model in issue reports
- Run `dotnet format` before committing
- Do not commit connection strings or other secrets

### Commit Signing

All commits should be signed to receive the verified badge on GitHub. Configure GPG or SSH signing:

**GPG Signing:**
```bash
# Generate a GPG key
gpg --full-generate-key

# List keys and copy the key ID
gpg --list-secret-keys --keyid-format=long

# Configure Git to use the key
git config --global user.signingkey <KEY_ID>
git config --global commit.gpgsign true

# Add GPG key to GitHub account
gpg --armor --export <KEY_ID>
```

**SSH Signing:**
```bash
# Generate an SSH key
ssh-keygen -t ed25519 -C "your_email@example.com"

# Configure Git to use SSH signing
git config --global gpg.format ssh
git config --global user.signingkey ~/.ssh/id_ed25519.pub
git config --global commit.gpgsign true

# Add SSH key to GitHub account as signing key
```

## References

- [Official Documentation](https://learn.microsoft.com/azure/data-api-builder/)
- [Samples](https://aka.ms/dab/samples)
- [Known Issues](https://learn.microsoft.com/azure/data-api-builder/known-issues)
- [Feature Roadmap](https://github.com/Azure/data-api-builder/discussions/1377)
- [Microsoft REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/Guidelines.md)
- [GraphQL Specification](https://graphql.org/)
