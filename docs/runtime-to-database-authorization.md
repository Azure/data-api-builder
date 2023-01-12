# Runtime to Database Authorization

- [MsSql](#mssql)

## MsSql

### SESSION_CONTEXT

For MsSql, DAB uses SESSION_CONTEXT to send user data to the underlying database. This data is accessible to DAB by virtue of the claims present in the authentication token (JWT/EasyAuth tokens).
The data sent to the database can then be used to configure an additional level of security (eg. by configuring Security policies) to further prevent access
to data in operations like SELECT, UPDATE, DELETE. The data available to the database via the SESSION_CONTEXT will be available till the connection to the
database is alive, i.e. once the connection is closed, SESSION_CONTEXT expires. The same data can be used inside a stored procedure as well. Long story short,
data available via SESSION_CONTEXT can be used anywhere within the lifetime of the connection.

#### How to read from/ write to SESSION_CONTEXT?
Please refer to a very elaborative doc already available [here](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-set-session-context-transact-sql?view=sql-server-ver16).

#### How to enable SESSION_CONTEXT in DAB?
Inside the config file, there is a section `options` inside the `data-source` section. The `options` section holds database specific properties. To enable SESSION_CONTEXT,
the user needs to have the property `set-session-context` set to `true` for MsSql.

#### What is the size limit for SESSION_CONTEXT?
As mentioned [here](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-set-session-context-transact-sql?view=sql-server-ver16#remarks), 
the total size of the session context is limited to 1 MB. If you set a value that causes this limit to be exceeded, the statement fails. 
You can monitor overall memory usage by querying [sys.dm_os_memory_cache_counters](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-os-memory-cache-counters-transact-sql?view=sql-server-ver16) (Transact-SQL) as follows: 
`SELECT * FROM sys.dm_os_memory_cache_counters WHERE type = 'CACHESTORE_SESSION_CONTEXT';`.

#### Example: How to use SESSION_CONTEXT to configure additional level of security (Row Level Security)?
For more details about Row Level Security (RLS), please refer [here](https://learn.microsoft.com/en-us/sql/relational-databases/security/row-level-security?view=sql-server-ver16),
but, basically RLS enables us to use group membership or execution context to control access to rows in a database table.

In this demonstration, we will first be creating a database table `revenues`. We will then configure a [Security Policy](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-security-policy-transact-sql?view=sql-server-ver16) which would add a FILTER PREDICATE
to this `revenues` table. The [FILTER PREDICATE](https://learn.microsoft.com/en-us/sql/relational-databases/security/row-level-security?view=sql-server-ver16#Description) is nothing but a table-valued function which will filter the rows accessible to operations SELECT, UPDATE, DELETE
based on the criteria that is configured for the function.



##### Laying down the ground work for SESSION_CONTEXT- Sql Queries:
We can execute the below SQL queries in the same order via SSMS or any other SQL client to lay the groundwork for SESSION_CONTEXT.

###### Creating revenues table:
CREATE TABLE revenues(
    id int PRIMARY KEY,
    category varchar(max) NOT NULL,
    revenue int,
    accessible_role varchar(max) NOT NULL
);

INSERT INTO revenues(id, category, revenue, accessible_role) VALUES (1, 'Book', 5000, 'Anonymous'), (2, 'Comics', 10000, 'Anonymous'), (3, 'Journals', 20000, 'Authenticated'), (4, 'Series', 40000, 'Authenticated');

###### Creating function to be used as FILTER PREDICATE:
Create a function to be used as a filter predicate by the security policy to restrict access to rows in the table for SELECT,UPDATE,DELETE operations.  
Users with roles(claim value) = @accessible_role(column value) will be able to access a particular row.  
CREATE FUNCTION dbo.revenuesPredicate(@accessible_role varchar(20))
RETURNS TABLE
WITH SCHEMABINDING
AS RETURN SELECT 1 AS fn_securitypredicate_result
WHERE @accessible_role = CAST(SESSION_CONTEXT(N'roles') AS varchar(20)) or (SESSION_CONTEXT(N'roles') is null and @accessible_role='Anonymous');

###### Creating SECURITY POLICY to add to the revenues table:
Adding a security policy which would restrict access to the rows in revenues table for  
SELECT,UPDATE,DELETE operations using the filter predicate dbo.revenuesPredicate.  
CREATE SECURITY POLICY dbo.revenuesSecPolicy 
ADD FILTER PREDICATE dbo.revenuesPredicate(accessible_role) 
ON dbo.revenues;

##### SESSION_CONTEXT in action:
Now that we have laid the groundwork for SESSION_CONTEXT, its time to see it in action.  

###### SCENARIO 1
SELECT * FROM dbo.revenues;  
-- Notice that we have not set the value of the 'roles' key utilised by the filter predicate. It is worth mentioning here  
-- that any key whose value is not specified is assigned a null value).  

###### RESULT
No rows returned by the query as the FILTER PREDICATE returned 0 (false) for each of the row, i.e. none of the row is accessible to the user.  

###### SCENARIO 2
EXEC sp_set_session_context 'roles', 'Anonymous'; -- setting the value of 'roles' key in SESSION_CONTEXT;  
SELECT * FROM dbo.revenues;  

###### RESULT
Rows corresponding to accessible_role = 'Anoymous' are returned as those rows match the criteria of the filter predicate imposed by the security policy.  
