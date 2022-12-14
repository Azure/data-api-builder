# What's New in Data API Builder 0.4.10

- [Public JSON Schema](#public-json-schema)
- [Updated `data-source` section for Cosmos DB]
- [`database-type` value renamed for Cosmos DB]
- [Managed Identity now supported with Postgres]
- [AAD User Authentication now supported with MySQL]
- [Support for filter on nested objects within a document in Azure SQL and SQL Server](#support-for-filter-on-nested-objects-within-a-document-in-azure-sql-and-sql-server)

## Public JSON Schema

JSON Schema has been published here:

```text
https://dataapibuilder.blob.core.windows.net/schemas/v0.4.10-alpha/dab.draft.schema.json
```

This will give you intellisense if you are using an IDE, like VS Code, that supports JSON Schemas. Take a look at `basic-empty-dab-config.json` in the `samples` folder, to have a starting point when manually creating the `dab-config.json` file.

Please note that if you are using DAB CLI to create and manage the `dab-config.json` file, DAB CLI is not yet creating the configuration file using the aforementioned reference to the JSON schema file.

## Support for filter on nested objects within a document in Azure SQL and SQL Server

With Azure SQL and SQL Server, you can use the object or array relationship defined in your schema which enables to do filter operations on the nested objects.

```graphql
query {
  books(filter: { series: { name: { eq: "Foundation" } } } ) {
    items {
      title
      year
      pages
    }
  }
}
```

