# Getting started with Data API Builder (DAB) CLI

Welcome to this getting started tutorial that will guide you to install and setup DAB CLI tool locally on your machine and then will guide you to generate and modify config file using this tool, which will be used for running Data API Builder.

## Prerequisite

This CLI tool is part of [.NET global tools](https://www.nuget.org/packages?packagetype=dotnettool). As a prerequisite to install and run this tool, you'll need to have [.NET SDK](https://dotnet.microsoft.com/en-us/download) >=6 installed on your DEV machine.

## Install the DAB CLI

You can install the DAB CLI using the [.NET tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools).

- Download the package: [dab-cli.0.0.5.nupkg](https://github.com/Azure/hawaii-cli/tags) file.
- Navigate to the folder where the package file is downloaded.

To install this tool globally, use:

```dotnetcli
dotnet tool install -g --add-source ./ dab-cli --version <version_number>
```

### Update the package version

If you already have an older version of DAB CLI installed, to upgrade the tool, use:

```dotnetcli
dotnet tool update -g --add-source ./ dab-cli --version <version_number>
```

### Validate the Install

Installing the package will make the `dab` command available on your development machine. To validate your installation, you can check the installed version with:

```dotnetcli
dab --version
```

## Generate the config file

This CLI tool will generate a config file for you, then you can build your config by adding required entities, relationships, role, and permissions etc.

To initialize the config file, use:

```dotnetcli
#For example, sample command to generate config file for SQL DB with CORS settings:
# dab init --name dab-config.json --database-type mssql --connection-string "HOST=XXX" --host-mode Development --cors-origin "http://localhost:3000"

dab init --name <filename> --database-type <db_type> --connection-string <connection_string>
```

To validate, navigate to your folder path (where you should be currently) and you should see the `xxx-xxx.config` file generated there.

> [!NOTE]
> While initializing, the CLI would create config file with the default name of `dab-config.json` if no config file name supplied.

## Add entities in config

To add the entities in config file with REST route, GraphQL type and permissions, run the commands as below:
```dotnetcli
# dab add todo --source s001.todo --rest todo --graphql todo --permission "anonymous:*"

dab add <entity> -source <source_db> --rest <rest_route> --graphql <graphql_type> --permissions <roles:actions>
```

You can also run the multiple commands in a batch to perform multiple actions. For example, to add entities for `Publisher`, `Stock` and `Book` you can run the command as below:

```dotnetcli
dab add Publisher --source publishers --permissions "anonymous:read"
dab add Stock --source stocks --permissions "anonymous:create,read,update"
dab add Book --source books --permissions "anonymous:read"
```

### Update entities in config

To update entities already added to the config, run the following update command:

```dotnetcli
# dab update todo --permission "authenticate:create" --fields.include "id,name,category"

dab update <entity> --source <new_source_db> --rest <new_rest_route> --graphql <new_graphql_type> --permissions <rules:actions> --fields.include <fields_to_include> --fields.exclude <fields_to_exclude>
```

### Add mappings

To add relationship mappings between entities, use command as below:

```dotnetcli
# dab update Book --map "id:id,title:title"

dab update <entity> --map <fields>
```

### Add policy details

To add policy details, run the command as below:

```dotnetcli
# dab update Book --permissions "anonymous:read" --fields.include "*" --policy-database "@claims.id eq @item.id"

dab update <entity> --permission <roles> --fields.include <fields> --policy-database <policy_conditions>
```

## Run the Data API Builder

To start the Data API Builder engine, use:

```dotnetcli
dab start --config my-config.json
```

> [!NOTE]
> While starting Data API Builder, you will need to supply `--config` only if you are using any custom config file name, other than default `dab-config.json`, or the config file exists on different file path.
