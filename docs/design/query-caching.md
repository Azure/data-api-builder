
## Why cache?

Reduce the number of round-trip requests between Data API builder (DAB) and the configured back-end database. Reducing the number of round-trip requests results in the end-user experiencing quicker response times.

### Objectives

- Cache responses to read operations
    - REST: GET requests REST
    - GraphQL: Queries
- Make cache configuration simple.
    - Limit developer exposure to implementation details of the cache and only surface essential properties in the runtime configuration.
    - Runtime config properties remain consistent and optional for backwards and future compatibility.
    - Replacing cache implementation (e.g. libraryA to libraryB) does not observably affect developers and their configuration.

### Non-Objectives

-	Follow-up reads on mutations (REST/GraphQL) are not cached.
-	Distributed caching for syncing cache entries across servers for multiple instances of DAB.

## Design

The caching service should be database and endpoint agnostic. DAB will instantiate a singleton caching service that can be used within dependent services. In order to reduce coupling between DAB and a particular caching service implementation, the most generic caching options are publicly exposed in the runtime configuration: cache enablement and cache entry ttl. Additional options specific to the caching service will be explicitly set internally by DAB.

Intentionally surfacing limited configuration options to the developer enables us to __make the caching service function uniformaly and independent of local hosting and SWA hosting configuration.__ This is because the developer exposed configuration properties are applied per-request and unnecessary for configuring the cache service during application startup.

To reduce the sprawl of cache implementation across the code base, we will introduce the caching layer within __a common step of query execution for both REST and GraphQL: the query engine.__ The query engine's responsibility is to build a database query and dispatch the query to the appropriate query executor aligned to the backend database type. 

By keeping the caching layer out of the query executor layer, we eliminate dependencies between database specific query execution logic and caching. Additionally, we prevent adding even tighter coupling between the runtime configuration and query executor because the query executor will not need to reference the runtime config to determine whether caching a request is necessary.

### Cache entry

#### Key

`dataSourceName:queryText:queryParameters`

The following example illustrates a real value created using the key schema listed above (new lines added for readability):

```text
7a07f92a-1aa2-4e2a-81d6-b9af0a25bbb6:

SELECT TOP 1 [dbo_books].[id] AS [id], [dbo_books].[title] AS [title], [dbo_books].[publisher_id] AS [publisher_id] FROM [dbo].[books] AS [dbo_books] WHERE [dbo_books].[id] = @param0 ORDER BY [dbo_books].[id] ASC FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER:

{"@param0":{"Value":1,"DbType":11},"@param1":{"Value":"id","DbType":null},"@param2":{"Value":"title","DbType":null},"@param3":{"Value":"publisher_id","DbType":null}}
```

The goals of the created key:
- Uniquely store query text per data source to avoid collisions.
- Agnostic towards GraphQL or REST request metadata.
- Determinate size based on the size of the string generated.
- Equivalent query metadata will map to the same cache value.
  - This protects against reference type comparison where two distinct C# objects with same property values (request metadata) are treated as two separate cache entries instead of the same entry.

- Rely on the underlying cache implementation to store the key/value pair in a concurrency compatible collection. The implementation defined collection will abstract away the work done to handle cache key hash collisions and appropriate bucketing.
  - Important to note that we can't and won't use a hashcode as the verbatim string for a cache key. Microsoft Docs remarks on default implementation of GetHashCode() say not to use a hashcode as a key to retrieve an object from a keyed collection or to store the value in a database because the value is not “permanent.” https://learn.microsoft.com/en-us/dotnet/api/system.object.gethashcode?view=net-7.0#remarks
  - If in the future we determine that we want to optimize to creating a consistently sized string to provide as a cache key, we can evaluate hashing the string generated. One example would be SHA256 hashing, however sha256 is a crypto operation and may result in a performance degradation that makes caching benefits neglible.

TODO: Does collation affect the cache key?

#### Value

The value stored in the cache is the database response serialized as a JSON string.

The size of the value stored is variable and dependent on the data returned by the database. 

