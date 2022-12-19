# What's New in Data API Builder 0.4.11

- [Public JSON Schema](#public-json-schema)
- [Updated JSON schema for `data-source` section](#updated-json-schema-for-data-source-section)
- [Support for filter on nested objects within a document in Azure SQL and SQL Server](#support-for-filter-on-nested-objects-within-a-document-in-azure-sql-and-sql-server)
- [`database-type` value renamed for Cosmos DB](#database-type-value-renamed-for-cosmos-db)
- [Renaming CLI properties for `cosmosdb_nosql`](#renaming-cli-properties-for-cosmosdb_nosql)
- [Managed Identity now supported with Postgres](#managed-identity-now-supported-with-postgres)
- [Support AAD User authentication for Azure MySQL Service](#support-aad-user-authentication-for-azure-mysql-service)

The full list of release notes for this version is available here: [version 0.4.11 release notes](https://github.com/Azure/data-api-builder/releases/tag/v0.4.11-alpha)

Details on how to install the latest version are here: [Installing DAB CLI](./getting-started/getting-started.md#installing-dab-cli)

## Public JSON Schema

JSON Schema has been published here:

```text
https://dataapibuilder.azureedge.net/schemas/v0.4.11-alpha/dab.draft.schema.json
```

This will give you intellisense if you are using an IDE, like VS Code, that supports JSON Schemas. Take a look at `basic-empty-dab-config.json` in the `samples` folder, to have a starting point when manually creating the `dab-config.json` file.

Please note that if you are using DAB CLI to create and manage the `dab-config.json` file, DAB CLI is not yet creating the configuration file using the aforementioned reference to the JSON schema file.

## Updated JSON schema for `data-source` section

The `data-source` section in the configuration file has been updated to be consistent across all supported databases but still allow each database to have custom configurations. A new section `option` has been introduced to group all the properties that are specific to a database. For example:

```json
{
  "$schema": "https://dataapibuilder.azureedge.net/schemas/v0.4.11-alpha/dab.draft.schema.json",
  "data-source": {
    "database-type": "cosmosdb_nosql",
    "options": {
      "database": "PlaygroundDB",
      "graphql-schema": "schema.gql"
    },
    "connection-string": "AccountEndpoint=https://localhost:8081/;AccountKey=REPLACEME;"
  }
}
```

The elements available in the `options` section will depends on the chosen `database-type`.

## Support for filter on nested objects within a document in Azure SQL and SQL Server

With Azure SQL and SQL Server, you can use the object or array relationship defined in your schema which enables to do filter operations on the nested objects.

```graphql
query {
  books(filter: { series: { name: { eq: "Foundation" } } }) {
    items {
      title
      year
      pages
    }
  }
}
```

## `database-type` value renamed for Cosmos DB

We've added support for PostgreSQL API with Cosmos DB. With a consolidated `data-source` section, the attribute `database-type` will denote the type of database. Since Cosmos DB supports multiple APIs, the currently supported database types are 'cosmosdb_nosql' and 'cosmosdb_postgresql'.

```json
  "data-source": {
    "database-type": "cosmosdb_nosql",
    "options": {
      "database": "PlaygroundDB",
      "graphql-schema": "schema.gql"
    }
  }
```

## Renaming CLI properties for `cosmosdb_nosql`

Following the configuration changes described in previous sections, now CLI properties are renamed accordingly as `cosmosdb_nosql-database` and `cosmosdb_nosql-container` for Cosmos DB NoSQL API.

```bash
dab init --database-type "cosmosdb_nosql" --graphql-schema schema.gql --cosmosdb_nosql-database PlaygroundDB  --cosmosdb_nosql-container "books" --connection-string "AccountEndpoint=https://localhost:8081/;AccountKey=REPLACEME;" --host-mode "Development"
```

## Managed Identity now supported with Postgres

Now the user can alternatively specify the access token in the config to authenticate with a Managed Identity. Alternatively, now the user can just not specify the password in the connection string and the runtime will attempt to fetch the default managed identity token. If this fails, connection will be attempted without a password in the connection string.

## Support AAD User authentication for Azure MySQL Service

Added user token as password field to authenticate with MySQL with AAD plugin.