
# MCP Inspector Testing Guide

Steps to run and test MCP tools using the https://www.npmjs.com/package/@modelcontextprotocol/inspector.
### Pre-requisite:
- Node.js must be installed on your system to run this code.
- Ensure that the DAB MCP server is running before attempting to connect with the inspector tool.

### 1. **Install MCP Inspector** 
npx @modelcontextprotocol/inspector

### 2. ** Bypass TLS Verification (For Local Testing)**
set NODE_TLS_REJECT_UNAUTHORIZED=0

### 3. ** Open the inspector with pre-filled token.**
http://localhost:6274/?MCP_PROXY_AUTH_TOKEN=<token>

### 4. ** How to use the tool..**
- Set the transport type "Streamable HTTP".
- Set the URL "http://localhost:5000/mcp" and hit connect.
- Select a Tool from the dropdown list.
- Fill in the Parameters required for the tool.
- Click "Run" to execute the tool and view the response.