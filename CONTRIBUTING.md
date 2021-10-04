
## Codestyle

We use `dotnet format` to enforce code conventions. It is run in automatically
in CI, so if you forget your PR cannot be merged. You can copy paste the
following commands to install a git pre-commit hook. This will cause a commit to
fail if you forgot to run `dotnet format`. If you have run on save enabled in
your editor this is not necessary.

```bash
cat > .git/hooks/pre-commit << __EOF__
#!/bin/bash
set -euo pipefail

get_files() {
    git diff --cached --name-only --diff-filter=ACMR Cosmos.GraphQL.Service |\
        grep '\.cs$' |\
        sed s=^Cosmos.GraphQL.Service/==g
}

if [ "$(get_files)" = '' ]; then
    exit 0
fi

get_files |
    xargs dotnet format Cosmos.GraphQL.Service/Cosmos.GraphQL.Service.sln \
        --check \
        --fix-whitespace --fix-style warn --fix-analyzers warn \
        --include \
    || {
        get_files |
            xargs dotnet format Cosmos.GraphQL.Service/Cosmos.GraphQL.Service.sln \
                --fix-whitespace --fix-style warn --fix-analyzers warn \
                --include
        exit 1
}
__EOF__
chmod +x .git/hooks/pre-commit
```
