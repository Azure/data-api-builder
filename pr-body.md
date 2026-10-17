## Why make this change?

- Closes #3856
  - A 19-digit BIGINT primary key is written into the pagination cursor as a double, so the next-page `$after` token becomes scientific notation (for example `5.123692796229828E+18`) and `ParseParamAsSystemType` returns 400 instead of the next page.
  - Issue: https://github.com/Azure/data-api-builder/issues/3856

## What is this change?

- `SqlPaginationUtil.TryResolveJsonElementToScalarVariable` (used by `MakeCursorFromJsonElement`) now resolves JSON numbers as `Int64` first, then as an integral `decimal` when the value is outside `Int64`, and only then as `double`.
  - That keeps BIGINT cursor values exact so `BaseSqlQueryStructure.ParseParamAsSystemType` can parse them as `Int64`.
  - Ordinary ints and fractional numbers still resolve to values that parse as `int`/`double` unchanged.

## How was this tested?

- [ ] Integration Tests
- [x] Unit Tests

Before (unmodified `TryResolveJsonElementToScalarVariable`, new tests only):

```
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2
Assert.AreEqual failed. Expected:<5123692796229827916 (System.Int64)>. Actual:<5.123692796229828E+18 (System.Double)>.
Assert.AreEqual failed. Expected:<9007199254740993 (System.Int64)>. Actual:<9007199254740992 (System.Double)>.
```

After the fix:

```
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28
  (SqlPaginationUtilUnitTests)

Passed!  - Failed:     0, Passed:    57, Skipped:     0, Total:    57
  (SqlPaginationUtilUnitTests + BaseSqlQueryStructureHelperTests + SqlResponseHelpersUnitTests)
```

`dotnet format src/Azure.DataApiBuilder.sln --verify-no-changes` completed with no formatting changes.

## Sample Request(s)

- REST first page (entity with a 19-digit BIGINT PK):

```http
GET /api/JotFormSubmission?$first=3&$select=JTF_SUB_ID
```

- Follow the returned `nextLink` (or pass `$after` from that response). The cursor `FieldValue` stays `5123692796229827916` and page 2 returns 200 with the next rows instead of 400.

```http
GET /api/JotFormSubmission?$first=3&$select=JTF_SUB_ID&$after=<cursor-from-page-1>
```

- GraphQL equivalent: page with `first` / `after` on the same BIGINT key. MCP `read_records` using that `after` cursor follows the same path.

This change was prepared with AI assistance and reviewed by the author.
