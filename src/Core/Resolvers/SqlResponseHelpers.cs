// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Class with helper methods that assist with the construction of API response for requests executed against SQL database types.
    /// </summary>
    public class SqlResponseHelpers
    {

        /// <summary>
        /// Format the results from a Find operation. If a nextLink/after token is required for
        /// pagination, the envelope is built directly via an anonymous response object so that
        /// pagination metadata is carried out-of-band rather than encoded into the row collection.
        /// </summary>
        /// <param name="findOperationResponse">The JsonElement from the query (object for single-row, array for collections).</param>
        /// <param name="context">The RequestContext.</param>
        /// <param name="sqlMetadataProvider">The metadataprovider.</param>
        /// <param name="runtimeConfig">Runtimeconfig object</param>
        /// <param name="httpContext">HTTP context associated with the API request</param>
        /// <param name="isMcpRequest"><c>true</c> if invoked from the MCP endpoint (emit <c>after</c>); <c>false</c> or <c>null</c> for REST (emit <c>nextLink</c>).</param>
        /// <returns>An OkObjectResult from a Find operation that has been correctly formatted.</returns>
        public static OkObjectResult FormatFindResult(
            JsonElement findOperationResponse,
            FindRequestContext context,
            ISqlMetadataProvider sqlMetadataProvider,
            RuntimeConfig runtimeConfig,
            HttpContext httpContext,
            bool? isMcpRequest = null)
        {
            // Empty result set: return the standard envelope { "value": [] } and skip extra-field/cursor work.
            if (findOperationResponse.ValueKind is JsonValueKind.Array && findOperationResponse.GetArrayLength() == 0)
            {
                return OkResponse(findOperationResponse);
            }

            bool isCollection = findOperationResponse.ValueKind is JsonValueKind.Array;

            // Compute additional fields that were fetched for cursor/$orderby computation but
            // are not part of $select and so should be stripped from the response payload.
            JsonElement firstRowProbe = isCollection ? findOperationResponse.EnumerateArray().First() : findOperationResponse;
            HashSet<string> extraFieldsInResponse = DetermineExtraFieldsInResponse(firstRowProbe, context.FieldsToBeReturned);

            uint defaultPageSize = runtimeConfig.DefaultPageSize();
            uint maxPageSize = runtimeConfig.MaxPageSize();
            bool hasNext = isCollection && SqlPaginationUtil.HasNext(findOperationResponse, context.First, defaultPageSize, maxPageSize);

            // No-pagination path: single object, or a collection without a next page.
            if (!hasNext)
            {
                if (extraFieldsInResponse.Count == 0)
                {
                    return OkResponse(findOperationResponse);
                }

                return isCollection
                    ? OkResponse(JsonSerializer.SerializeToElement(RemoveExtraFieldsInResponseWithMultipleItems(findOperationResponse.EnumerateArray().ToList(), extraFieldsInResponse)))
                    : OkResponse(RemoveExtraFieldsInResponseWithSingleItem(findOperationResponse, extraFieldsInResponse));
            }

            // Paginated path.
            List<JsonElement> rows = findOperationResponse.EnumerateArray().ToList();

            // More records exist than requested, we know this by requesting 1 extra record,
            // that extra record is removed here.
            rows.RemoveAt(rows.Count - 1);

            // The fields such as primary keys, fields in $orderby clause that are retrieved in addition to the
            // fields requested in the $select clause are required for calculating the $after element which is part of nextLink.
            // So, the extra fields are removed post the calculation of $after element.
            string after = SqlPaginationUtil.MakeCursorFromJsonElement(
                               element: rows[rows.Count - 1],
                               orderByColumns: context.OrderByClauseOfBackingColumns,
                               primaryKey: sqlMetadataProvider.GetSourceDefinition(context.EntityName).PrimaryKey,
                               entityName: context.EntityName,
                               schemaName: context.DatabaseObject.SchemaName,
                               tableName: context.DatabaseObject.Name,
                               sqlMetadataProvider: sqlMetadataProvider);

            // When there are extra fields present, they are removed before returning the response.
            if (extraFieldsInResponse.Count > 0)
            {
                rows = RemoveExtraFieldsInResponseWithMultipleItems(rows, extraFieldsInResponse);
            }

            // MCP endpoint: { value: [...], after: "<cursor>" }
            if (isMcpRequest is true)
            {
                return new OkObjectResult(new
                {
                    value = rows,
                    after = after
                });
            }

            // REST endpoint: { value: [...], nextLink: "<absolute-or-relative-uri>" }
            string basePaginationUri = SqlPaginationUtil.ConstructBaseUriForPagination(httpContext, runtimeConfig.Runtime?.BaseRoute);
            string queryString = SqlPaginationUtil.BuildQueryStringWithAfterToken(
                                     queryStringParameters: context.ParsedQueryString,
                                     newAfterPayload: after);
            UriBuilder uriBuilder = new(basePaginationUri)
            {
                // Form final link by appending the query string
                Query = queryString
            };
            string nextLink = runtimeConfig.NextLinkRelative()
                ? uriBuilder.Uri.PathAndQuery // returns just "/api/<Entity>?$after...", no host
                : uriBuilder.Uri.AbsoluteUri; // returns full URL

            return new OkObjectResult(new
            {
                value = rows,
                nextLink = nextLink
            });
        }

        /// <summary>
        /// To support pagination and $first clause with Find requests, it is necessary to provide the nextLink
        /// field in the response. For the calculation of nextLink, the fields such as primary keys, fields in $orderby clause
        /// are retrieved from the database in addition to the fields requested in the $select clause.
        /// However, these fields are not required in the response.
        /// This function helps to determine those additional fields that are present in the response.
        /// </summary>
        /// <param name="response">Response json retrieved from the database</param>
        /// <param name="fieldsToBeReturned">List of fields to be returned in the response.</param>
        /// <returns>Additional fields that are present in the response. Returns an empty set when <paramref name="response"/> is not a JSON object (e.g. a scalar or array-typed row value), since there are no named properties to filter.</returns>
        private static HashSet<string> DetermineExtraFieldsInResponse(JsonElement response, List<string> fieldsToBeReturned)
        {
            // Guard: a result row is normally a JSON object, but with database engines that can return
            // array/scalar/collection-typed shapes at the row level there is nothing to enumerate. In that
            // case there are no extra-field columns to strip, so return an empty set rather than throwing.
            if (response.ValueKind is not JsonValueKind.Object)
            {
                return new HashSet<string>();
            }

            HashSet<string> fieldsPresentInResponse = new();

            foreach (JsonProperty property in response.EnumerateObject())
            {
                fieldsPresentInResponse.Add(property.Name);
            }

            // context.FieldsToBeReturned will contain the fields requested in the $select clause.
            // If $select clause is absent, it will contain the list of columns that can be returned in the
            // response taking into account the include and exclude fields configured for the entity.
            // So, the other fields in the response apart from the fields in context.FieldsToBeReturned
            // are not required.
            return fieldsPresentInResponse.Except(fieldsToBeReturned).ToHashSet();
        }

        /// <summary>
        /// Helper function that removes the extra fields from each item of a list of json elements.
        /// </summary>
        /// <param name="jsonElementList">List of Json Elements with extra fields</param>
        /// <param name="extraFields">Additional fields that needs to be removed from the list of Json elements</param>
        /// <returns>List of Json Elements after removing the additional fields</returns>
        private static List<JsonElement> RemoveExtraFieldsInResponseWithMultipleItems(List<JsonElement> jsonElementList, IEnumerable<string> extraFields)
        {
            for (int i = 0; i < jsonElementList.Count; i++)
            {
                jsonElementList[i] = RemoveExtraFieldsInResponseWithSingleItem(jsonElementList[i], extraFields);
            }

            return jsonElementList;
        }

        /// <summary>
        /// Helper function that removes the extra fields from a single json element.
        /// </summary>
        /// <param name="jsonElement"> Json Element with extra fields</param>
        /// <param name="extraFields">Additional fields that needs to be removed from the Json element</param>
        /// <returns>Json Element after removing the additional fields</returns>
        private static JsonElement RemoveExtraFieldsInResponseWithSingleItem(JsonElement jsonElement, IEnumerable<string> extraFields)
        {
            JsonObject? jsonObject = JsonObject.Create(jsonElement);

            if (jsonObject is null)
            {
                throw new DataApiBuilderException(
                    message: "While processing your request the server ran into an unexpected error",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            foreach (string extraField in extraFields)
            {
                jsonObject.Remove(extraField);
            }

            return JsonSerializer.SerializeToElement(jsonObject);
        }

        /// <summary>
        /// Helper function returns an OkObjectResult that wraps a single JsonElement (object or array)
        /// into the standard <c>{ "value": [ ... ] }</c> envelope used by REST/MCP responses.
        ///
        /// Pagination metadata (<c>nextLink</c>/<c>after</c>) is intentionally NOT inferred from the
        /// shape of <paramref name="jsonResult"/>. <see cref="FormatFindResult"/> attaches those fields
        /// out-of-band when needed. This avoids confusing array-typed column values (e.g. SQL Server
        /// JSON arrays, vector/collection types) with a pagination sentinel.
        /// </summary>
        /// <param name="jsonResult">Value representing the Json results of the client's request.</param>
        /// <returns>Correctly formatted OkObjectResult.</returns>
        public static OkObjectResult OkResponse(JsonElement jsonResult)
        {
            // For consistency we always return the payload as an array under "value".
            List<JsonElement> rows = jsonResult.ValueKind is JsonValueKind.Array
                ? jsonResult.EnumerateArray().ToList()
                : new List<JsonElement> { jsonResult };

            return new OkObjectResult(new
            {
                value = rows
            });
        }

        /// <summary>
        /// For the given entity, constructs the primary key route
        /// using the primary key names from metadata and their values
        /// from the JsonElement representing the entity.
        /// </summary>
        /// <param name="context">RestRequestContext</param>
        /// <param name="dbOperationResultSet">Result set from the insert/upsert operation</param>
        /// <param name="sqlMetadataProvider">Metadataprovider for db on which to perform operation.</param>
        /// <remarks> When one or more primary keys are not present an empty string will be returned.</remarks>
        /// <returns>the primary key route e.g. /id/1/partition/2 where id and partition are primary keys.</returns>
        public static string ConstructPrimaryKeyRoute(RestRequestContext context, Dictionary<string, object?> dbOperationResultSetRow, ISqlMetadataProvider sqlMetadataProvider)
        {
            if (context.DatabaseObject.SourceType is EntitySourceType.View)
            {
                return string.Empty;
            }

            string entityName = context.EntityName;
            SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);
            StringBuilder newPrimaryKeyRoute = new();

            foreach (string primaryKey in sourceDefinition.PrimaryKey)
            {
                if (!sqlMetadataProvider.TryGetExposedColumnName(entityName, primaryKey, out string? pkExposedName))
                {
                    return string.Empty;
                }

                newPrimaryKeyRoute.Append(pkExposedName);
                newPrimaryKeyRoute.Append("/");

                if (!dbOperationResultSetRow.ContainsKey(pkExposedName))
                {
                    // A primary key will not be present in the upsert/insert operation result set in the following two cases.
                    // 1. The role does not have read action configured
                    // 2. The read action excludes one or more primary keys
                    // In both the cases, an empty location header will be returned eventually and so the primary key route calculation can be short circuited.
                    return string.Empty;
                }

                // This code block is reached after the successful execution of database update/insert operations.
                // So, it is a safe assumption that a non-null value will be present in the result set for a primary key.
                newPrimaryKeyRoute.Append(dbOperationResultSetRow[pkExposedName]!.ToString());
                newPrimaryKeyRoute.Append("/");
            }

            // Remove the trailing "/"
            newPrimaryKeyRoute.Remove(newPrimaryKeyRoute.Length - 1, 1);

            return newPrimaryKeyRoute.ToString();
        }

        /// <summary>
        /// Constructs and returns a HTTP 200 Ok response.
        /// The response is constructed using the results of an upsert database operation when database policy is not defined for the read permission.
        /// If database policy is defined, the results of the subsequent select statement is used for constructing the response.
        /// </summary>
        /// <param name="resultRow">Result of the upsert database operation</param>
        /// <param name="jsonDocument">Result of the select database operation</param>
        /// <param name="isReadPermissionConfiguredForRole">Indicates whether read permissions is configured for the role</param>
        /// <param name="isDatabasePolicyDefinedForReadAction">Indicates whether database policy is configured for read action</param>
        public static OkObjectResult ConstructOkMutationResponse(
            Dictionary<string, object?> resultRow,
            JsonDocument? jsonDocument,
            bool isReadPermissionConfiguredForRole,
            bool isDatabasePolicyDefinedForReadAction)
        {
            using JsonDocument emptyResponseJsonDocument = JsonDocument.Parse("[]");

            // When a database policy is defined for the read action, a subsequent select query in another roundtrip to the database was executed to fetch the results.
            // So, the response of that database query is used to construct the final response to be returned.
            if (isDatabasePolicyDefinedForReadAction)
            {
                return (jsonDocument is not null) ? OkMutationResponse(jsonDocument.RootElement.Clone())
                                                  : OkMutationResponse(emptyResponseJsonDocument.RootElement.Clone());
            }

            // When no database policy is defined for the read action, the result from the upsert database operation is
            // used to construct the final response.
            // When no read permission is configured for the role, or all the fields are excluded
            // an empty response is returned.
            return (isReadPermissionConfiguredForRole && resultRow.Count > 0) ? OkMutationResponse(resultRow)
                                                                              : OkMutationResponse(emptyResponseJsonDocument.RootElement.Clone());
        }

        /// <summary>
        /// Constructs and returns a HTTP 201 Created response.
        /// The response is constructed using results of the insert/upsert(resulting into an insert) database operation when database policy is not defined for the read permission.
        /// If database policy is defined, the results of the subsequent select statement is used for constructing the response.
        /// </summary>
        /// <param name="resultRow">Reuslt of the upsert database operation</param>
        /// <param name="jsonDocument">Result of the select database operation</param>
        /// <param name="primaryKeyRoute">Primary key route to be used in the Location Header</param>
        /// <param name="isReadPermissionConfiguredForRole">Indicates whether read permissions is configured for the role</param>
        /// <param name="isDatabasePolicyDefinedForReadAction">Indicates whether database policy is configured for read action</param>
        /// <param name="operationType">Resultant Operation type - Update, Insert, etc.</param>
        /// <param name="baseRoute">Base Route configured in the config file</param>
        /// <param name="httpContext">HTTP Context associated with the API request</param>
        public static CreatedResult ConstructCreatedResultResponse(
            Dictionary<string, object?> resultRow,
            JsonDocument? jsonDocument,
            string primaryKeyRoute,
            bool isReadPermissionConfiguredForRole,
            bool isDatabasePolicyDefinedForReadAction,
            EntityActionOperation operationType,
            string baseRoute,
            HttpContext httpContext
            )
        {
            string locationHeaderURL = string.Empty;
            using JsonDocument emptyResponseJsonDocument = JsonDocument.Parse("[]");

            // For PUT/PATCH requests where PKs are in the URL, the caller passes operationType as Upsert
            // and primaryKeyRoute as empty, so the Location header is not populated (the client already knows the URL).
            // For keyless PUT/PATCH requests that result in an insert, the caller passes operationType as Insert
            // with a non-empty primaryKeyRoute so the client can discover the newly created resource's location.
            // For POST requests, the primary key route will be empty in the following scenarios:
            // 1. When read action is not configured for the role.
            // 2. When the read action for the role does not have access to one or more PKs.
            // When the computed primaryKeyRoute is non-empty and operationType is Insert, the Location header is populated.
            // Location is made up of three parts: the scheme/host from the request, the base route from config,
            // and the computed primary key route.
            if (operationType is EntityActionOperation.Insert && !string.IsNullOrEmpty(primaryKeyRoute))
            {
                // Use scheme/host from X-Forwarded-* headers if present, else fallback to request values
                string scheme = SqlPaginationUtil.ResolveRequestScheme(httpContext.Request);
                string host = SqlPaginationUtil.ResolveRequestHost(httpContext.Request);
                locationHeaderURL = UriHelper.BuildAbsolute(
                                        scheme: scheme,
                                        host: new HostString(host),
                                        pathBase: baseRoute,
                                        path: httpContext.Request.Path);

                locationHeaderURL = locationHeaderURL.EndsWith('/') ? locationHeaderURL + primaryKeyRoute : locationHeaderURL + "/" + primaryKeyRoute;
            }

            // When the database policy is defined for the read action, a select query in another roundtrip to the database was executed to fetch the results.
            // So, the response of that database query is used to construct the final response to be returned.
            if (isDatabasePolicyDefinedForReadAction)
            {
                return (jsonDocument is not null) ? new CreatedResult(location: locationHeaderURL, OkMutationResponse(jsonDocument.RootElement.Clone()).Value)
                                                  : new CreatedResult(location: locationHeaderURL, OkMutationResponse(emptyResponseJsonDocument.RootElement.Clone()).Value);
            }

            // When no database policy is defined for the read action, the results from the upsert database operation is
            // used to construct the final response.
            // When no read permission is configured for the role, or all the fields are excluded
            // an empty response is returned.
            return (isReadPermissionConfiguredForRole && resultRow.Count > 0) ? new CreatedResult(location: locationHeaderURL, OkMutationResponse(resultRow).Value)
                                                                              : new CreatedResult(location: locationHeaderURL, OkMutationResponse(emptyResponseJsonDocument.RootElement.Clone()).Value);
        }

        /// <summary>
        /// Helper function returns an OkObjectResult with provided arguments in a
        /// form that complies with vNext Api guidelines.
        /// </summary>
        /// <param name="result">Dictionary representing the results of the client's request.</param>
        public static OkObjectResult OkMutationResponse(Dictionary<string, object?>? result)
        {
            // Convert Dictionary to array of JsonElements
            string jsonString = $"[{JsonSerializer.Serialize(result)}]";
            JsonElement jsonResult = JsonSerializer.Deserialize<JsonElement>(jsonString);
            IEnumerable<JsonElement> resultEnumerated = jsonResult.EnumerateArray();

            return new OkObjectResult(new
            {
                value = resultEnumerated
            });
        }

        /// <summary>
        /// Helper function returns an OkObjectResult with provided arguments in a
        /// form that complies with vNext Api guidelines.
        /// The result is converted to a JSON Array if the result is not of that type already.
        /// </summary>
        /// <seealso>https://github.com/microsoft/api-guidelines/blob/vNext/Guidelines.md#92-serialization</seealso>
        /// <param name="jsonResult">Value representing the Json results of the client's request.</param>
        public static OkObjectResult OkMutationResponse(JsonElement jsonResult)
        {
            if (jsonResult.ValueKind != JsonValueKind.Array)
            {
                string jsonString = $"[{JsonSerializer.Serialize(jsonResult)}]";
                jsonResult = JsonSerializer.Deserialize<JsonElement>(jsonString);
            }

            IEnumerable<JsonElement> resultEnumerated = jsonResult.EnumerateArray();

            return new OkObjectResult(new
            {
                value = resultEnumerated
            });
        }
    }
}
