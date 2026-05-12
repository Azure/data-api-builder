# Hot Chocolate v16 upgrade

This is a high-level companion to PR #3480. The goal here is to give a reader
the *why* behind each non-obvious code change without forcing them to diff Hot
Chocolate v13/v14 against v16 themselves. For the line-level changes, read the
PR.

---

## 1. Package bump

Every `HotChocolate.*` package in `src/Directory.Packages.props` moves to
`16.0.0`. No DAB code is "improved" by the upgrade in isolation — the rest of
this doc explains the API changes that the bump *forced* us to make.

---

## 2. Scalar API rename

HC v16 renamed the scalar override methods. Anything that derived from
`ScalarType<T>` and overrode parsing/serialization had to change names.

| v13 / v14                     | v16                                                                          |
| ----------------------------- | ---------------------------------------------------------------------------- |
| `ParseLiteral(IValueNode)`    | `OnCoerceInputLiteral(IValueNode)`                                           |
| `ParseValue(object)`          | `OnCoerceInputValue(object)`                                                 |
| `Serialize(object)`           | `OnCoerceOutputValue(object)` *(plus `OnValueToLiteral` for literal output)* |

Caller-side helpers also moved: `ParseValue` / `ParseResult` became
`ValueToLiteral`. DAB only owns one custom scalar — `SingleType` in
`src/Service.GraphQLBuilder/CustomScalars/` — which now inherits from
`FloatTypeBase<float>` and uses the new method names.

`ByteArrayType` is `[Obsolete]` in favor of `Base64StringType`. We still need
to pattern-match `ByteArrayType` in the resolver because DAB's generated schema
still binds the GraphQL name `"ByteArray"` to it. Migrating the schema to
emit `"Base64String"` is intentionally **out of scope for this PR** — it's a
second user-visible schema change and deserves its own deprecation cycle.

---

## 3. The `Byte` scalar got split (user-visible)

HC v16 split the legacy `ByteType` into two scalars:

- `ByteType`         — runtime `sbyte`, range -128..127 (now signed)
- `UnsignedByteType` — runtime `byte`,  range 0..255

SQL Server `tinyint` is unsigned (0..255), so the only correct binding for
DAB is `UnsignedByteType`. That means the GraphQL **schema type names** change:

- The scalar exposed in generated schemas: `Byte` → `UnsignedByte`
- The filter input type: `ByteFilterInput` → `UnsignedByteFilterInput`

This is a **breaking change for clients** that hard-code those names in
GraphQL queries or generated client bindings. The runtime values returned to
clients are unchanged. The PR description / release notes call this out.

---

## 4. `TimeSpan` no longer has a dedicated scalar

`TimeSpanType` was removed in v16 in favor of `DurationType` (ISO-8601 on the
wire, parsed via `XmlConvert.ToTimeSpan`). DAB does not bind any column type
to `Duration` today — SQL `time` rides on `TimeOnly` → NodaTime's
`LocalTimeType` — so the `DurationType` arm in `ExecutionHelper.ExecuteLeafField`
is a defensive fallback only. It is symmetric with HC's own serialization,
because HC's `DurationType` produces ISO-8601 on output.

---

## 5. `ISelection` and `Selection.SyntaxNode` changed

Two related changes:

1. `ISelection` was removed; downstream code uses the concrete `Selection`.
2. `Selection.SyntaxNode` (a single `FieldNode`) became
   `Selection.SyntaxNodes` — a `ReadOnlySpan<FieldSelectionNode>`. The span
   shape was introduced so HC can represent **field merging** (multiple syntax
   nodes that resolve to the same selection).

For DAB's purposes, every executable selection always has at least one syntax
node — an empty span is an invariant violation. We added a single helper
to centralize that invariant and convert "empty span" into a targeted
`DataApiBuilderException` rather than an `IndexOutOfRangeException` at the
call site:

```csharp
// src/Service.GraphQLBuilder/SelectionExtensions.cs
public static FieldNode RequireFieldNode(this Selection selection)
```

