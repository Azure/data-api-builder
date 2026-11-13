# Documentation

Published product documentation is available on [Microsoft Learn](https://learn.microsoft.com/azure/data-api-builder/).

Repository documentation for the Oracle provider is available in [Oracle provider behavior and limitations](oracle.md).

The process for updating Data API builder documentation on Learn is available [here](https://learn.microsoft.com/contribute/)

## SQL pagination and field permissions

SQL-backed REST, GraphQL, and MCP pagination cursors contain sort-field and primary-key
values, including composite-key tie breakers. Base64 encoding is not encryption.
Every field included in a cursor must be readable by the caller's effective role,
even when it was not selected in the request.

If a response would emit a cursor containing a field the role cannot read, the
request fails with an authorization error instead of returning that cursor.
Reads that do not emit a cursor are unaffected. Omitting a permitted field from
the requested projection does not prevent pagination.

To retain pagination without exposing confidential keys, use an entity or database
view with a non-sensitive unique key that the role is allowed to read. Do not grant
read access to a confidential key solely to enable pagination.
