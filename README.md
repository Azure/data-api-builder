# Project

## Introduction
DataGateway provides a consistent, productive abstraction for building GraphQL and REST API applications with data. Powered by Azure Databases, DataGateway provides modern access patterns to the database, allowing developers to use REST or GraphQL and providing developer experiences that meet developers where they are. 


## Configure and Run

Clone the repository with your prefered method or locally navigate to where you'd like the repository to be and clone with the following command, make sure you replace `<directory name>` 

```
git clone https://github.com/Azure/hawaii-gql.git <directory name>
```

Once you have the respository cloned locally, you will need a database in order to make use of the project. One option is a local instance of SQL Server.

If you have a local instance of SQL Server running, you will need to ensure that your account has permissions on the server. You can add permissions by running the following SQL. Replace DOMAIN\username with your domain and username.

```sql
    CREATE LOGIN [DOMAIN\username] FROM WINDOWS WITH DEFAULT_DATABASE=[master]
    ALTER SERVER ROLE [sysadmin] ADD MEMBER [DOMAIN\username]
```

In order to properly connect to your database you will need to modify the connection string used in the project. Open the project with Visual Studio, or the editor of your choice, and find the files in the `DataGateway.Service` directory of the form `appsettings.XXX.json`

In these files you need to modify the value for `ConnectionString` for the project to be able to connect the service to your database. These connection strings will be specific to the instance of the database that you are running. Example connection strings are provided for assistance.

#### CosmosDB
```
"ConnectionString": "AccountEndpoint=https://cosmostest.documents.azure.com:443/;AccountKey=REPLACEME"
```

#### MsSql
```
"ConnectionString": "Server=tcp:127.0.0.1,1433;Persist Security Info=False;User ID=USERNAME;Password=PASSWORD;MultipleActiveResultSets=False;Connection Timeout=5;"
```

#### PostresSql
```
"ConnectionString": "Host=localhost;Database=graphql"
```

Once you have your connection strings properly formatted you can build and run the project. In Visul Studio this can be done by selecting the type of database you wish to connect when you run build and run the project from within Visual Studio.

To build and run the project from the command line you need to set the Database Type, and then can use the dotnet run command. For example `ASPNETCORE_ENVIRONMENT=PostgreSql dotnet watch run --project DataGateway.Service` would build and run the project for PostregreSql.

When the project finishes building and starts to run there should be a browser that opens with Banana Cake Pop running. You will need to provide the endpoint uri on the left side of the window, which is `https://localhost:5001/graphql`. If the service is running successfully you should see a green dot on that same side of the window with the endpoint's address. From this window you can create and execute queries using GraphQL. For more information on using Bana Cake Pop to test GraphQL queries, please see `https://chillicream.com/docs/bananacakepop`

Likewise, once the project is running you can test the API with a tool like postman (https://www.postman.com/). Files are included that will automatically populate your database with useful tables. The tests that are built into the project use these tables for validation as well. To do so, execute the SQL contained in `MsSqlBooks.sql` located in the `DataGateway.Service` directory.

Please note that if you edit the `MsSqlBooks.sql`file, you need to edit the runtime configuration and graphql schema files accordingly. The runtime configuration file is `sql-config.json` and the GraphQL schema file is `books.gql`


When testing out the API, take note of the service root uri displayed in the window that pops up, ie: `Now listening on: https://localhost:5001`

When manually testing the API with postman, this is the beginning of the uri that will contain your request. You must also include the route, and any desired query strings (for more information on the formatting guidelines we conform to see: https://github.com/microsoft/api-guidelines/blob/vNext/Guidelines.md).

For example, to invoke a FindMany on the Table "Books" and retrieve the "id" and "title" we would have do a GET request on uri `https://localhost:5001/books/?_f=id,title`

To see how the code flows, set a breakpoint in the controller which is associated with the particular DatabaseType that you are using, ie: after line 75 in `RestController.cs`

This is a good entry point for debugging if you are not sure where in the service your problem is located.

### Contributing

If you wish to contribute to this project please see [Contributing.md](https://github.com/Azure/hawaii-gql/blob/main/CONTRIBUTING.md)


## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft
trademarks or logos is subject to and must follow
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
