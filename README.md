# Project

## Introduction

DataGateway provides a consistent, productive abstraction for building GraphQL and REST API applications with data. Powered by Azure Databases, DataGateway provides modern access patterns to the database, allowing developers to use REST or GraphQL and providing developer experiences that meet developers where they are.

## Setup

### 1. Clone Repository

Clone the repository with your preferred method or locally navigate to where you'd like the repository to be and clone with the following command, make sure you replace `<directory name>`

``` bash
git clone https://github.com/Azure/hawaii-gql.git <directory name>
```

### 2. Configure Database Engine

You will need to provide a database to run behind DataGateway. DataGateway supports SQL Server, CosmosDB, PostgreSQL, and MySQL.

#### 2.1 Configure Database Account

With a local or cloud hosted instance a supported database deployed, ensure that you have an account with the necessary access permissions.
The account should have access to all entities that are defined in the runtime configuration.

#### 2.2 Supply a Connection String

Project startup requires a connection string to be defined (**Note:** Dynamic config is out of scope of this initial startup guide).

In your editor of choice, locate template configuration files in the `DataGateway.Service` directory of the form `appsettings.XXX.json`.

Supply a value `ConnectionString` for the project to be able to connect the service to your database. These connection strings will be specific to the instance of the database that you are running. Example connection strings are provided for assistance.

#### MsSql

Local SQL Server Instance

``` c#
"ConnectionString": "Server=tcp:127.0.0.1,1433;Persist Security Info=False;User ID=USERNAME;Password=PASSWORD;MultipleActiveResultSets=False;Connection Timeout=5;"
```

LocalDB Instance

``` c#
"ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=DataGateway;Integrated Security=true"
```

#### MySQL

``` c#
"ConnectionString": "server=localhost;database=graphql;Allow User variables=true;uid=USERNAME;pwd=PASSWORD"
```

#### PostgreSQL

``` c#
"ConnectionString": "Host=localhost;Database=graphql"
```

#### CosmosDB

``` c#
"ConnectionString": "AccountEndpoint=https://<REPLACEME>.documents.azure.com:443/;AccountKey=<REPLACEME>"
```

#### 2.3 Setup Sample Database

Schema and data population files are included that are necessary for running sample queries and unit/integration tests.

**Execute the setup script(s)** located in the `DataGateway.Service` directory for each DB engine installed locally:

- SQL Server/LocalDB `MsSqlBooks.sql`
- PostgreSql `PosgreSqlBooks.sql`
- MySQL `MySqlBooks.sql`

**Note:** Edits to `.sql` files require matching edits to the GraphQL (.gql)schema file and runtime config.

- Runtime config: `sql-config.json`
- GraphQL schema file is `books.gql`

### 3. Configure Authentication

#### Easy Auth

The runtime supports authentication through Static Web Apps/App Service's EasyAuth feature.

An example config for Easy Auth sets the **Provider** value:

```json
  "DataGatewayConfig": {
    "DatabaseType": "DB",
    "ResolverConfigFile": "DB-config.json",
    "DatabaseConnection": {
      "ConnectionString": "<ConnectionString>"
    },
    "Authentication": {
      "Provider": "EasyAuth"
    }
  }
```

HTTP requests must have the `X-MS-CLIENT-PRINCIPAL` HTTP header set with a JWT value. An example value can be found in [this PR #97 Description](https://github.com/Azure/hawaii-gql/pull/97).

#### JWT(Bearer) Authentication

Configure **Bearer token authentication** with identity providers like Azure AD.

```json
  "DataGatewayConfig": {
    "DatabaseType": "DB",
    "ResolverConfigFile": "DB-config.json",
    "DatabaseConnection": {
      "ConnectionString": "<ConnectionString>"
    },
    "Authentication": {
      "Type": "AzureAD",
      "Audience": "<AudienceGUIDfromAppRegistration>",
      "Issuer": "https://login.microsoftonline.com/<tenantID>/v2.0"
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

#### Command Line

1. Set environment variable `ASPNETCORE_ENVIRONMENT` to database type `MsSql`, `PostgreSql`,`Cosmos`, or `MySql`.
   1. Example: `ASPNETCORE_ENVIRONMENT=PostgreSql`
2. Run the command `dotnet watch run --project DataGateway`
   1. watch flag used to detect configuration change and restart.

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

- For example, to invoke a FindMany on the Table "Books" and retrieve the "id" and "title" we would have do a GET request on uri `https://localhost:5001/books/?_f=id,title`

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
