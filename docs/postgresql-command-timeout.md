# PostgreSQL Command Timeout Configuration

This document describes how to configure command timeout for PostgreSQL data sources in Data API Builder.

## Overview

Data API builder now supports configuring PostgreSQL command timeout through the `command-timeout` option in the data source configuration. This feature allows you to override the default command timeout for all PostgreSQL queries executed by Data API builder.

## Configuration

Add the `command-timeout` option to your PostgreSQL data source configuration:

```json
{
  "data-source": {
    "database-type": "postgresql",
    "connection-string": "Host=localhost;Database=mydb;Username=user;Password=pass;",
    "options": {
      "command-timeout": 60
    }
  }
}
```

### Parameters

- **command-timeout**: Integer value representing the timeout in seconds
  - **Type**: `integer`
  - **Minimum**: `0`
  - **Default**: `30` (if not specified)
  - **Description**: Sets the wait time (in seconds) before terminating the attempt to execute a command and generating an error

### Behavior

1. **Override**: The `command-timeout` value from the configuration will override any `CommandTimeout` parameter present in the connection string
2. **Precedence**: Configuration file setting takes priority over connection string setting
3. **Scope**: Applies to all PostgreSQL queries executed through Data API Builder

### Example

```json
{
  "$schema": "schemas/dab.draft.schema.json",
  "data-source": {
    "database-type": "postgresql",
    "connection-string": "Host=localhost;Database=bookstore;Username=postgres;Password=password;",
    "options": {
      "command-timeout": 120
    }
  },
  "runtime": {
    "rest": { "enabled": true, "path": "/api" },
    "graphql": { "enabled": true, "path": "/graphql" }
  },
  "entities": {
    "Book": {
      "source": { "object": "books", "type": "table" },
      "permissions": [
        { "role": "anonymous", "actions": [{ "action": "*" }] }
      ]
    }
  }
}
```

In this example, all PostgreSQL queries will have a 120-second timeout, regardless of any `CommandTimeout` value in the connection string.

## Implementation Details

The feature is implemented by:

1. **Schema Validation**: The JSON schema validates the `command-timeout` parameter
2. **Options Parsing**: The `PostgreSqlOptions` class parses the timeout value from various data types (integer, string, JsonElement)
3. **Connection String Processing**: The timeout is applied to the Npgsql connection string builder during connection string normalization
4. **Override Logic**: Configuration values take precedence over existing connection string parameters

## Related

- See `samples/postgresql-command-timeout-example.json` for a complete working example
- For other database types, command timeout can be configured directly in the connection string