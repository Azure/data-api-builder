# Sample: Data API builder in Aspire

To run this sample, install the prerequisites, then execute the following command in this directory:

```sh
> aspire run
```

### Prerequisites

 - Dotnet CLI v10+ [Install](https://dotnet.microsoft.com/en-us/download/dotnet) Verify with `dotnet --version`
 - Aspire CLI v13+ [Install](https://aspire.dev/get-started/install-cli/) Verify with `aspire --version`
 - Docker Desktop (running) [Install](https://www.docker.com/products/docker-desktop/)

## Resources

```mermaid
flowchart TD
    SQL["SQL Server"]
    DAB["Data API Builder"]
    CMD["SQL Commander"]
    MCP["MCP Inspector"]

    SQL --> DAB
    SQL --> CMD
    DAB --> MCP
```

## Files

- [`apphost.cs`](apphost.cs) - Single-page [Aspire orchestration](https://learn.microsoft.com/en-us/dotnet/aspire/app-host/configuration)
- [`database.sql`](database.sql) - Basic Star Trek schema & seed data
- [`dab-config.json`](dab-config.json) - Standard DAB configuration file
- [`dab-config.cmd`](dab-config.cmd) - [CLI commands](https://learn.microsoft.com/en-us/azure/data-api-builder/command-line/) to create `dab-config.json` (reference only)
- [`global.json`](global.json) - .NET SDK version settings

## Data structures

This schema demonstrates most relationship cardinalities.

```mermaid
stateDiagram-v2
direction LR

  classDef empty fill:none,stroke:none
  classDef table stroke:black;
  classDef phantom stroke:gray,stroke-dasharray:5 5;


  class Actor table
  class Character table
  class Series table
  class Species table
  class Series_Character table
  class Character_Species table
  class Series_Character phantom
  class Character_Species phantom
  state Tables {
    Actor --> Character
    Character --> Actor
    Series_Character --> Series
    Character_Species --> Species
    Series_Character --> Character
    Character_Species --> Character
  }
```