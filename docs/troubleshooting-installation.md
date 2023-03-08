# Troubleshoot Data API builder installation

Data API builder is distributed as a NuGet package and installed via .NET tool as described in the [Install Data API builder](./getting-started/getting-started.md) article. This article provides solutions to common problems that might arise when you're installing Data API builder.

## Installing .NET 6 on your machine

Data API builder requires .NET 6 to be installed on your machine. If you don't have .NET 6 installed, you can install it by following the instructions in the [.NET 6 installation guide](https://learn.microsoft.com/dotnet/core/install/).

### Installing .NET 6 on Ubuntu 22

Installing .NET 6 on Ubuntu 22 can be tricky as the .NET package are available both in the Ubuntu repo and also in the Microsoft repo, which can lead to conflicts. If you're having issues, and when executing `dotnet` on Linux if you have error like `A fatal error occurred. The folder [/usr/share/dotnet/host/fxr] does not exist`  make sure to take a look at this post on StackOverflow: [How to install .NET 6 on Ubuntu 22](https://stackoverflow.com/questions/73753672/a-fatal-error-occurred-the-folder-usr-share-dotnet-host-fxr-does-not-exist?answertab=scoredesc#tab-top).

## Using .NET tool

Data API builder is distributed as a NuGet package and can be installed using the `dotnet tool` command. If you're having issues using `dotnet tool`, take a look at [Troubleshoot .NET tool usage issues](https://learn.microsoft.com/dotnet/core/tools/troubleshoot-usage-issues)

## Data API builder CLI

### `dab` command can't be found

Make sure that the folder where .NET tool stores the downloaded package is in your PATH variable: [Global tools](https://learn.microsoft.com/dotnet/core/tools/troubleshoot-usage-issues#global-tools)

```powershell
($env:PATH).Split(";")
```

or

```bash
echo $PATH | tr ":" "\n"
```
