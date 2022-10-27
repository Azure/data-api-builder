# Adding Views/Stored-Procedure to Runtime Config JSON

## New Options
- CLI supports some new command line options (for `add` and `update` command):
- - `--source.type` -> To Specify the type of source (tables, views, and stored-procedure),
- -  `--source.params` -> To Specify parameters for Stored Procedure,
- -  `--source.key-fields` -> To Specify key-fields for Table/Views.

## Sample command
# When source is given just as a string.
`dab add MyEntity --source "dbo.my_entity" --permissions "anonymous:*"`
> preview:
```
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

# When source is a stored-procedure
`dab add MyEntity --source "s001.book" --source.type "stored-procedure" --source.params "param1:123,param2:hello,param3:true" --permissions "anonymous:*"`
> preview:
```
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

# When source is a view
`dab add MyEntity --source "s001.book" --source.type "view" --source.key-fields "col1,col2" --permissions "anonymous:*"`
> preview:
```
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

# Some unique Cases:
## Conversion from one DatabaseObjectSource to another.
If the source object looks like this:
```
{
	"object": "bookSp",
	"type": "stored-procedure",
	"parameters": {
		"param1": "hello",
		"param2": 123
	}
}
```

and suppose we want to make it a table and we run this command:
`dab update Book --bookTable --source.type table --source.key-fields "col1,col2"`
Result:
```
{
	"object": "bookTable",
	"type": "table",
	"key-fields":["col1", "col2"]
}
```
and we convert it back to the original one by the below command:
`dab update Book --source bookSp --source.type stored-procedure --source.parameters "param1:hello,param2:123"`

**NOTE:**
The CLi is smart enough to recognize the non-required field, i.e, Parameters for table/view, and keyFields for Stored Procedures.
So, when a user converts a table to stored-procedure it automatically makes the keyFields `null`, similarly it makes the Parameters null when converting from stored-procedure to table/views. 
If user explicitly gives parameters while converting from stored-procedure to table/view. the CLI will give an error for the same. Same with the keyfields in case of converting from table/view to stored-procedure.

## Conversion from object to String.
suppose we want to change the initial storedProcedure object to just a string object.
`dab update Book --source bookTable --source.type table`
Result:
```
{
	"source": "bookTable"
}
```
**NOTE:**
If the user changes from stored-procedure to table without adding keyfields then it automatically converts it to string object rather than DatabaseObjectSource, as the default type is Table.

what happens if we run the below command for updating the above stored-procedure:
`dab update Book --source bookView --source.type view`
Result:
```
{
	"object": "bookView",
	"type": "view"
}
```
