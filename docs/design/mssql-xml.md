# SQL Server XML Support

DAB represents SQL Server `xml` values as JSON strings and GraphQL `String`
values. SQL NULL is returned as JSON/GraphQL null. This applies to configured
table entities, automatically discovered entities, and stored-procedure XML
parameters and result columns. SQL Server validates XML, including XML schema
collections associated with typed XML columns.

Explicitly configured tables with XML columns could already be queried before
this change. The new runtime type mapping enables XML stored-procedure metadata
and autoentity discovery; the supported-type constants alone do not enable a
runtime feature.

## Query Limitations

SQL Server cannot compare or order XML values, use XML with LIKE, group by XML,
or apply distinct aggregation to XML. Use `isNull` in GraphQL or `eq null` /
`ne null` in REST/MCP filters. Filtering, sorting, and grouping on other supported
columns in an entity containing XML remain available.

The current GraphQL schema still exposes string comparison/pattern filters,
ordering, and scalar/grouping fields for XML. These are not supported XML
operations, even though they are present in the generated schema. REST and MCP
likewise do not perform XML-specific capability validation before querying SQL.
SQL errors 305, 8116, and 8117 are now classified as bad requests instead of
internal server errors. GraphQL returns `DatabaseInputError`; MCP read/aggregate
tools use their existing bad-request error envelopes rather than an HTTP status.
This is an error-handling improvement, not support for these SQL operations.

A follow-up should introduce a SQL-type capability for non-comparable XML and
native JSON columns: restrict GraphQL filters to null checks, omit unsupported
ordering/grouping fields, and reject unsupported REST/MCP operations before SQL
execution. Native JSON error handling is also discussed in
[issue #3752](https://github.com/Azure/data-api-builder/issues/3752).

## Input And Output Text

XML input is bound as a UTF-16 .NET string. Omit an XML encoding declaration or
declare UTF-16. An `encoding="utf-8"` declaration conflicts with the bound input
and SQL Server rejects it with error 9402. DAB does not strip declarations or
rewrite XML supplied by callers.

For error 9402, DAB includes actionable guidance in the error response and
query-execution logs: `XML encoding declaration conflicts with UTF-16 input.
Omit the XML encoding declaration or use encoding="utf-16".` Development mode
also retains the original SQL diagnostic. This guidance concerns the XML
declaration, not the HTTP request encoding.

SQL Server does not preserve the original XML text. It can discard declarations
and insignificant whitespace, normalize attribute quotes, and collapse empty
elements. Compare XML structurally rather than hashing or comparing the original
input string.

Table mutation results are cast to `NVARCHAR(MAX)` in SQL so their XML strings
match subsequent table reads, including empty-element formatting. Stored
procedures control their own result sets: a native XML result passes through
SqlClient and can have different empty-element whitespace. A procedure requiring
the SQL string representation can explicitly return
`CAST(@doc AS NVARCHAR(MAX))` instead.

## Error Classification

XML parsing errors 9400-9480, depth error 6335, inline-DTD error 6359, and typed
XML validation errors 6908/6926 return HTTP 400 / `DatabaseInputError` in REST,
and `DatabaseInputError` in GraphQL and MCP create/update errors. Production
responses use the safe message `Invalid XML value supplied for an xml field.`;
error 9402 instead uses the encoding guidance above. Development responses
retain SQL Server's detailed diagnostic.

Classification is based on the SQL error number, not on the provenance of the
value. A view or procedure that converts invalid server-stored text to XML can
therefore return a client-input error even when the request supplied no XML.
The analogous native JSON mapping has the same limitation. Such an error does
not establish that the caller supplied the invalid data.