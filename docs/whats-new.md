# What's New in Data API Builder

## Version 0.4.10

The full list of release notes for this version is available here: [version 0.4.10 release notes](https://github.com/Azure/data-api-builder/releases/tag/v0.4.10-alpha)

- [Nested filtering GraphQL support for MsSql]
- [AAD User authentication Support for MySQL]
- [Stored Procedures GraphQL Support]
- [Managed Identity Support for PostgreSql]
- [Runtime Configuration Updates in Cosmos DB]

### Nested filtering GraphQL support for MsSql

Added nested filtering support for relational databases:

```sample request
{
    books (filter: {
      authors : {
          books: {
            title: { contains: "Awesome"}
          }
          name: { eq: "Aaron"}
      }
    }) {
      items {
      title
    }
  }
}
```

### AAD User authentication Support for MySQL

AAD User authentication are now supported for MySQL, added user token as password field to authenticate with MySQL with AAD plugin.

### Stored Procedures GraphQL Support

Stored procedures are now supported for GraphQL requests.
For stored procedures Data API Builder will support all CRUD operations:

- GraphQL: queries and mutations
- REST: POST, GET, PUT, PATCH, DELETE methods

It will be up to the developer to limit what of the above operations are applicable to the stored procedure, by correctly configuring the permission associated with the entity connected to the stored procedure. For example, a stored procedure named `get_users` should only support the `read` permission, but that will be up to the developer to correctly configure the configuration file to do so. For example:

```json
"entities": {
    "user": {
        "source": {
        "object": "web.get_users",
        "type": "stored-procedure",
        "parameters": {
            "param1": 123,
            "param2": "hello",
            "param3": true
        }
        },
        "permissions": [
        {
            "actions": [ "read" ],
            "role": "anonymous"
        }
        ]
    }
}
```

### Managed Identity Support for PostgreSql

The user now can specify the access token in the config to authenticate with managed identity. Alternatively, the user now can just not specify the password in the connection string and the runtime will attempt to fetch the default managed identity token. If this fails, connection will be attempted without a password in the connection string.

### Runtime Configuration Updates in Cosmos DB

- Reading comsos options from data-source/options instead of "cosmos" object.
- Honor the database name being set by SWA during runtime configuration.


