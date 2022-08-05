## Scope

This change adds mappings (which can be thought of as Aliases) for fields that we return to the client on a per entity basis. This mapping is set by the runtime configuration, and allows the user to have the field names of their choice in the request and response map to certain database object names. These mappings must be unique, such that no one name maps to multiple columns, and that no names map to the same column.

The mapping, along with the exposed columns, together makeup the collection of exposed names that can be used to form requests. This can be thought of as a mapping from `Exposed Names` to `Backing Columns`. Where each exposed name has a unique backing column. For example if the set of columns of a given table was {`col1`, `col2`, `col3`}, and the mapping for the related entity was {`col1` : `col2`}, then the mapping from `Exposed Names` to `Backing Columns` would be {`col2` : `col1`, `col3` : `col3`}, where the names `col2` and `col3` are exposed to the user to make requests with, and `col2` is backed by the actual database column `col1`, and `col3` is backed by the actual database column of the same name, `col3`.

We create this mapping on startup because we need to have the mapping in place in order to generate the correct `Edm Model`, which will allow for the parsing of request query strings that include the aliases of the backing columns. We save the mapping in the `SqlMetadataProvider` into a dictionary that will link each entity to its associated mapping. We then create the same for the mapping in the other direction as well. Having both directions let's us easily lookup any entry from either the entire set of backing columns, or from the entire set of exposed names, and allows us to get the corresponding backing column or exposed name as well.

Anytime we process a part of the request which includes potential aliases from this mapping we must be careful to use our mapping from the entity associated with the request to translate the alias into a backing column. For example, if the request includes in its query string a sorted order we must translate the columns requested for sorting into their backing columns.

Then when we form the `SqlQueryStructure` we only need to be sure that we use the backing column for the column name in the labeled columns that we form, and the exposed name as the actual label. This will then slot directly into our regular code paths for building the query as we desire.

## Class Changes

### EdmModelBuilder
>The code paths for building the model now need to include the entire dictionary for entity-->databaseObject mapping rather than just the values that hold all of the databaseObjects. This is to map aliases to actual backing columns on a per entity basis, and therefore we need the associated entity name. We also include the mapping for each entity from Backing Column to Exposed Names. When we create a new entity for the model we ensure that the structural properties that we set have a name that comes from our mapping. These properties represent columns in our model, and so by using the map, we can set the name of the property to the exposed name, which is what the request our model is needed to parse, will contain.

The following is example of how our model looks with respect to the names of the columns in the backend database, the names we store in datagateway as backing columns, and the names we store in datagateway as exposed names.

This example is given to highlight some of the potential pitfalls when dealing with remapping column names.

#### Actual Table Columns
| id | ident | num | number | address
| -- | ----- | --- | ------ | -------
#### Mapping in runtime config
| Backing Column | Exposed Name |
| -------------- | ------------ |
| id             | ident        |
| ident          | identity     |
| num            | number       |

#### Model
| Table Column Name | DG Backing Column | DG Exposed Name |
| ----------------- | ----------------- | --------------- |
| id                | id                | ident           |
| ident             | ident             | identity        |
| num               | num               | number          |
| number            |                   |                 |
| address           | address           | address          |

### FilterParser.cs
>This class is called as a part of model generation and so must pass along the mappings that we need to pull out the correct aliasing.

### ODataASTVisitor.cs
>Added Exposed Names to Backing Columns map to the class as a nullable type, so for REST when this is not null we can use to get the correct column name from the handling of `SingleValueProprtyAccessNode`.

### SqlMetadataProvider.cs
>On startup we initialize two dictionaries that we have added to this class. They each map an entity name to a dictionary which itself maps `Exposed Names` to `Backing Columns`, or `Backing Columns` to `Exposed Names`. Because we have dependancies on this mapping for things like building Edm Models, we have to do this for each entity on startup. When these mapings are built, we give priority to the mapping associated with each entity as defined in the configuration file. We then add in any columns that are not overridden by the mapping with themselves as their value, in each of the dictionaries that we initialize. This gives us the mappings that fully express `Exposed Name` to `Backing Column`, and the reverse. 

### RequestParser.cs
>When we parse the request we must be careful to make sure that we correctly translate the names in the request to the actual backing columns that are used for sorting when we create the order by list.

### RequestValidator.cs
>When validating the `FieldsToBeReturned` we use the mappings that we have generated to verify that the fields all exist in the set of `Exposed Names`. When validating the primary keys in the request we also must check if the provided name exists in the set of `Exposed Names`.

### SqlQueryStructure.cs
>Form labeled columns using the backing column as the column name and the exposed name as the label name. `PopulateParamsAndPredicates` now uses the backing column to use in the query, while using the exposed name to return error information. Within the query, the exposed name is what is used as the column while the exposed name is used as the alias, ie: [column] AS [alias], as can be seen in the following example query using the column names from the example in the model section above.

`SELECT TOP 101 [dbo_example].[id] AS [ident], [dbo_example].[ident] AS [identity], [dbo_example].[num] AS [number], [dbo_example].[address] AS [address] `

`FROM [dbo].[example] AS [dbo_example]`

`WHERE 1 = 1`

`ORDER BY [dbo_example].[id] ASC`

`FOR JSON PATH, INCLUDE_NULL_VALUES`

### SqlPaginationUtil.cs
>When parsing the `After` from the provided JSON string, for REST requests we use the mapping to translate the request name into a `Backing Column`, but use the name from the request in any error information we return.
