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



**To generate the config:**
```
hawaii init -name <<filename>> --database_type <<db_type>> --connection_string <<connection_string>>
```
**To add entity to the config:**
```
hawaii add <<entity>> -source <<source.DB>> --rest <<rest_route>> --graphql <<graphql_type>> --permissions <<rules:actions>>
```
		
	
**example:**
```	
hawaii init -n todo-001 --database_type "mysql" --connection_string "localhost:8000"
```	
The Generated config will be available in ./generatedConfigs/todo-001.json
```	
hawaii add todo --source s001.todo --rest todo --graphql todo --permissions "anonymous:*"
```
Entity will be added to the config with given rest route, graphql type and prermissions.	