#### Size

The maximum cache size is dependent on the memory (RAM) specifications of the hosting server. The cache will share memory space with the running instance of DAB and other operating system/hosting resources. 
Since key/value pairs are not consistently sized, we can utilize string length (character count) to determine a relative size of the cache entry. 

### Cache Behavior

#### Eviction

Cache entries are evicted when the cache gets close to our predetermined size limit, and when entries expire based on their configured ttl.

#### Updates

Detect if the operation is a follow up read to a mutation. We can then map that follow up read generated query to a cache key entry and attempt to invalidate the cache entry. This would not allow us to expire all variations of read queries generated for the specific entity(s) because querystrings, role based access, and column based access may be cached with different keys. This behavior backs up our guidance to limit caching to rarely changing database objects/records because cache entries may be stale and fed back to the user.

## Security and privacy

### Data residency

__Cache data is stored in-memory__ and remains in memory until evicted due to ttl rules, manual eviction, or DAB process shutdown. Cache data is not persisted to disk.
The cache will store many representations of database data due to the variations of SQL queries generated and executed against the database. Some queries may include or exclude certain database columns depending on the role of the requestor. 

__Authorization configuration does not explicitly affect how the caching service operates__ because all authorization actions execute prior cache interaction. Requests will be rejected due to authorization restrictions prior to cache interaction.

__DAB will return unexpired cache entries when a database goes offline.__ Depending on the developer configured ttl for an entity, the cache entries will persist in an offline database scenario for the ttl defined duration. A cache entry is evicted after the ttl is surpassed.

### Data in transit

__Data returned by the database is received over the same TLS connection established to execute queries.__ The caching service described in this document is an in-memory cache and not a distributed cache. Consequently, the cache does not further transmit data returned by the database, by design. 

### Access token metadata usage

__Cache entries will not be created for database query text generated from database policies.__ This is because database policies directly affect values set within SQL Server's `-set-session-context` variables and is an implementation detail of database specific query executors. The caching service will be implemented independent of and before execution of query executor code. 

Access tokens directly influence how database policies are resolved. Access tokens are also short lived and token contents used to generate a SQL query may change from token to token. This adds an additional burden to the developer to remember to configure their cache entry `ttl` to properly align with access token expiration. We can't know how frequently token contents may change. Cache entries created from token contents are specific to individual users. We should aim to prevent the cache from favoring specific users because we can then avoid have individual users flood the cache for themselves. 

## Configuration Changes

Entity object have the following top-level properties: source, graphql, rest, permissions, relationships, mappings. Caching will introduce a new top-level property for entities to enable entity-level granularity in cache settings.

```json
{
  "entities": {
    "EntityA": {
      "cache": {
        "enabled": true,
        "ttl": 123
      }
    }
  }
}
```

| Property | JSON Data Type | Default Value | Description Value | Required |
|----------|------------|-------------|---------------|----------|
| enabled  | Boolean | false | Whether caching is enabled for read requests on this entity. |  Yes |
| ttl      | Number | 30 | Number of seconds a cache entry is valid before cache eviction. Min: 1s Max: 24hrs/1440s |  Yes |

Entity configuration does not require you to set the **cache** property because caching is disabled by default. When you enable caching explicitly by setting **enabled** to true, you must also define the **ttl** property with an integer value. When evaluating the behavior of caching an entity, you can set the **enabled** property to false instead of removing the entire caching section from the entity's configuration section.

**Note:** You should consider enabling caching on an entity when existing records are rarely modified and the entity is frequently accessed.

While the **ttl** property has a default value of 30 seconds, you should specify a value most applicable for your use case. 
- A higher **ttl** value may be appropriate for entities which are rarely modified and frequently accessed.
- A lower **ttl** value may be appropriate for entities which may change on occassion and are frequentiy accessed. 

## Endpoint Behavior

### GraphQL

**Nested queries** result in joins between multiple database objects which transitively implies joins between multiple entities. Consider the following GraphQL query whose top-level query references the `Book` object type and the nested query references the `Publisher` object type:

