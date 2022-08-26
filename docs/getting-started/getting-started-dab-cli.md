# Getting started with Data API Builder (`dab`) CLI

Welcome to this getting started tutorial that will guide you to install and setup `dab` CLI tool locally on your machine and then will guide you to generate and modify the config file using this tool, which will be used for running Data API Builder.

## Prerequisite

This CLI tool is part of [.NET global tools](https://www.nuget.org/packages?packagetype=dotnettool). As a prerequisite to install and run this tool, you'll need to have [.NET SDK](https://dotnet.microsoft.com/en-us/download) >=6 installed on your development machine.

## Install CLI

Make sure you have installed the `dab` CLI tool as described here: [Running Data API Builder for Azure Databases using CLI](../running-using-dab-cli.md)

## Generate the config file

This CLI tool will generate a `dab` engine config file for you, then you can build your config by adding required entities, relationships, roles, and permissions etc.

To initialize the config file, use the init command. For example, the following sample command
generates a config file for SQL DB with CORS settings:

```dotnetcli
# dab init --config dab-config.json --database-type mssql --connection-string "HOST=XXX" --host-mode Development --cors-origin "http://localhost:3000"

dab init --config <filename> --database-type <mssql|cosmos|postgresql|mysql> --connection-string <connection_string>
```

To validate, navigate to your folder path (where you should be currently) and you should see the config file generated there with given file name, e.g. `xxx-xxx.json`.

> [!NOTE]
> While initializing, the CLI would create config file with the default name of `dab-config.json` if no config file name supplied.

## Add entities to config

To add the entities to the config file with the GraphQL type and permissions defined, run the following add command:
```dotnetcli
# dab add book --source dbo.books --graphql book --permissions "anonymous:*"

dab add <entity> --source <source_db> --graphql <graphql_type> --permissions <roles:actions>
```

### Update entities in config

To update entities which are already added to the config, run the following update command:

```dotnetcli
# dab update book --permissions "authenticate:create,update" --fields.include "id,title"

dab update <entity> --source <new_source_db> --graphql <new_graphql_type> --permissions <rules:actions> --fields.include <fields_to_include> --fields.exclude <fields_to_exclude>
```

### Add entity relationship mappings

To add relationship mappings between entities, use the following command:
Make sure both the source and target entities have already been added.

```dotnetcli
# dab add author --source dbo.authors --permissions "anonymous:create,read,update"
```

```dotnetcli
# dab update author --relationship books --target.entity book --cardinality many --linking.object dbo.books_authors

dab update <entity> --relationship <relationship_name> --target.entity <target_entity> --cardinality <one | many>
```

### Add database policies to role permissions

To add policy details, run the following command:

```dotnetcli
# dab update book --permissions "authenticated:update" --fields.include "*" --policy-database "@claims.id eq @item.id"

dab update <entity> --permissions <roles> --fields.include <fields> --policy-database <policy_conditions>
```

## Run the Data API Builder

To start the Data API Builder engine, use:

```dotnetcli
# dab start --config my-config.json
```

> [!NOTE]
> While starting Data API Builder engine, you will need to supply `--config` only if you are using any custom config file name, other than default (`dab-config.json or dab-config.{DAB_ENVIRONMENT}.json`), or the config file exists on different file path.
>
> DAB loads the config in the following order (if not supplied in command-line arguments):
> - `dab-config.{DAB_ENVIRONMENT}.json` : For example, the `dab-config.Development.json` and `dab-config.Production.json` files. The environment version of the file is loaded based on the value set for `DAB_ENVIRONMENT` environment variable.
> - `dab-config.json`
