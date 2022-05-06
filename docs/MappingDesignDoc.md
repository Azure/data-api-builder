## Scope

This change adds mappings for fields that we return to the client on a per entity basis. This mapping is set by the runtime configuration, and allows the user to have the field names of their choice in the request and response map to certain database object names. These mappings must be unique, such that no one name maps to multiple columns, and that no names map to the same column. We also must validate that no keys in the mapping are valid columns and also a field to be returned. The cases then can be described as:
* not a valid column and not in the values of our mapping
* valid column and in the values of the mappings, and column != key in key : value, where value = column. In other words, we allow columns to map their names to themselves, but values can not map to other column names.
* valid column and not in the values of the mappings, but a key in the mapping. In other words, the mapping indicates that this column name should be mapped to a value, yet the column name is the one being used, not the value.

 To implement this change we modify the validation of fields to be returned in the `RequestValidator` from a check if the field exists as a column in the table definition to instead:
* Check if the field is both not in the columns of the `TableDefinition` and not in the values of the entity's mapping. In which case we throw an exception.
* If the above does not throw an exception then check if the field is a key in the mapping, but not a value. In which case we throw an exception.
* Finally we check if the field to be returned is both a key in the mapping, a value in the mapping, and not both the key mapped to a value of itself.

This covers the above cases, since if the field to be returned in both not a column and not a value we have case 1 and can throw. If that passes (since we will OR to the next logical statement) then it must be the case that the field to be returned is either a valid column, in the values, or both. We then check if the field to be returned is key but not a value, in which case we throw an exception. At this point it is possible that the field to be returned is both a key and a value, in which case we will throw unless it is the same key mapped to the same value (ie: "title" : "title").

Once the fields to be returned are validated we know they are either a column with no mapped value, or a mapped value that represents a column. Therefore when we build our structure, for each labeled column that we create, if we build the labeled column from a field to be returned, we will either build the labeled column with the column and label as the same name, or if the field to be returned exists in the mapping's values, then we will create the labeled column with a label as the field to be returned and the column name of the column name from the key in the map.

When we generate these columns in the case where there are no fields to be returned, if there is a mapping that exists, we will use the value as the label.

## Class Changes

### RequestValidator.cs
>Add in the additional validation of the request as described above.

### SqlQueryStructure.cs
>Add in the logic to use the correct label for the code paths used by REST.
