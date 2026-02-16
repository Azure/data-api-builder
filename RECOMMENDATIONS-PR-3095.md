# Recommendations for PR #3095

These are review recommendations for [PR #3095](https://github.com/Azure/data-api-builder/pull/3095) to improve the approach. Copy-paste the instructions below as a comment on PR #3095 for Copilot to apply.

---

## Instructions for Copilot

@copilot Please apply the following improvements to this PR:

### 1. Use `ConstructBaseUriForPagination` instead of calling helpers directly

`SqlPaginationUtil.ConstructBaseUriForPagination` is already `public` and already calls `ResolveRequestScheme` and `ResolveRequestHost` internally. Instead of exposing those private helpers as `internal` and calling them separately, just call `ConstructBaseUriForPagination` directly. This is simpler, keeps the helpers private, and avoids duplicating the `UriHelper.BuildAbsolute` pattern.

### 2. In `SqlMutationEngine.cs` (around line 397-405)

Replace:

```csharp
HttpContext httpContext = GetHttpContext();
// Use scheme/host from X-Forwarded-* headers if present, else fallback to request values
string scheme = SqlPaginationUtil.ResolveRequestScheme(httpContext.Request);
string host = SqlPaginationUtil.ResolveRequestHost(httpContext.Request);
string locationHeaderURL = UriHelper.BuildAbsolute(
        scheme: scheme,
        host: new HostString(host),
        pathBase: GetBaseRouteFromConfig(_runtimeConfigProvider.GetConfig()),
        path: httpContext.Request.Path);
```

With:

```csharp
HttpContext httpContext = GetHttpContext();
string locationHeaderURL = SqlPaginationUtil.ConstructBaseUriForPagination(
        httpContext,
        GetBaseRouteFromConfig(_runtimeConfigProvider.GetConfig()));
```

Also remove the now-unused `using Microsoft.AspNetCore.Http.Extensions;` from the top of `SqlMutationEngine.cs`.

### 3. In `SqlResponseHelpers.cs` (around line 381-390)

Replace:

```csharp
// Use scheme/host from X-Forwarded-* headers if present, else fallback to request values
string scheme = SqlPaginationUtil.ResolveRequestScheme(httpContext.Request);
string host = SqlPaginationUtil.ResolveRequestHost(httpContext.Request);
locationHeaderURL = UriHelper.BuildAbsolute(
                        scheme: scheme,
                        host: new HostString(host),
                        pathBase: baseRoute,
                        path: httpContext.Request.Path);
```

With:

```csharp
locationHeaderURL = SqlPaginationUtil.ConstructBaseUriForPagination(
                        httpContext,
                        baseRoute);
```

Also remove the now-unused `using Microsoft.AspNetCore.Http.Extensions;` from the top of `SqlResponseHelpers.cs`.

### 4. In `SqlPaginationUtil.cs` — Revert visibility changes

Revert `ResolveRequestScheme`, `ResolveRequestHost`, `IsValidScheme`, and `IsValidHost` back to `private` since they are no longer called externally.

### Summary of why this is better

- **No access modifier changes needed**: `ConstructBaseUriForPagination` is already `public`. Keeping the helpers `private` maintains encapsulation.
- **Less code duplication**: The `UriHelper.BuildAbsolute` call pattern is centralized in `ConstructBaseUriForPagination` instead of being repeated.
- **Fewer lines changed**: The PR becomes a net deletion (removing code rather than adding), making it simpler to review.
- **Removes unused imports**: The `using Microsoft.AspNetCore.Http.Extensions` directive (for `UriHelper`) is no longer needed in either file.
