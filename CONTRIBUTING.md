
## Codestyle

We use `dotnet format` to enforce code conventions. It is run automatically
in CI, so if you forget your PR cannot be merged. You can copy paste the
following commands to install a git pre-commit hook. This will cause a commit to
fail if you forgot to run `dotnet format`. If you have run on save enabled in
your editor this is not necessary.

```bash
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

## Testing

All tests are run in CI, so if they fail your PR will not be merged. Running
tests locally can be useful to debug a failure.

### Running PostgreSql and MsSql tests

The only thing that should different between CI and your own machine is how you
connect to the database that's used for the tests. You should create a custom
overrides file with your connection string:
- `appsettings.MsSqlIntegrationTest.overrides.json` for SQL Server
- `appsettings.PostgreSqlIntegrationTest.overrides.json` for Postgres

There's a template for these files called:
- `appsettings.MsSqlIntegrationTest.overrides.example.json` for SQL Server
- `appsettings.PostgreSqlIntegrationTest.overrides.example.json` for SQL Server

If you copy those files to the path without `example` in it and change the
places where it says `REPLACEME` then you should be able to run the tests
locally without using the following commands:

- `dotnet test --filter "TestCategory=MsSql"` for SQL Server
- `dotnet test --filter "TestCategory=PostgreSql"` for Postgres

### Running CosmosDB tests

TODO

### Running unit tests

TODO


