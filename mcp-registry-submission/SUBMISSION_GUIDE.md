# MCP Registry Submission Guide for Data API Builder

This document provides instructions for submitting the Data API Builder MCP server to the Model Context Protocol (MCP) public registry.

## 📁 Submission Package Contents

The complete submission package is located in:
```
mcp-registry-submission/data-api-builder/
```

### Files Included:

1. **server.json** - Server manifest with metadata, package info, and environment variables
2. **README.md** - Comprehensive documentation with usage examples and configuration
3. **examples/** - Directory containing JSON examples:
   - `introspect-schema.json` - Schema discovery example
   - `create-record.json` - Record creation example
   - `query-records.json` - Data querying with filters
   - `update-record.json` - Record update example
   - `execute-stored-procedure.json` - Stored procedure execution
   - `complete-workflow.json` - End-to-end multi-step workflow

## 🚀 Submission Steps

### Prerequisites

Before submitting, ensure you have:

1. ✅ **NuGet Account** - The DAB package is published on NuGet as `Microsoft.DataApiBuilder`
2. ✅ **GitHub Account** - For GitHub-based authentication to the MCP registry
3. ✅ **mcp-publisher CLI** - Install the registry publisher tool

### Step 1: Install mcp-publisher

#### Windows (PowerShell):
```powershell
$arch = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq "Arm64") { "arm64" } else { "amd64" }
Invoke-WebRequest -Uri "https://github.com/modelcontextprotocol/registry/releases/latest/download/mcp-publisher_windows_$arch.tar.gz" -OutFile "mcp-publisher.tar.gz"
tar xf mcp-publisher.tar.gz mcp-publisher.exe
rm mcp-publisher.tar.gz
```

Move `mcp-publisher.exe` to a directory in your PATH.

#### macOS/Linux:
```bash
curl -L "https://github.com/modelcontextprotocol/registry/releases/latest/download/mcp-publisher_$(uname -s | tr '[:upper:]' '[:lower:]')_$(uname -m | sed 's/x86_64/amd64/;s/aarch64/arm64/').tar.gz" | tar xz mcp-publisher && sudo mv mcp-publisher /usr/local/bin/
```

#### Homebrew:
```bash
brew install mcp-publisher
```

Verify installation:
```bash
mcp-publisher --help
```

### Step 2: Prepare server.json

The `server.json` file is already prepared in the submission package. Key points:

- **Server Name**: `io.github.azure/data-api-builder`
  - Format matches GitHub authentication requirement (io.github.{org}/{repo})
- **Registry Type**: `nuget`
- **Package Identifier**: `Microsoft.DataApiBuilder`
- **Transport**: `stdio`

### Step 3: Add Verification Information to NuGet Package

The MCP registry requires package verification. For NuGet packages, you need to add an `mcpName` property.

**Note**: Since DAB is already published to NuGet, you'll need to coordinate with the package maintainers to add this metadata in a future release:

```xml
<!-- In the .csproj file -->
<PropertyGroup>
  <PackageId>Microsoft.DataApiBuilder</PackageId>
  <McpName>io.github.azure/data-api-builder</McpName>
  <!-- other properties -->
</PropertyGroup>
```

**Alternative**: The registry also supports verification via repository URL matching, which is already configured in `server.json`.

### Step 4: Authenticate with MCP Registry

Use GitHub authentication (recommended for `io.github.*` namespaced servers):

```bash
mcp-publisher login github
```

This will:
1. Display a device code (e.g., `ABCD-1234`)
2. Provide a URL: `https://github.com/login/device`
3. Wait for you to authorize the application

Follow the prompts in your browser and enter the code.

Upon success, you'll see:
```
Successfully authenticated!
✓ Successfully logged in
```

### Step 5: Publish to MCP Registry

Navigate to the submission package directory:

```bash
cd mcp-registry-submission/data-api-builder
```

Publish the server:

```bash
mcp-publisher publish
```

Expected output:
```
Publishing to https://registry.modelcontextprotocol.io...
✓ Successfully published
✓ Server io.github.azure/data-api-builder version 1.0.0
```

### Step 6: Verify Publication

Verify the server is live in the registry:

```bash
curl "https://registry.modelcontextprotocol.io/v0.1/servers?search=io.github.azure/data-api-builder"
```

You should see your server metadata in the JSON response.

## 🔍 Validation Checklist

Before publishing, verify:

- [ ] `server.json` schema is valid
- [ ] Server name follows `io.github.{org}/{repo}` format
- [ ] NuGet package `Microsoft.DataApiBuilder` exists and is accessible
- [ ] README.md is comprehensive with clear usage examples
- [ ] All example JSON files are well-formed
- [ ] Environment variables are documented
- [ ] Security considerations are noted
- [ ] License information is correct (MIT)
- [ ] Repository URL is valid: https://github.com/Azure/data-api-builder

## 🐛 Troubleshooting

### Error: "Registry validation failed for package"

**Cause**: Package verification failed.

**Solution**: 
- Ensure the NuGet package has the `mcpName` metadata
- Or verify that `repository.url` in `server.json` matches the GitHub repo
- Check that the package version specified exists on NuGet

### Error: "You do not have permission to publish this server"

**Cause**: Authentication method doesn't match server namespace.

**Solution**:
- Server name `io.github.azure/data-api-builder` requires GitHub authentication
- Authenticate as a member of the `Azure` GitHub organization
- Or use DNS authentication for custom domains

### Error: "Invalid or expired Registry JWT token"

**Cause**: Authentication token expired.

**Solution**:
```bash
mcp-publisher login github
```

## 📚 Additional Resources

- **MCP Registry Quickstart**: https://github.com/modelcontextprotocol/registry/blob/main/docs/modelcontextprotocol-io/quickstart.mdx
- **MCP Registry Repository**: https://github.com/modelcontextprotocol/registry
- **Package Types Documentation**: https://github.com/modelcontextprotocol/registry/blob/main/docs/modelcontextprotocol-io/package-types.mdx
- **Authentication Methods**: https://github.com/modelcontextprotocol/registry/blob/main/docs/modelcontextprotocol-io/authentication.mdx

## 🔄 Updating the Server

To publish an updated version:

1. Update the `version` field in `server.json`
2. Ensure the corresponding NuGet package version exists
3. Run `mcp-publisher publish` again

The registry supports versioning, and clients can specify which version to use.

## 📝 Notes for Microsoft/Azure Team

- **Organization Access**: Publishing requires membership in the `Azure` GitHub organization
- **NuGet Package**: Coordinate adding `mcpName` metadata in next DAB release
- **Continuous Publishing**: Consider setting up GitHub Actions for automated publishing
- **Version Alignment**: Keep `server.json` version aligned with NuGet package releases

## ✅ Final Checklist

Before final submission:

- [ ] All files reviewed and tested
- [ ] Examples validated with actual MCP client
- [ ] Documentation is clear and comprehensive
- [ ] Security warnings are appropriate
- [ ] Links are valid and working
- [ ] Contact information is up to date
- [ ] License is clearly stated
- [ ] Known issues are documented

---

**Prepared by**: GitHub Copilot  
**Date**: December 10, 2024  
**Target Registry**: Model Context Protocol (MCP) Public Registry  
**Server**: Data API Builder (DAB) MCP Server
