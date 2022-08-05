# Configuration File - MSSQL Specific

## Summary

This files document Azure SQL / SQL Server (`mssql`) specific settings that can be used in Hawaii in the database-specific section.

## Azure SQL and SQL Server Configuration Options

```
"mssql": {
    "set-session-context": true
}
```

`set-session-context` is optional. It is `true` by default and tells Data API builder engine to pass received JWT claims into the [session context](https://docs.microsoft.com/en-us/sql/t-sql/functions/session-context-transact-sql?view=sql-server-ver15). The name of the key containing the claims is `$dab-claims`
