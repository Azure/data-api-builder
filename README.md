# Project

## Introduction
DataGateway provides a consistent, productive abstraction for building GraphQL and REST API applications with data. Powered by Azure Databases, DataGateway provides modern access patterns to the database, allowing developers to use REST or GraphQL and providing developer experiences that meet developers where they are. 


## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

### Build and Run
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

#### MsSql
```
"ConnectionString": "Server=tcp:127.0.0.1,1433;Persist Security Info=False;User ID=USERNAME;Password=PASSWORD;MultipleActiveResultSets=False;Connection Timeout=5;"
```

#### PostresSql
```
"ConnectionString": "Host=localhost;Database=graphql"
```

Once you have your connection strings properly formatted you can build and run the project. In Visul Studio this can be done by selecting the type of database you wish to connect when you run build and run the project from within Visual Studio.

To build and run the project from the command line, first build with project from the root director with `dotnet build Azure.DataGateway.Service.sln --configuration Debug` and then run the project with `dotnet run ...`

Once the project is running you can test the API with a tool like postman (https://www.postman.com/). Files are included that will automatically populate your database with useful tables. The tests that are built into the project use these tables for validation as well. To do so, execute the SQL contained in MsSqlBooks.sql located in the DataGateway.Service directory.

When testing out the API, take note of the service root uri displayed in the window that pops up, ie: `Now listening on: https://localhost:5001`

When manually testing the API with postman, this is the beginning of the uri that will contain your request. You must also include the route, and any desired query strings (for more information on the formatting guidelines we conform to see: https://github.com/microsoft/api-guidelines/blob/vNext/Guidelines.md).

For example, to invoke a FindMany on the Table "Books" and retrieve the "id" and "title" we would have a uri of `https://localhost:5001/books/?_f=id,title`

#### __Codestyle__
We use dotnet format to enforce code conventions. It is run automatically in CI, so if you forget your PR cannot be merged. You can copy paste the following commands to install a git pre-commit hook. This will cause a commit to fail if you forgot to run dotnet format. If you have run on save enabled in your editor this is not necessary.

```
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
    xargs dotnet format Azure.DataGateway.Service.sln \\
        --check \\
        --fix-whitespace --fix-style warn --fix-analyzers warn \\
        --include \\
    || {
        get_files |
            xargs dotnet format Azure.DataGateway.Service.sln \\
                --fix-whitespace --fix-style warn --fix-analyzers warn \\
                --include
        exit 1
}
__EOF__
chmod +x .git/hooks/pre-commit
```

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft
trademarks or logos is subject to and must follow
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
