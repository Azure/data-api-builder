# Contributing

_We are still working on our contribution guidelines. In the meantime, please feel free to open issues and provide feedback on how to streamline the process._

## Introduction

This project welcomes contributions and suggestions. Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit <https://cla.opensource.microsoft.com>.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the Microsoft Open Source Code of Conduct. For more information see the Code of Conduct FAQ or contact <opencode@microsoft.com> with any additional questions or comments.

## Raising Issues

We very much welcome issues to help us improve the project. When submitting an issue, select the appropriate template and fill out the information requested. This will help us to understand the issue and resolve it as quickly as possible. Be sure to include as much information as you can, including configuration files, logs, and hosting model.

## Working with the code

Data API builder (DAB) is a .NET application written in C#, consisting of several projects Core, Auth, CLI, Config, Service and GraphQLBuilder. There are also test projects for the CLI and Service.

### Running locally

Before running the code, ensure you have the correct version of .NET installed (refer to the [global.json](global.json) file) and open the solution from `src/Azure.DataApiBuilder.Service.sln` in Visual Studio (or other editor of choice).

The next step is to ensure you have a config file for DAB defined. You can pick a sample one from the `src/Service.Tests` project and copy the `dab-config.<engine>.json` file to the `src/Service` directory (and if the database is CosmosDb_NoSql the GraphQL schema file too). You can also use the [generator tool](#an-alternative-way-to-generate-config-files) described below. Note these sample configuration files expect the backend database schema to be created from the respective `src/Service.Tests/DatabaseSchema-<engine>.sql` schema file in the `src/Service.Tests` project.

Make sure the config has a valid connection string, or you use the [`@env` syntax to reference an environment variable](https://learn.microsoft.com/azure/data-api-builder/configuration-file#accessing-environment-variables).

#### Visual Studio

Before running the sample code in Visual Studio, you must be sure to have your config file correctly populated with a valid connection string for your configured back end. Once you have the back end running and your configuration file set correctly, you may do the following to run the code in Visual Studio without using the CLI.

1. Open the solution `src/Azure.DataApiBuilder.Service.sln`
2. Select the **Startup Project** `Azure.DataApiBuilder.Service`.
3. Select a **debug profile** for database type: `MsSql`, `PostgreSql`,`CosmosDb_NoSql`, or `MySql`.
4. Select **Clean Solution**
5. Select **Build Solution**
6. Start runtime

#### An alternative way to generate config files

The following steps outline an alternative way of generating config files to assist with local development.

1. The **ConfigGenerators** directory contains text files with DAB commands for each database type.
2. Based on your choice of database, in the respective text file, update the **connection-string** property of the **init** command.
3. Execute the command `dotnet build -p:generateConfigFileForDbType=<database_type>` in the directory `data-api-builder\src` to build the project and generate the config file that can be used when starting DAB. The config file will be generated in the directory `data-api-builder\src\Service`. `mssql`, `postgresql`,`cosmosdb_nosql` and `mysql` are the values that can be used with `generateConfigFileForDbType`. Only the config file for the specified database type will be generated.

### Integration Testing

Primarily DAB uses integration tests to verify the engine operates correctly in how it creates queries and reads/writes against the database. For the integration tests to be run you will need to have the database configured with the expected schema and connection string set in the configuration files.

#### Running Sql (MsSql, PostgreSQL, MySql) tests

To run the SQL tests locally you will need to:

1. Setup a database using the server(s) that you want to test against.
1. Create the database schema using the `src/Service.Tests/DatabaseSchema-<engine>.sql` file.
1. Set the connection string in `src/Service.Tests/dab-config.<engine>.json` or use the [`@env` syntax to reference an environment variable](https://learn.microsoft.com/azure/data-api-builder/configuration-file#accessing-environment-variables).
   - Note - do not commit the connection string to the repo.

Tests can then be run using the following commands:

- `dotnet test --filter "TestCategory=MsSql"` for SQL Server
- `dotnet test --filter "TestCategory=PostgreSql"` for PostgreSQL
- `dotnet test --filter "TestCategory=MySql"` for MySql

Alternatively, you can execute the tests from Visual Studio.

#### Running CosmosDB tests

To run the CosmosDB tests locally you will need to run either the CosmosDB emulator (Windows or cross-platform) or a CosmosDB instance in Azure. You will also need to set the connection string in `dab-config.CosmosDb_NoSql.json`. Since there is no schema for CosmosDB, the tests will create the database and collection for the test run.

#### Modifying tests

_Note: This section is still a work in progress._

Adding new tests will require you to work in the confines of the existing database schema (for SQL) or GraphQL schema (for CosmosDB). Add the test to the appropriate class and use the methods on the base class and helpers to perform the operations against the engine.

Some tests have generated SQL in them. To make those queries readable we have
first put them through a SQL formatter. When adding other autogenerated queries
or modifying existing ones, please use the same SQL formatter. That way diffs in
these queries stay minimal:

- For Postgres use <https://sqlformat.org/>. Then after formatting make sure to
  remove all unnecessary double quotes, because otherwise you have to escape
  them in the multiline string.
- For SQL Server use <https://poorsql.com/>. Edit the default formatter settings
  by checking the "trailing commas" checkbox, and adding `\s\s\s\s` in the "indent string box".
- For MySql use <https://poorsql.com/> with the same configuration as SQL Server (above) and set
  the max line width to 100.

### Code style

We use `dotnet format` to enforce code conventions. It is run automatically in CI, so if you forget your PR cannot be merged.

#### Enforcing code style with git hooks

You can copy paste the following commands to install a git pre-commit hook (creates a pre-commit file in your .git folder, which isn't shown in vs code).  This will cause a commit to fail if you forgot to run `dotnet format`. If you have run on save enabled in your editor this is not necessary.

```bash
cat > .git/hooks/pre-commit << __EOF__
#!/bin/bash
set -euo pipefail

get_files() {
    git diff --cached --name-only --diff-filter=ACMR |\\
        grep '\.cs$'
}

if [ "\$(get_files)" = '' ]; then
    exit 0
fi

get_files |
    xargs dotnet format src/Azure.DataApiBuilder.sln \\
        --verify-no-changes
        --include \\
    || {
        get_files |
            xargs dotnet format src/Azure.DataApiBuilder.sln \\
                --include
        exit 1
}
__EOF__
chmod +x .git/hooks/pre-commit
```

The file should look like this

``` bash
#!/bin/bash
set -euo pipefail

get_files() {
    git diff --cached --name-only --diff-filter=ACMR |  \
        grep '\.cs$'
}

if [ "$(get_files)" = '' ]; then
    exit 0
fi

get_files |
    xargs dotnet format src/Azure.DataApiBuilder.sln \
        --verify-no-changes \
        --include \
    || {
        get_files |
            xargs dotnet format src/Azure.DataApiBuilder.sln \
                --include
        exit 1
}
```
