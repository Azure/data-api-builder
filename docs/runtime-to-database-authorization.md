# Runtime to Database Authorization

- [MsSql](#MsSql)

## MsSql

### SESSION_CONTEXT

For MsSql, DAB uses SESSION_CONTEXT to send user data to the underlying database. This data is accessible to DAB by virtue of the claims present in the authentication token (JWT/EasyAuth tokens).
The data sent to the database can then be used to configure an additional level of security (eg. by configuring Security policies) to further prevent access
to data in operations like SELECT, UPDATE, DELETE. The data available to the database via the SESSION_CONTEXT will be available till the connection to the
database is alive, i.e. once the connection is closed, SESSION_CONTEXT expires. The same data can be used inside a stored procedure as well. Long story short,
data available via SESSION_CONTEXT can be used anywhere within the lifetime of the connection.

#### How to enable SESSION_CONTEXT?
Inside the config file, there is a section `options` inside the `data-source section`. The `options` section holds database specific properties. To enable SESSION_CONTEXT,
the user needs to have the property `set-session-context` set to `true` for MsSql.