All previous call sites that did `Selection.SyntaxNode` now do
`selection.RequireFieldNode()`.

---

## 6. `OperationResult.WithContextData` removed

The old fluent `result.WithContextData(...)` is gone. To set context data on
the result you now set `singleResult.ContextData` directly. DAB uses an
`ImmutableDictionary` builder in `DetermineStatusCodeMiddleware` to set
`ExecutionContextData.HttpStatusCode`.

---

## 7. `EnableOneOf` is on by default

We previously had `ModifyOptions(o => o.EnableOneOf = true)`. v16 enables
`@oneOf` by default, so the explicit call is gone. Equivalent behavior, less
code.

---

## 8. `DateTimeType` configuration

`new DateTimeType(disableFormatCheck: true)` is obsolete. The replacement is
`new DateTimeType(new DateTimeOptions { ValidateInputFormat = ... })` and the
polarity is **flipped**: `ValidateInputFormat = true` means strict ISO-8601,
which corresponds to the old `disableFormatCheck = false`.

DAB exposes this via `graphql.enable-legacy-datetime-scalar`. When the flag is
`true` (the historical default that preserves the v13 lenient parser), we pass
`ValidateInputFormat = false` so existing clients with looser DateTime
literals do not break.

There is also a **wire-format change** in HC v16 worth noting because it
surfaced in many tests: HC v16's `DateTimeType` elides trailing zero
fractional seconds on output. `1999-01-08T10:23:54.000Z` is now emitted as
`1999-01-08T10:23:54Z`. The PR updated affected test assertions.

---

## 9. `WithOptions` takes a delegate, and `MapNitroApp` is gone

The endpoint-mapping API moved from an options object to a per-request delegate:

```csharp
// v13 / v14
endpoints.MapGraphQL().WithOptions(new GraphQLServerOptions { ... });
endpoints.MapNitroApp().WithOptions(new GraphQLToolOptions { ... });
```

```csharp
// v16
endpoints.MapGraphQL().WithOptions(options =>
{
    options.Tool.Enable = IsUIEnabled(runtimeConfig, env);
});
```

`MapNitroApp()` was removed entirely. Nitro is now served by the **same**
`/graphql` endpoint and is gated by `options.Tool.Enable` on the per-request
`GraphQLServerOptions`. End-user behavior is preserved: the IDE renders in
development, is hidden in production.

---

## 10. Lazy schema initialization (DAB-specific)

Most important DAB-specific change. HC v16 builds the schema **eagerly** at
host startup by default. DAB has a "hosted" mode where the runtime config is
supplied **after** the host starts — POST `/configuration`. There is no
config at startup, so eager schema construction either:

- builds an empty placeholder schema and serves it indefinitely (because HC
  keeps the warm placeholder executor in the background while the real
  schema's warmup runs after `/configuration` is hit), or
- fails outright if the placeholder fallback is removed.

Setting `options.LazyInitialization = true` (the v15 default) defers schema
construction to the first GraphQL request. By that point the runtime config
is loaded for both file-based startup and the POST `/configuration` path, so
the schema is built from the right metadata.

**Do not remove this flag without re-validating the hosted scenario.**

---

## 11. Test-only changes

Aside from the DateTime literal updates and `Byte` → `UnsignedByte` rename in
filter input names, three test files needed `#nullable enable annotations` at
the top. These files use C# nullable syntax (`Type?`) while their host
project (`Service.Tests`) declares `<Nullable>disable</Nullable>`, which
produces CS8632 warnings. They are not currently treated as errors under
`dotnet build --configuration Debug`, but the pragmas suppress build-log
noise. The change is independent of the HC upgrade; it was rolled in to keep
the build log clean.

Also `MultiSourceQueryExecutionUnitTests` was updated for the v16
`OperationResultData` / `ResultDocument` / `ResultElement` / `ResultProperty`
traversal API, and `TestNoConfigReturnsServiceUnavailable` now recognizes
that lazy `WithOptions` resolution surfaces "no runtime config" as
`DataApiBuilderException(ServiceUnavailable)` rather than a 503 response or
`ApplicationException`.
