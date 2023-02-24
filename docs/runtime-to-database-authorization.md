# Runtime to Database Authorization

## MsSql

### SESSION_CONTEXT

For MsSql, Data API builder (DAb) uses SESSION_CONTEXT to send user specified metadata to the underlying database. Such metadata is available to DAb by virtue of the claims present in the access token.
The data sent to the database can then be used to configure an additional level of security (e.g. by configuring Security policies) to further prevent access
to data in operations like SELECT, UPDATE, DELETE. The SESSION_CONTEXT data is available to the database for the duration of the database connection until that connection is closed. The same data can be used inside a stored procedure as well.  

#### How to read and write to SESSION_CONTEXT

Learn more about setting SESSION_CONTEXT data from the `sp_set_session_context` [Microsoft Learn article](https://learn.microsoft.com/sql/relational-databases/system-stored-procedures/sp-set-session-context-transact-sql).

#### How to enable SESSION_CONTEXT in DAb

In the config file, the `data-source` section sub-key `options` holds database specific configuration properties. To enable SESSION_CONTEXT, the user needs to set the property `set-session-context` to `true` for MsSql. This can be done while generating the config file via CLI at the first time or can be done later as well by setting the property manually in the config file.

##### CLI command to set the SESSION_CONTEXT

```bash
dab init -c config.json --database-type mssql --connection-string some-connection-string --set-session-context true
```

This will generate the data-source section in config file as follows:

```json
"data-source": {
    "database-type": "mssql",
    "options": {
      "set-session-context": true
    },
    "connection-string": "some-connection-string"
  }
 ```

#### How and what data is sent via SESSION_CONTEXT (Example)

All the claims present in the EasyAuth/JWT token are sent via the SESSION_CONTEXT to the underlying database. A sample decoded EasyAuth token looks like:

```json
{
  "auth_typ": "aad",
  "claims": [
    {
      "typ": "aud",
      "val": "<AudienceID>"
    },
    {
      "typ": "iss",
      "val": "https://login.microsoftonline.com/<TenantID>/v2.0"
    },
    {
      "typ": "iat",
      "val": "1637043209"
    },
    {
      "typ": "nbf",
      "val": "1637043209"
    },
    {
      "typ": "exp",
      "val": "1637048193"
    },
    {
      "typ": "aio",
      "val": "ATQAy/8TAAAAGf/W0I7stMr3YH5iHFvESie38+INPT+Zf/p+ByYjTE5TsfeZud/5gqrpBpC1qUsD"
    },
    {
      "typ": "azp",
      "val": "a903e2e6-fd13-4502-8cae-9e09f86b7a6c"
    },
    {
      "typ": "azpacr",
      "val": "1"
    },
    {
      "typ": "name",
      "val": "Sean"
    },
    {
      "typ": "uti",
      "val": "_sSP3AwBY0SucuqqJyjEAA"
    },
    {
      "typ": "ver",
      "val": "2.0"
    }
  ],
  "name_typ": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
  "role_typ": "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
}
```

All the claims present in the the token are translated into key-value pairs passed via SESSION_CONTEXT query formulated like below:

```sql
EXEC sp_set_session_context 'aud', '<AudienceID>', @read_only = 1;
EXEC sp_set_session_context 'iss', 'https://login.microsoftonline.com/<TenantID>/v2.0', @read_only = 1;
EXEC sp_set_session_context 'iat', '1637043209', @read_only = 1;
EXEC sp_set_session_context 'nbf', '1637043209', @read_only = 1;
EXEC sp_set_session_context 'exp', '1637048193', @read_only = 1;
EXEC sp_set_session_context 'aio', 'ATQAy/8TAAAAGf/W0I7stMr3YH5iHFvESie38+INPT+Zf/p+ByYjTE5TsfeZud/5gqrpBpC1qUsD', @read_only = 1;
EXEC sp_set_session_context 'azp', 'a903e2e6-fd13-4502-8cae-9e09f86b7a6c', @read_only = 1;
EXEC sp_set_session_context 'azpacr', 1, @read_only = 1;
EXEC sp_set_session_context 'name', 'Sean', @read_only = 1;
EXEC sp_set_session_context 'uti', '_sSP3AwBY0SucuqqJyjEAA', @read_only = 1;
EXEC sp_set_session_context 'ver', '2.0', @read_only = 1;
```

#### Example: How to use SESSION_CONTEXT to configure additional level of security (Row Level Security)

For more details about Row Level Security (RLS), please refer this [Microsoft Learn article](https://learn.microsoft.com/sql/relational-databases/security/row-level-security), but, basically RLS enables us to use group membership or execution context to control access to rows in a database table.  

In this demonstration, we will first be creating a database table `revenues`. We will then configure a [Security Policy](https://learn.microsoft.com/sql/t-sql/statements/create-security-policy-transact-sql) which would add a FILTER PREDICATE
to this `revenues` table. The [FILTER PREDICATE](https://learn.microsoft.com/sql/relational-databases/security/row-level-security#Description) is nothing but a table-valued function which will filter the rows accessible to operations SELECT, UPDATE, DELETE
based on the criteria that is configured for the function.  
At the end of the demonstration, we will see that only those rows are returned to the user which match the criteria of the filter predicate imposed by the security policy.  

##### Laying down the ground work for SESSION_CONTEXT- SQL Queries

We can execute the below SQL queries in the same order via SSMS or any other SQL client to lay the groundwork for SESSION_CONTEXT.

###### Creating revenues table

```sql
CREATE TABLE revenues(
    id int PRIMARY KEY,  
    category varchar(max) NOT NULL,  
    revenue int,  
    username varchar(max) NOT NULL  
);  
```

```sql
INSERT INTO revenues(id, category, revenue, username) VALUES  
(1, 'Book', 5000, 'Sean'),  
(2, 'Comics', 10000, 'Sean'),  
(3, 'Journals', 20000, 'Davide'),  
(4, 'Series', 40000, 'Davide');  
```

###### Creating function to be used as FILTER PREDICATE

Create a function to be used as a filter predicate by the security policy to restrict access to rows in the table for SELECT, UPDATE, DELETE operations. We use the variable @username to store the value of the column revenues.username and then filter the rows accessible to the user using the filter predicate with condition: @username = SESSION_CONTEXT(N'name').
  
```sql
CREATE FUNCTION dbo.revenuesPredicate(@username varchar(max))  
RETURNS TABLE  
WITH SCHEMABINDING  
AS RETURN SELECT 1 AS fn_securitypredicate_result  
WHERE @username = CAST(SESSION_CONTEXT(N'name') AS varchar(max));  
```

###### Creating SECURITY POLICY to add to the revenues table

Adding a security policy which would restrict access to the rows in revenues table for SELECT, UPDATE, DELETE operations using the filter predicate dbo.revenuesPredicate.

```sql
CREATE SECURITY POLICY dbo.revenuesSecPolicy 
ADD FILTER PREDICATE dbo.revenuesPredicate(username)  
ON dbo.revenues;  
```

##### SESSION_CONTEXT in action

Now that we have laid the groundwork for SESSION_CONTEXT, it's time to see it in action.  

```sql
EXEC sp_set_session_context 'name', 'Sean'; -- setting the value of 'name' key in SESSION_CONTEXT;  
SELECT * FROM dbo.revenues;  
```

###### Result

Rows corresponding to `username` = 'Sean' are returned as only these rows match the criteria of the filter predicate imposed by the security policy.

> [!Note]
> If `SESSION_CONTEXT` is not set, any key referenced in `SESSION_CONTEXT` is assigned a null value, and no errors are raised.  
> In this example, no rows would have been returned by the query as the filter predicate would have returned 0 (false) for each of the row, i.e. none of the row is accessible to the user.  
