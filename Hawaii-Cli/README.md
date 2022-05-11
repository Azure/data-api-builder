# hawaii-cli
CLi tool for hawaii

**Below are the steps to install the tool in your local machine**

1. To update the cli tool trigger name from hawaii to anyother, goto csProj file and update the ToolCommandName accordingly:
```
<PackAsTool>true</PackAsTool>
<ToolCommandName>hawaii</ToolCommandName>
<PackageOutputPath>./nupkg</PackageOutputPath>
```

2. goto your project directory and pack it up.
```
dotnet pack
```

3. Install the tool
```
dotnet tool install --global --add-source ./nupkg hawaii-cli
```

4. after making new changes. do the below steps
a) update the version in *csproj: 
```
    <PropertyGroup>
	  <Version>2.0.0</Version>
	</PropertyGroup>
```	
b) pack the project : `dotnet pack`

c) update the installed tool: 
```
dotnet tool update -g --add-source ./nupkg hawaii-cli --version 2.0.0
```

NOTE
ISSUE: If you see any error while while installing the tool due to some class not found. 
FIX: the gql-engine(hawaii-gql) is probably not linked properly.
Please update the correct path in .csproj file.
```
<ItemGroup>
	<Reference Include="Azure.DataGateway.Config">
		<HintPath>..\hawaii-gql\DataGateway.Config\bin\Debug\net6.0\Azure.DataGateway.Config.dll</HintPath>
	</Reference>
</ItemGroup>
```

TO SHARE THE CHANGES:
1) Once you create the package (dotnet pack). It's ready to be shared.
2) Share the latest package (.nupkg file).
3) To install: `dotnet tool install -g --add-source ./ hawaii-cli --version <<version_number>>`



**To generate the config:**
```
hawaii init -name <<filename>> --database_type <<db_type>> --connection_string <<connection_string>>
```
**To add entity to the config:**
```
hawaii add <<entity>> -source <<source.DB>> --rest <<rest_route>> --graphql <<graphql_type>> --permissions <<rules:actions>>
```
**To update entity to the config:**
```
hawaii update <<entity>> -source <<new_source.DB>> --rest <<new_rest_route>> --graphql <<new_graphql_type>> --permissions <<rules:actions>> --fields.include <<fields to include>> --fields.exclude <<fields to exclude>>
```

		
	
**example:**
```	
hawaii init -n todo-001 --database_type "mysql" --connection_string "localhost:8000"
```	
The Generated config will be in the current directory as todo-001.json
```	
hawaii add todo --source s001.todo --rest todo --graphql todo --permissions "anonymous:*"
```
Entity will be added to the config with given rest route, graphql type and prermissions.
```	
hawaii update todo --permissions "authenticate:create" --fields.include "id,name,category"
```
Entity will be updated in the config with the provided changes.



## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft 
trademarks or logos is subject to and must follow 
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.

