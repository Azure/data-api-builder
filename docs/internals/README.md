# Project

## Introduction

Data API builder for Azure Databases provides a consistent, productive abstraction for building GraphQL and REST API applications with data. Powered by Azure Databases, Data API builder provides modern access patterns to the database, allowing developers to use REST or GraphQL and providing developer experiences that meet developers where they are.

## Setup

### 1. Clone Repository

**Runtime Engine**:

Clone the repository with your preferred method or locally navigate to where you'd like the repository to be and clone with the following command, make sure you replace `<directory name>`

```bash
git clone https://github.com/Azure/hawaii-engine.git <directory name>
```

**CLI-tool**:

To make any changes to the CLI tool, clone the Hawaii-Cli repository: https://github.com/Azure/hawaii-cli.git

For Installation of CLI tool, Refer [README:HAWAII-CLI](https://github.com/Azure/hawaii-cli#readme)


### 2. Configure Database Engine

You will need to provide a database to run behind DataGateway. DataGateway supports SQL Server, CosmosDB, PostgreSQL, and MySQL.

#### 2.1 Configure Database Account

With a local or cloud hosted instance a supported database deployed, ensure that you have an account with the necessary access permissions.
The account should have access to all entities that are defined in the runtime configuration.

#### 2.2 Supply a `connection-string` for the respective `database-type`

Project startup requires a config that can be generated using dab.

##### Use Cli-tool to Generate the config
Below command will let you generate the config file with the required database-type and connection-string (**Note:** --name denotes name of the generated config, do not add extension).
```
dab init --name dab-config.XXX --database-type <<DBTYPE>> --connection-string <<CONNSTRING>>
```

In your editor of choice, you can locate template configuration files in the `DataGateway.Service` directory of the form `dab-config.XXX.json`.

Supply a value `connection-string` for the project to be able to connect the service to your database. These connection strings will be specific to the instance of the database that you are running. Example connection strings are provided for assistance.

#### MsSql

Local SQL Server Instance

```json
"data-source": {
  "database-type": "mssql",
  "connection-string": "Server=tcp:127.0.0.1,1433;Persist Security Info=False;User ID=USERNAME;Password=PASSWORD;MultipleActiveResultSets=False;Connection Timeout=5;"
}
```

LocalDB Instance

```json
"data-source": {
  "database-type": "mssql",
  "connection-string": "Server=(localdb)\\MSSQLLocalDB;Database=DataGateway;Integrated Security=true"
}
```

#### MySQL

```json
"data-source": {
  "database-type": "mysql",
  "connection-string": "server=localhost;database=graphql;Allow User variables=true;uid=USERNAME;pwd=PASSWORD"
}
```

#### PostgreSQL

```json
"data-source": {
  "database-type": "postgresql",
  "connection-string": "Host=localhost;Database=graphql"
}
```

#### CosmosDB

```json
"data-source": {
  "database-type": "cosmos",
  "connection-string": "AccountEndpoint=https://<REPLACEME>.documents.azure.com:443/;AccountKey=<REPLACEME>"
}
```
The `connection-string` can also be supplied as the value of the environment variable `HAWAII_CONNSTRING`. If set, it will override the `connection-string` value from the config file.

#### 2.3 Setup Sample Database

Schema and data population files are included that are necessary for running sample queries and unit/integration tests.

**Execute the setup script(s)** located in the `DataGateway.Service` directory for each DB engine installed locally:

- SQL Server/LocalDB `MsSqlBooks.sql`
- PostgreSql `PosgreSqlBooks.sql`
- MySQL `MySqlBooks.sql`

**Note:** Edits to `.sql` files require matching edits to the GraphQL (.gql)schema file and the runtime config.

- Runtime config: `dab-config.json`
- GraphQL schema file is `books.gql`
- Resolver config: `sql-config.json`

#### 2.4. Configure Authentication

**Cli-tool**:

When we do `dab init`, it will automatically generate the default Host settings with default authentication(**Note:** --host-mode is an optional flag that takes in the environment: Production/Development. Updating other keys are not supported currently). Below is the default Host setting that is generated with host-mode as Development. by Default authentication-provider will be "EasyAuth".

```json
    "host": {
      "mode": "development",
      "cors": {
        "origins": [],
        "allow-credentials": true
      },
      "authentication": {
        "provider": "EasyAuth",
        "jwt": {
          "audience": "",
          "issuer": "",
          "issuerkey": ""
        }
      }
    }
```

#### Setting up Role and Actions

dab allows us to specify role and actions for every entity using the --permission option. permissions can only be specified with add/update command.
```
dab add <<enity_name>> --source <<xxx>> --permissions "<<role>>:<<actions>>" --fields.include <<a,b,c>> --fields.exclude <<x,y,z>>
```
**NOTE:**
`<<role>>` here can be **anonymous/authenticated**.
`<<action>>` here can be any CRUD operations.(for multiple use ',' seperated values. Use "*" to specify all CRUD actions).

Example:
```
dab add MY_ENTITY -s "my_source" --permissions "anonymous:read"
dab update MY_ENTITY --permissions "authenticated:create,update"
dab update MY_ENTITY --permissions "authenticated:delete" --fields.include "*" --fields.exclude "id,date"
```
**Generated Config:**
```
"permissions": [
        {
          "role": "anonymous",
          "actions": [ "read" ]
        },
        {
          "role": "authenticated",
          "actions": [
            "create",
            "update",
            {
              "action": "delete",
              "fields": {
                "include": [ "*" ],
                "exclude": [ "id", "date" ]
              }
            }
          ]
```

#### Easy Auth

The runtime supports authentication through Static Web Apps/App Service's EasyAuth feature.

An example config for Easy Auth sets the **Provider** value:

```json
  "runtime": {
    "host": {
      "authentication": {
        "provider": "EasyAuth"
      }
    }
  }
```

HTTP requests must have the `X-MS-CLIENT-PRINCIPAL` HTTP header set with a JWT value. An example value can be found in [this PR #97 Description](https://github.com/Azure/hawaii-gql/pull/97).

#### JWT(Bearer) Authentication

Configure **Bearer token authentication** with identity providers like Azure AD.

```json
  "data-source": {
  "database-type": "cosmos",
  "connection-string": "AccountEndpoint=https://<REPLACEME>.documents.azure.com:443/;AccountKey=<REPLACEME>"
  },
  "runtime": {
    "host": {
      "authentication": {
        "provider": "AzureAD",
        "jwt": {
          "audience": "<AudienceGUIDfromAppRegistration>",
          "issuer": "https://login.microsoftonline.com/<tenantID>/v2.0"
        }
      }
    }
  }
```



HTTP requests must have the `Authorization` HTTP header set with the value `Bearer <JWT TOKEN>`. The token must be issued and signed for the DataGateway runtime.

### 4. Build and Run

#### Visual Studio

1. Select the **Startup Project** `Azure.DataGateway.Service`.
2. Select a **debug profile** for database type: `MsSql`, `PostgreSql`,`Cosmos`, or `MySql`.
3. Select **Clean Solution**
4. Select **Build Solution** (Do not select rebuild, as any changes to configuration files may not be reflected in the build folder.)
5. Start runtime

#### Local Development

For local development, the required config files can be generated along with the build.

1. The **ConfigGenerators** directory contains the DAB commands for each database type.
2. Update the **connection-string** property of the **init** command.
3. Execute the command `dotnet build -p:generateConfigFiles=true`. This builds the project and generates the config files.
4. The runtime can be run using the above mentioned steps.
5. After performing the code changes, the build can be performed using one of the two ways.
  - If there are changes to the dab commands, execute `dotnet build -p:generateConfigFiles=true`
  - If there are no changes to the dab commands, execute `dotnet build` 

#### Which configuration file is used?

1. DAB runtime determines the name of the configuration file based on environment values, following the same behavior offered by ASP.NET Core for the `appsettings.json` file. It expects the configuration file in the same directory as the runtime.

2. The precedence followed is in the following order from high to low:

    a. Command Line Argument e.g. `--ConfigFileName=custom-config.json`

    b. Value of `DAB_ENVIRONMENT` suffixed to dab-config.
    e.g. setting `DAB_ENVIRONMENT=Development` will prompt the runtime to look for `dab-config.Development.json`

    c. Value of `ASPNETCORE_ENVIRONMENT` suffixed to dab-config.
    e.g. setting `ASPNETCORE_ENVIRONMENT=MsSql` will prompt the runtime to look for `dab-config.MsSql.json`

    d. By default, runtime will look for `dab-config.json`

3. For any of the configuration file names determined for the environment, if there is another file with the `.overrides` suffix in the current directory, that overridden file name will instead be picked up.
e.g. if both `dab-config.json` and `dab-config.overrides.json` are present, precedence will be given to `dab-config.overrides.json` - however, the runtime will still follow the above rules of precedence.
e.g. When DAB_ENVIRONMENT is set as `Development` and if all three config files exist- `dab-config.Development.json`, `dab-config.json`, `dab-config.overrides.json`- the runtime will pick `dab-config.Development.json`.

#### Command Line

1. Based on your preferred mode of specifying the configuration file name, there are different ways to launch the runtime.
2. Set the `DAB_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT`, typically using their value to be database type `MsSql`, `PostgreSql`,`Cosmos`, or `MySql`.

    Example: `ASPNETCORE_ENVIRONMENT=PostgreSql`

3. Run the command `dotnet watch run --project DataGateway`
   - watch flag used to detect configuration change and restart.

4. The runtime config file provided as a command line takes precedence. So, another way of running is:

`dotnet watch run --project DataGateway --ConfigFileName=custom-config.json`

### 5. Query Execution Tools

#### Banana Cake Pop for GraphQL

After startup, a browser window opens with Banana Cake Pop (BCP), the GraphQL request GUI.

- Provide the endpoint URI `https://localhost:5001/graphql`  so the GraphQL schema is read.
  - A green dot in the URI text box will confirm schema detection.

For more information on using Banana Cake Pop to test GraphQL queries, please see `https://chillicream.com/docs/bananacakepop`

#### Postman for REST

You can test the REST API with [Postman](https://www.postman.com/).

When testing out the API, take note of the service root URI displayed in the window that pops up, ie: `Now listening on: https://localhost:5001`

When manually testing the API with postman, this is the beginning of the uri that will contain your request. You must also include the route, and any desired query strings. Request expectations can be found in [Microsoft REST API Guidelines]( https://github.com/microsoft/api-guidelines/blob/vNext/Guidelines.md).

- For example, to invoke a FindMany on the Table "Books" and retrieve the "id" and "title" we would have do a GET request on uri `https://localhost:5001/books/?$select=id,title`

#### Debugging

To see code execution flow, the first place to start would be to set breakpoints in either `GraphQLController.cs` or `RestController.cs`. Those controllers represent the entry point of a GraphQL and REST request, respectively.

## Using Docker Containers

Instructions for using Docker containers can be found under [docs/GetStarted.md](https://github.com/Azure/hawaii-gql/blob/main/docs/GetStarted.md)

### Contributing

If you wish to contribute to this project please see [Contributing.md](https://github.com/Azure/hawaii-gql/blob/main/CONTRIBUTING.md)

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft
trademarks or logos is subject to and must follow
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
