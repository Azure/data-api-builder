# Getting started with Data API Builder (DAB) CLI

Welcome to this getting started tutorial that will guide you to install and setup DAB CLI tool locally on your machine and then will guide you to generate and modify config file using this tool, which will be used for running Data API Builder.

## Prerequisite

This CLI tool is part of [.NET global tools](https://www.nuget.org/packages?packagetype=dotnettool). As a prerequisite to install and run this tool, you'll need to have [.NET SDK](https://dotnet.microsoft.com/en-us/download) >=6 installed on your DEV machine.

## Install the DAB CLI

You can install the DAB CLI using [.NET tools](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools).

- Download the latest version of the package: [dab.<version_number>.nupkg](https://msdata.visualstudio.com/CosmosDB/_artifacts/feed/DataApiBuilder)
- Navigate to the folder where the package file is downloaded.

To install this tool globally, use:

```dotnetcli
dotnet tool install -g --add-source ./ dab --version <version_number>
```

### Update the package version

If you already have an older version of DAB CLI installed, update the tool using:

```dotnetcli
dotnet tool update -g --add-source ./ dab --version <version_number>
```

### Validate the Install

Installing the package will make the `dab` command available on your development machine. To validate your installation, you can check the installed version with:

```dotnetcli
dab --version
```

## Generate the config file

This CLI tool will generate a DAB engine config file for you, then you can build your config by adding required entities, relationships, roles, and permissions etc.

To initialize the config file, use:

```dotnetcli
#For example, sample command to generate config file for SQL DB with CORS settings:
# dab init --config dab-config.json --database-type mssql --connection-string "HOST=XXX" --host-mode Development --cors-origin "http://localhost:3000"

dab init --config <filename> --database-type <db_type> --connection-string <connection_string>
```

To validate, navigate to your folder path (where you should be currently) and you should see the config file generated there with given file name, e.g. `xxx-xxx.json`.

> [!NOTE]
> While initializing, the CLI would create config file with the default name of `dab-config.json` if no config file name supplied.

## Add entities to config

To add the entities to the config file with the REST route, GraphQL type and permissions defined, run the following commands:
```dotnetcli
# dab add book --source dbo.books --rest book --graphql book --permissions "anonymous:*"

dab add <entity> --source <source_db> --rest <rest_route> --graphql <graphql_type> --permissions <roles:actions>
```

You can also run multiple commands in a single batch to perform multiple actions. For example, to add entities for `author` and `book` you can run the following commands:

```dotnetcli
dab add author --source dbo.authors --permissions "anonymous:create,read,update"
dab add book --source dbo.books --permissions "anonymous:read"
```

### Update entities in config

To update entities already added to the config, run the following update command:

```dotnetcli
# dab update book --permissions "authenticate:create" --fields.include "id,title"

dab update <entity> --source <new_source_db> --rest <new_rest_route> --graphql <new_graphql_type> --permissions <rules:actions> --fields.include <fields_to_include> --fields.exclude <fields_to_exclude>
```

### Add entity relationship mappings

To add relationship mappings between entities, use the following command:

```dotnetcli
# dab update author --relationship books --target.entity book --cardinality many --linking.object dbo.books_authors

dab update <entity> --relationship <relationship_name> --target.entity <target_entity> --cardinality <one | many>
```

### Add database policies to role permissions

To add policy details, run the following command:

```dotnetcli
# dab update book --permissions "anonymous:read" --fields.include "*" --policy-database "@claims.id eq @item.id"

dab update <entity> --permissions <roles> --fields.include <fields> --policy-database <policy_conditions>
```

## Run the Data API Builder

To start the Data API Builder engine, use:

```dotnetcli
dab start --config my-config.json
```

> [!NOTE]
> While starting Data API Builder engine, you will need to supply `--config` only if you are using any custom config file name, other than default (`dab-config.json or dab-config.{DAB_ENVIRONMENT}.json`), or the config file exists on different file path.
>
> DAB loads the config in the following order (if not supplied in command-line arguments):
> - `dab-config.{DAB_ENVIRONMENT}.json` : For example, the `dab-config.Development.json` and `dab-config.Production.json` files. The environment version of the file is loaded based on the value set for `DAB_ENVIRONMENT` environment variable.
> - `dab-config.json`
