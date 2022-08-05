## Introduction

This project welcomes contributions and suggestions. Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the Microsoft Open Source Code of Conduct. For more information see the Code of Conduct FAQ or contact opencode@microsoft.com with any additional questions or comments.


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