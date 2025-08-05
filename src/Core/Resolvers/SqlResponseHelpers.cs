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
        /// Format the results from a Find operation. Check if there is a requirement
        /// for a nextLink, and if so, add this value to the array of JsonElements to
        /// be used as part of the response.
        /// </summary>
        /// <param name="jsonDoc">The JsonDocument from the query.</param>
        /// <param name="context">The RequestContext.</param>
        /// <param name="sqlMetadataProvider">the metadataprovider.</param>
        /// <param name="runtimeConfig">Runtimeconfig object</param>
        /// <param name="httpContext">HTTP context associated with the API request</param>
        /// <returns>An OkObjectResult from a Find operation that has been correctly formatted.</returns>
        public static OkObjectResult FormatFindResult(
            JsonElement findOperationResponse,
            FindRequestContext context,
            ISqlMetadataProvider sqlMetadataProvider,
            RuntimeConfig runtimeConfig,
            HttpContext httpContext)
        {

            // When there are no rows returned from the database, the jsonElement will be an empty array.
            // In that case, the response is returned as is.
            if (findOperationResponse.ValueKind is JsonValueKind.Array && findOperationResponse.GetArrayLength() == 0)
            {
                return OkResponse(findOperationResponse);
            }

            HashSet<string> extraFieldsInResponse = (findOperationResponse.ValueKind is not JsonValueKind.Array)
                                                  ? DetermineExtraFieldsInResponse(findOperationResponse, context.FieldsToBeReturned)
                                                  : DetermineExtraFieldsInResponse(findOperationResponse.EnumerateArray().First(), context.FieldsToBeReturned);

            uint defaultPageSize = runtimeConfig.DefaultPageSize();
            uint maxPageSize = runtimeConfig.MaxPageSize();

            // If the results are not a collection or if the query does not have a next page
            // no nextLink is needed. So, the response is returned after removing the extra fields.
            if (findOperationResponse.ValueKind is not JsonValueKind.Array || !SqlPaginationUtil.HasNext(findOperationResponse, context.First, defaultPageSize, maxPageSize))
            {
                // If there are no additional fields present, the response is returned directly. When there
                // are extra fields, they are removed before returning the response.
                if (extraFieldsInResponse.Count == 0)
                {
                    return OkResponse(findOperationResponse);
                }
                else
                {
                    return findOperationResponse.ValueKind is JsonValueKind.Array ? OkResponse(JsonSerializer.SerializeToElement(RemoveExtraFieldsInResponseWithMultipleItems(findOperationResponse.EnumerateArray().ToList(), extraFieldsInResponse)))
                                                                                  : OkResponse(RemoveExtraFieldsInResponseWithSingleItem(findOperationResponse, extraFieldsInResponse));
                }
            }

            List<JsonElement> rootEnumerated = findOperationResponse.EnumerateArray().ToList();

            // More records exist than requested, we know this by requesting 1 extra record,
            // that extra record is removed here.
            rootEnumerated.RemoveAt(rootEnumerated.Count - 1);

            // The fields such as primary keys, fields in $orderby clause that are retrieved in addition to the
            // fields requested in the $select clause are required for calculating the $after element which is part of nextLink.
            // So, the extra fields are removed post the calculation of $after element.
            string after = SqlPaginationUtil.MakeCursorFromJsonElement(
                               element: rootEnumerated[rootEnumerated.Count - 1],
                               orderByColumns: context.OrderByClauseOfBackingColumns,
                               primaryKey: sqlMetadataProvider.GetSourceDefinition(context.EntityName).PrimaryKey,
                               entityName: context.EntityName,
                               schemaName: context.DatabaseObject.SchemaName,
                               tableName: context.DatabaseObject.Name,
                               sqlMetadataProvider: sqlMetadataProvider);

            // nextLink is the URL needed to get the next page of records using the same query options
            // with $after base64 encoded for opaqueness

            string path = GetPathForNextLink(httpContext, runtimeConfig.Runtime?.BaseRoute);

            JsonElement nextLink = SqlPaginationUtil.CreateNextLink(
                                  path,
                                  queryStringParameters: context!.ParsedQueryString,
                                  after,
                                  runtimeConfig.NextLinkRelative());

            // When there are extra fields present, they are removed before returning the response.
            if (extraFieldsInResponse.Count > 0)
            {
                rootEnumerated = RemoveExtraFieldsInResponseWithMultipleItems(rootEnumerated, extraFieldsInResponse);
            }

            rootEnumerated.Add(nextLink);
            return OkResponse(JsonSerializer.SerializeToElement(rootEnumerated));
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
        /// <returns>Additional fields that are present in the response</returns>
        private static HashSet<string> DetermineExtraFieldsInResponse(JsonElement response, List<string> fieldsToBeReturned)
        {
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
        /// Helper function returns an OkObjectResult with provided arguments in a
        /// form that complies with vNext Api guidelines.
        /// </summary>
        /// <param name="jsonResult">Value representing the Json results of the client's request.</param>
        /// <returns>Correctly formatted OkObjectResult.</returns>
        public static OkObjectResult OkResponse(JsonElement jsonResult)
        {
            // For consistency we return all values as type Array
            if (jsonResult.ValueKind != JsonValueKind.Array)
            {
                string jsonString = $"[{JsonSerializer.Serialize(jsonResult)}]";
                jsonResult = JsonSerializer.Deserialize<JsonElement>(jsonString);
            }

            List<JsonElement> resultEnumerated = jsonResult.EnumerateArray().ToList();
            // More than 0 records, and the last element is of type array, then we have pagination
            if (resultEnumerated.Count > 0 && resultEnumerated[resultEnumerated.Count - 1].ValueKind == JsonValueKind.Array)
            {
                // Get the nextLink
                // resultEnumerated will be an array of the form
                // [{object1}, {object2},...{objectlimit}, [{nextLinkObject}]]
                // if the last element is of type array, we know it is nextLink
                // we strip the "[" and "]" and then save the nextLink element
                // into a dictionary with a key of "nextLink" and a value that
                // represents the nextLink data we require.
                string nextLinkJsonString = JsonSerializer.Serialize(resultEnumerated[resultEnumerated.Count - 1]);
                Dictionary<string, object> nextLink = JsonSerializer.Deserialize<Dictionary<string, object>>(nextLinkJsonString[1..^1])!;
                IEnumerable<JsonElement> value = resultEnumerated.Take(resultEnumerated.Count - 1);
                return new OkObjectResult(new
                {
                    value = value,
                    @nextLink = nextLink["nextLink"]
                });
            }

            // no pagination, do not need nextLink
            return new OkObjectResult(new
            {
                value = resultEnumerated
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

            // For PUT and PATCH API requests, the users are aware of the Pks as it is required to be passed in the request URL.
            // In case of tables with auto-gen PKs, PUT or PATCH will not result in an insert but error out. Seeing that Location Header does not provide users with
            // any additional information, it is set as an empty string always.
            // For POST API requests, the primary key route calculated will be an empty string in the following scenarions.
            // 1. When read action is not configured for the role.
            // 2. When the read action for the role does not have access to one or more PKs.
            // When the computed primaryKeyRoute is non-empty, the location header is calculated.
            // Location is made up of three parts, the first being constructed from the Host property found in the HttpContext.Request.
            // The second part being the base route configured in the config file.
            // The third part is the computed primary key route.
            if (operationType is EntityActionOperation.Insert && !string.IsNullOrEmpty(primaryKeyRoute))
            {
                locationHeaderURL = UriHelper.BuildAbsolute(
                                        scheme: httpContext.Request.Scheme,
                                        host: httpContext.Request.Host,
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

        /// <summary>
        /// Constructs a validated request URL for next link using the specified <see cref="HttpContext"/> and an optional base route.
        /// </summary>
        /// <remarks>
        /// This method uses the "X-Forwarded-Proto" and "X-Forwarded-Host" headers to determine
        /// the scheme and host of the request, falling back to the request's original scheme and host if the headers
        /// are not present or invalid. The method ensures that the scheme is either "http" or "https" and that the host
        /// is a valid hostname or IP address.
        /// </remarks>
        /// <param name="httpContext">The HTTP context containing the request information.</param>
        /// <param name="baseRoute">An optional base route to prepend to the request path. If not specified, no base route is used.</param>
        /// <returns>A string representing the fully constructed and validated request URL.</returns>
        /// <exception cref="DataApiBuilderException">
        /// Thrown if the scheme specified in the "X-Forwarded-Proto" header is invalid,
        /// or if the host specified in the "X-Forwarded-Host" header is invalid.
        /// </exception>
        private static string GetPathForNextLink(HttpContext httpContext, string? baseRoute = null)
        {
            HttpRequest req = httpContext.Request;

            string scheme = GetValidatedScheme(req);
            string host = GetValidatedHost(req);

            // If the base route is not empty, we need to insert it into the URI before the rest path.
            // Path is of the form ....restPath/pathNameForEntity. We want to insert the base route before the restPath.
            // Finally, it will be of the form: .../baseRoute/restPath/pathNameForEntity.
            return UriHelper.BuildAbsolute(
                scheme: scheme,
                host: new HostString(host),
                pathBase: string.IsNullOrWhiteSpace(baseRoute) ? PathString.Empty : new PathString(baseRoute),
                path: req.Path);
        }

        /// <summary>
        /// Extracts and validates the request scheme from "X-Forwarded-Proto" or falls back to the request scheme.
        /// </summary>
        /// <param name="req">The HTTP request.</param>
        /// <returns>Validated scheme string ("http" or "https").</returns>
        /// <exception cref="DataApiBuilderException">Thrown when client explicitly sets an invalid scheme.</exception>
        internal static string GetValidatedScheme(HttpRequest req)
        {
            string? rawScheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault();
            string? normalized = rawScheme?.Trim().ToLowerInvariant();

            bool isExplicit = !string.IsNullOrEmpty(rawScheme);
            bool isValid = IsValidScheme(normalized);

            if (isExplicit && !isValid)
            {
                // Log a warning and ignore the invalid value, fallback to request's scheme
                Console.WriteLine($"Warning: Invalid scheme '{rawScheme}' in X-Forwarded-Proto header. Falling back to request scheme: '{req.Scheme}'.");
                return req.Scheme;
            }

            return isValid ? normalized! : req.Scheme;
        }

        /// <summary>
        /// Extracts and validates the request host from "X-Forwarded-Host" or falls back to the request host.
        /// </summary>
        /// <param name="req">The HTTP request.</param>
        /// <returns>Validated host string.</returns>
        /// <exception cref="DataApiBuilderException">Thrown when client explicitly sets an invalid host.</exception>
        internal static string GetValidatedHost(HttpRequest req)
        {
            string? rawHost = req.Headers["X-Forwarded-Host"].FirstOrDefault();
            string? trimmed = rawHost?.Trim();

            bool isExplicit = !string.IsNullOrEmpty(rawHost);
            bool isValid = IsValidHost(trimmed);

            if (isExplicit && !isValid)
            {
                // Log a warning and ignore the invalid value, fallback to request's host
                Console.WriteLine($"Warning: Invalid host '{rawHost}' in X-Forwarded-Host header. Falling back to request host: '{req.Host}'.");
                return req.Host.ToString();
            }

            return isValid ? trimmed! : req.Host.ToString();
        }

        /// <summary>
        /// Checks if the provided scheme is valid.
        /// </summary>
        /// <param name="scheme">Scheme, e.g., "http" or "https".</param>
        /// <returns>True if valid, otherwise false.</returns>
        private static bool IsValidScheme(string? scheme)
        {
            return scheme is "http" or "https";
        }

        /// <summary>
        /// Checks if the provided host is a valid hostname or IP address.
        /// </summary>
        /// <param name="host">The host name (with optional port).</param>
        /// <returns>True if valid, otherwise false.</returns>
        private static bool IsValidHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            // Reject dangerous characters
            if (host.Contains('\r') || host.Contains('\n') || host.Contains(' ') ||
                host.Contains('<') || host.Contains('>') || host.Contains('@'))
            {
                return false;
            }

            // Validate host part (exclude port if present)
            string hostnamePart = host.Split(':')[0];

            if (Uri.CheckHostName(hostnamePart) == UriHostNameType.Unknown)
            {
                return false;
            }

            // Final sanity check: ensure it parses into a full URI
            return Uri.TryCreate($"http://{host}", UriKind.Absolute, out _);
        }
    }
}
