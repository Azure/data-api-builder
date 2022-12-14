# What's New in Data API Builder

## Release Notes

The full list of new features and enhancements for the latest version of Data API Builder can be found on the [release notes](https://github.com/Azure/data-api-builder/tags) page.

- [Nested filtering GraphQL support for MsSql](#nested-filtering-graphql-support-for-mssql)
- [AAD User authentication Support for MySQL](#aad-user-authentication-support-for-mysql)
- [Stored Procedures GraphQL Support](#stored-procedures-graphql-support)
- [Managed Identity Support for PostgreSql](#managed-identity-support-for-postgresql)
- [Runtime Configuration Updates in Cosmos DB](#runtime-configuration-updates-in-cosmos-db)

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

### Azure AD User authentication Support for MySQL

Azure AD User authentication is now supported for MySQL enabling the use of a user token as the password field value to authenticate with the MySQL Azure AD plugin.

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

- Reading cosmos options from data-source/options instead of "cosmos" object.
- Honor the database name being set by SWA during runtime configuration.


