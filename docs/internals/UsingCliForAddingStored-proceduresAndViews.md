# Adding Views/Stored-Procedure to Runtime Config JSON

## New Options
- CLI supports some new command line options (for `add` and `update` command):
  - `--source.type` -> To Specify the type of source (tables, views, and stored-procedure),
  - `--source.params` -> To Specify parameters for Stored Procedure,
  - `--source.key-fields` -> To Specify key-fields for Table/Views.

## Usage
### When `source.type` is not provided
`dab add MyEntity --source "dbo.my_entity" --permissions "anonymous:*"`
NOTE: By Default sourceType is inferred to be table.
> preview:
```json
"MyEntity": {
    "source": "dbo.my_entity",
    "permissions": [
        {
        "role": "anonymous",
        "actions": [ "create", "read", "update" ]
        }
    ]
}
```

### When `source.type` is stored-procedure
`dab add MyEntity --source "s001.book" --source.type "stored-procedure" --source.params "param1:123,param2:hello,param3:true" --permissions "anonymous:*"`
**NOTE**: source-params are optional if the procedure doesn't take any params. 
> preview:
```json
"MyEntity": {
    "source": {
        "type": "stored-procedure",
        "object": "s001.book",
        "parameters": {
            "param1": 123,
            "param2": "hello",
            "param3": true
        }
    },
    "permissions": [
        {
            "role": "anonymous",
            "actions": [ "read" ]
        }
    ]
}
```

### When `source.type` is view
`dab add MyEntity --source "s001.book" --source.type "view" --source.key-fields "col1,col2" --permissions "anonymous:*"`

**NOTE**: key-fields are optional if the view is simple and keys are inferable
> preview:
```json
"MyEntity": {
    "source": {
        "type": "view",
        "object": "s001.book",
        "key-fields": [ "col1", "col2" ]
    },
    "permissions": [
        {
            "role": "anonymous",
            "actions": [ "read" ]
        }
    ]
}
```

## Some unique Cases:
### Conversion from one DatabaseObjectSource to another.
If the source object looks like this:
```json
{
	"object": "bookSp",
	"type": "stored-procedure",
	"parameters": {
		"param1": "hello",
		"param2": 123
	}
}
```

and suppose we want to make it a table, then we need to run this command:
`dab update Book --bookTable --source.type table --source.key-fields "col1,col2"`

Result:
```json
{
	"object": "bookTable",
	"type": "table",
	"key-fields":["col1", "col2"]
}
```
We can convert it back to the original one by the below command:
`dab update Book --source bookSp --source.type stored-procedure --source.parameters "param1:hello,param2:123"`

**NOTE:**
The CLI recognizes redundant fields, i.e., Parameters for table/view, and keyFields for Stored Procedures.
When a user converts a table to a stored procedure, the CLI automatically sets the keyFields property to `null`. Similarly, the CLI sets the Parameters property to `null` when converting from a stored procedure to a table or view. 
When a user explicitly sets the Parameters option while converting from stored-procedure to table/view, the CLI will return an error. The CLI will similarly return an error when a user sets the keyFields option when converting from a table/view to a stored procedure.

### Conversion from object to String.
suppose we want to change the initial storedProcedure object to just a string object.
`dab update Book --source bookTable --source.type table`

Result:
```json
{
	"source": "bookTable"
}
```
**NOTE:**
If the user changes from stored-procedure to table without adding keyfields then it automatically converts the value of `source` to string object from `DatabaseObjectSource` to represent the default source type of table.

When we run the following command to update the above stored-procedure:
`dab update Book --source bookView --source.type view`

Result:
```json
{
	"object": "bookView",
	"type": "view"
}
```
