# Design Document: `dab configure` Command

## Overview

The `dab configure` command is designed to simplify updating config properties outside of the **entities** section. This document outlines the design, functionality, and implementation details of the `dab configure` command.

## Objectives

- Add support to CLI to configure the `data-source` and `runtime` sections of the runtime config.

- Ensure configurations are validated before being applied.

## Command Syntax

```sh

dab configure [options]

```

### Options

Some Example of the options that we currently support:
- `--config `: Specify the configuration file path.
- `--runtime.graphql.depth-limit`: Max allowed depth of the nested query.
- `--data-source.database-type`: Type of database to connect to.
- `--data-source.options.schema`: Schema path for Cosmos DB for NoSql.
- `--help`: Display help information about the command.

**NOTE:** The goal is to make all the properties under the `runtime` and `data-source` section configurable via `configure` command.

### Naming Convention

#### Configuration Structure
The configuration file consists of three main sections:

1. data-source: Defines the database connection and options.
2. runtime: Configures the runtime settings for REST, GraphQL, and host settings.
3. entities: Entity related information.

`dab configure` is only for updating the `data-source` and `runtime` sections of the config. For the `entities` section, we already have the `dab update` command.

#### Naming the option

1. Identify the section to configure. `data-source` or `runtime`.
2. Identify the property in that section to update.
3. '.' is used for nesting.

Example:
```json
{
  "data-source": {
    "database-type": "mssql",
    "connection-string": "xxx",
    "options": {
      "set-session-context": true
    }
  },
  "runtime": {
    "rest": {
      "enabled": true,
      "path": "/api",
      "request-body-strict": true
    },
    "graphql": {
      "enabled": true,
      "path": "/graphql",
      "allow-introspection": true,
      "multiple-mutations": {
        "create": {
          "enabled": true
        }
      }
    },
    "host": {
      "cors": {
        "origins": [
          "http://localhost:5000"
        ],
        "allow-credentials": false
      },
      "authentication": {
        "provider": "StaticWebApps"
      },
      "mode": "development"
    }
  }
}
```
1. To update graphql path in the runtime section, the option name should be
`--runtime.graphql.path`.
2. To update `set-session-context` in data-source, the option name should be
`--data-source.options.set-session-context`.


## Implementation Details

1. Add the New `dab configure` options in `ConfigureOptions.cs` located at `src\Cli\Commands\ConfigureOptions.cs`.

2. Update the method `TryConfigureSettings` in `ConfigGenerator.cs` located at `src\Cli\ConfigGenerator.cs`.

3. Use the existing validation to make sure the user provided input is valid.

4. Make sure the method `TryConfigureSettings(...)` returns `false` in case of any failures and no changes should me made to the config in this case.

5. If the required validations associated with the user input is correct, apply the change and return `true`.


## Testing

- Add Unit tests for command parsing and validation in `ConfigureOptionsTests.cs` located at `src\Cli.Tests\ConfigureOptionsTests.cs`.

- Add Integration tests for end-to-end configuration scenarios in `EndToEndTests.cs` located at `src\Cli.Tests\EndToEndTests.cs`.

- Manual testing for user interactions and error handling.

## Conclusion

The `dab configure` command provides a streamlined way to update config properties outside of entities section.
