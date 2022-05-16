# Contributing guide

1. To update the CLI tool trigger name from hawaii to any other, goto csProj file and update the ToolCommandName :
```
<PackAsTool>true</PackAsTool>
<ToolCommandName>hawaii</ToolCommandName>
<PackageOutputPath>./nupkg</PackageOutputPath>
```

2. Goto your project directory and pack it up.
```
dotnet pack
```

3. Install the tool
```
dotnet tool install --global --add-source ./nupkg hawaii-cli
```

4. After making new changes. Do the below steps

	a) update the version in *csproj: 
	```
    <PropertyGroup>
	  <Version>2.0.0</Version>
	</PropertyGroup>
	```	
	b) pack the project : 
	```
	dotnet pack
	```
	c) update the installed tool: 
	```
	dotnet tool update -g --add-source ./nupkg hawaii-cli --version 2.0.0
	```

### NOTE 
If you see any error while installing the tool due to some class not found. 

### FIX: 
Check if gql-engine(hawaii-gql) is probably not linked properly and update the correct path in .csproj file.
```
<ItemGroup>
	<Reference Include="Azure.DataGateway.Config">
		<HintPath>..\hawaii-gql\DataGateway.Config\bin\Debug\net6.0\Azure.DataGateway.Config.dll</HintPath>
	</Reference>
</ItemGroup>
```

## Share the changes
1) Once you create the package (dotnet pack). It's ready to be shared.
2) Share the latest package (.nupkg file).