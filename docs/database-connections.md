# Database connections in Data API Builder

In order to work properly, Data API Builder needs to connect to a target database. To do that a connection string must be supplied in the [configuration file](./configuration-file.md)

## Connection Resiliency

Connection to databases are automatically retried, in case a transient error is trapped. Retry logic uses an Exponential Backoff strategy. Maximum number of retries set to 5. Between every subsequent retry backoff timespan is power(2, retryAttempt). For 1st retry, it is performed after a gap of 2 seconds, 2nd retry after a timespan of 4 seconds, 3rd after 8,......, 5th after 32 seconds.

## Database Specific Details

### Azure SQL & SQL Server

Data API Builder uses the SqlClient library to connect to Azure SQL or SQL Server. A list of all the supported connection string options is available here: [SqlConnection.ConnectionString Property](https://learn.microsoft.com/dotnet/api/system.data.sqlclient.sqlconnection.connectionstring).

Usage of Managed Service Identities (MSI) is also supported. Don't specify and username and password in the connection string, and the DefaultAzureCredential will be used as documented here: [Azure Identity client library for .NET - DefaultAzureCredential](https://learn.microsoft.com/dotnet/api/overview/azure/Identity-readme#defaultazurecredential)

### Cosmos DB

WIP

### PostgreSQL

WIP

### MariaDB

WIP

### MySQL

WIP