```graphql
query NestedEntityQuery{
  book_by_pk (id: 1) {
    id,
    title,
    publishers {
      id,
      name
    }
  }
}
```

Because DAB resolves the request into a single database query, DAB is unable to query the cache for one half the request and query the database for the other half. 
**DAB will only honor the cache configuration for the top-level referenced entity.** The developer will be responsible for ensuring their cache settings are consistently set across related entities to experience caching behavior consistent across entities.

### REST
- Unique cache entries will be created when the following circumstances occur:
  - Variable order of primary keys and values in the URL.
```https
GET https://localhost:<port>/api/Entity/PK_KEY/PK_VAL/PK_KEY2/PK_VAL2
GET https://localhost:<port>/api/Entity/PK_KEY2/PK_VAL2/PK_KEY/PK_VAL
```
  - Variable order of query string parameters and values
```https
GET https://localhost:<port>/api/Entity/id/?$filter=id ne 1 
GET https://localhost:<port>/api/Entity/id/?$filter=1 ne id
```
- The `rest-request-strict` configuration property doesn't affect caching because the property only affects requests with a request body. GET requests validated by DAB to not have request bodies. Additionally, any extraneous properties provided in a PUT, PATCH, or POST request are ignored by DAB.

#### `cache-control` HTTP header behavior

The `cache-control`` header is defined as: 

> The Cache-Control HTTP header field holds directives (instructions) — in both requests and responses — that control caching in browsers and shared caches (e.g. Proxies, CDNs). [developers.mozilla.org](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Cache-Control)

This document's caching design applies to Data API builder at the server level and not how caching is handled at the browser or CDN level. Data API builder's behavior centers around user-defined authorization to limit the scope of data returned by the API. By definition, authenticated and subsequently authorized requests will all contain the `Authorization` HTTP header whose contents consist of the authenticated user's access token. In other words, responses are generated for specific logged-in users which per [RFC9111 - HTTP Caching](https://www.rfc-editor.org/rfc/rfc9111) states the following restriction:

> A shared cache MUST NOT use a cached response to a request with an Authorization header field (Section 11.6.2 of [HTTP]) to satisfy any subsequent request unless the response contains a Cache-Control field with a response directive (Section 5.2.2) that allows it to be stored by a shared cache, and the cache conforms to the requirements of that directive for that response. [Storing Responses to Authenticated Requests](https://www.rfc-editor.org/rfc/rfc9111#name-storing-responses-to-authen)

Due to how cache keys and values are created in the scope of this document, caching Data API builder responses in CDNs and browsers is not in scope. This means that `cache-control` **response** directives are not in scope. While the spec does state that the server can return `cache-control` if desired, DAB performs authentication and authorization on requests prior to accessing the in-memory cache. That same level of validation can't occur in the browser or CDN headers for security purposes. We can validate that ASP.NET core returns `private` or `no-store` as the `cache-control` header value.

- Honor HTTP Header `cache-control` per https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Cache-Control with the following two **request** directives:
  1. `no-cache` - Get a fresh response from the database and updates DAB's cache with the fresh result.
  1. `no-store` - Do not cache the request or response. Do not attempt to fetch a result from the cache.

Note that `cache-control` **request** directives will be ignored by browser and CDN caches when an `Authorization` header is included in the request. 

Mozilla docs https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Cache-Control#preventing_storing suggest that "the most restrictive directive should be honored" when directives conflict. When both `no-cache` and `no-store` are present, `no-store` wins.

## Database object compatibility

| Database Objec Type | Cache compatible?                                            |
|---------------------|--------------------------------------------------------------|
| Table               | Yes                                                          |
| View                | Yes                                                          |
| Stored-Procedure    | Yes, only for stored procedures which query data. See below. |

Stored procure entities must have the following configuration to allow for response caching.

```json
{
  "entityName": {
    "rest": {
      "methods": [
        "GET"
      ]
    },
    "graphql": {
      "operation": "query"
    }
  }
}
```
