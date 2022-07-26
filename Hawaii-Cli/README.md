# Hawaii CLI

Command line tool for Hawaii development workflows built on
C#

 - Helpful commands to improve your workflows
   	1.Initialize the configuration.
   	2.Add new Entities.
   	3.Update Entity Details
   	4.Add/Update relationship between entities.

 - Let the user to run locally in any environment
 
## Install
If you have the .nuget package, run the below command to install directly:
```
dotnet tool install -g --add-source ./ hawaii-cli --version <<version_number>>
```

Else, you can go to the root directory of the project and create your own nuget package and then install:
```
dotnet pack
dotnet tool install -g --add-source ./nupkg hawaii-cli --version <<version_number>>
```
### Mac Issue:
Sometimes, On macOS when a .Net tool is installed globally, it will not be found in the PATH. It might give an error saying, **"hawaii not found"**.
To Fix execute the below command:
```
export PATH=$PATH:~/.dotnet/tools
```

## Usage / Initialization

### Generate the config:
```
hawaii init --name <<filename>> --database-type <<db_type>> --connection-string <<connection_string>>
```
### Add entity to the config:
```
hawaii add <<entity>> -source <<source.DB>> --rest <<rest_route>> --graphql <<graphql_type>> --permissions <<rules:actions>>
```
### Update entity to the config:
```
hawaii update <<entity>> -source <<new_source.DB>> --rest <<new_rest_route>> --graphql <<new_graphql_type>> --permissions <<rules:actions>> --fields.include <<fields to include>> --fields.exclude <<fields to exclude>>
```

## Example
```	
hawaii init -n todo-001 --database_type "mysql" --connection_string "localhost:8000"
```	
The Generated config will be in the current directory as todo-001.json
```	
hawaii add todo --source s001.todo --rest todo --graphql todo --permission "anonymous:*"
```
Entity will be added to the config with given rest route, graphql type and permissions.
```	
hawaii update todo --permission "authenticate:create" --fields.include "id,name,category"
```
Entity will be updated in the config with the provided changes.

Generate config with some permissions and relationship
```
hawaii init --name todo-005 --database-type mssql --connection-string ""

hawaii add todo --name todo-005 --source s005.todos --permissions "authenticated:*" 

hawaii add user --name todo-005 --source s005.users --permissions "authenticated:*" 

hawaii add category --name todo-005 --source s005.categories  --permissions "authenticated:read"

hawaii update category --name todo-005 --graphql category

hawaii update category --name todo-005 --relationship todos --target.entity todo --cardinality many --relationship.fields "id:category_id"

hawaii update todo --name todo-005 --relationship category --target.entity category --cardinality one --relationship.fields "category_id:id"

hawaii update user --name todo-005 --relationship owns --target.entity todo --cardinality many --relationship.fields "id:owner_id"

hawaii update todo --name todo-005 --relationship owner --target.entity user --cardinality one --relationship.fields "owner_id:id"
 
```

## Contributing

Please read through the [contributing guidelines](./CONTRIBUTING.md)
