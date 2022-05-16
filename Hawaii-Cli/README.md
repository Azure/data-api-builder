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
You can install the CLI using `yarn` by running the following command. This will add the `graphql` binary to your path.

```sh
yarn global add graphql-cli
```

The equivalent npm global install will also work.


## Usage / Initialization

### Generate the config:
```
hawaii init -name <<filename>> --database_type <<db_type>> --connection_string <<connection_string>>
```
### Add entity to the config:
```
hawaii add <<entity>> -source <<source.DB>> --rest <<rest_route>> --graphql <<graphql_type>> --permission <<rules:actions>>
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
Entity will be added to the config with given rest route, graphql type and prermissions.
```	
hawaii update todo --permission "authenticate:create" --fields.include "id,name,category"
```
Entity will be updated in the config with the provided changes.

Generate config with some permissions and relationship
```
hawaii init --name todo-005 --database-type mssql --connection-string ""

hawaii add todo --name todo-005 --source s005.todos --permission "authenticated:*" 

hawaii add user --name todo-005 --source s005.users --permission "authenticated:*" 

hawaii add category --name todo-005 --source s005.categories  --permission "authenticated:read"

hawaii update category --name todo-005 --graphql category

hawaii update category --name todo-005 --relationship todos --target.entity todo --cardinality many --mapping.fields "id:category_id" 

hawaii update todo --name todo-005 --relationship category --target.entity category --cardinality one --mapping.fields "category_id:id" 

hawaii update user --name todo-005 --relationship owns --target.entity todo --cardinality many --mapping.fields "id:owner_id" 

hawaii update todo --name todo-005 --relationship owner --target.entity user --cardinality one --mapping.fields "owner_id:id"
 
```

## Contributing

Please read through the [contributing guidelines](./CONTRIBUTING.md)