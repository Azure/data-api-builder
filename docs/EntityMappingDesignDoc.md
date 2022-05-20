## Scope

This change adds mappings for fields that we return to the client on a per entity basis. This mapping is set by the runtime configuration, and allows the user to have the field names of their choice in the request and response map to certain database object names. These mappings must be unique, such that no one name maps to multiple columns, and that no names map to the same column.

The mapping, along with the exposed columns, together makeup the collection of exposed names that can be used to form requests. This can be thought of as a mapping from `Exposed Names` to `Backing Columns`. Where each exposed name has a unique backing column. For example if the set of columns of a given table was {`col1`, `col2`, `col3`}, and the mappiny for the related entity was {`col1` : `col2`}, then the mapping from `Exposed Names` to `Backing Columns` would be {`col2` : `col1`, `col3` : `col3`}, where the names `col2` and `col3` are exposed to the user to make requests with, and `col2` is backed by the actual database column `col1`, and `col3` is backed by the actual database column of the same name, `col3`.

We create this mapping when we instantiate the `RestRequestContext` and also save the mapping in the other direction as well. This let's us easily lookup any entry from the entire set of backing columns, or from the entire set of exposed names, and allows us to get the corresponding backing column or exposed name as well.

Then when we form the `SqlQueryStructure` we only need to be sure that we use the backing column for the column name in the labeled columns that we form, and the exposed name as the actual label. This will then slot directly into our regular code paths for building the query as we desire.

## Class Changes

### RestRequestContext
>When constructing a new request context we create the mappings that we will need and save them as fields in the context. This makes validation and query generation simple.
### RequestValidator.cs
>Add in the additional validation of the request as described above. Using our maps this is easy, since if something doesn't exist in the exposed names key set then it isn't a valid name to request.

### SqlQueryStructure.cs
>Form labeled columns using the backing column as the column name and the exposed name as the label name.
