# hawaii-cli
CLi tool for hawaii

Below are the steps to install the tool in your local machine

1. goto csProj file and add the below 3 lines:
<PackAsTool>true</PackAsTool>
<ToolCommandName>hawaii</ToolCommandName>
<PackageOutputPath>./nupkg</PackageOutputPath>

2. goto your project directory and pack it up.
dotnet pack

3. Install the tool
dotnet tool install --global --add-source ./nupkg hawaii-cli


4. after making new changes. do the below steps
a) update the version in *csproj: 
	<PropertyGroup>
	  <Version>2.0.0</Version>
	</PropertyGroup>
b) pack the project : dotnet pack
c) update the installed tool: dotnet tool update -g --add-source ./nupkg hawaii-cli --version 2.0.0



To generate the config:

hawaii init -name <<filename>> --database_type <<db_type>> --connection_string <<connection_string>>

example:
hawaii init -n todo-001 --database_type "mysql" --connection_string "localhost:8000"
The Generated config will be available in ./generatedConfigs/todo-001.json
