// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Core.Resolvers
{
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
        /// <param name="httpContextAccessor"></param>
        /// <param name="runtimeConfigProvider">fetch the base route configured.</param>
        /// <returns>An OkObjectResult from a Find operation that has been correctly formatted.</returns>
        public static OkObjectResult FormatFindResult(JsonDocument jsonDoc, FindRequestContext context, ISqlMetadataProvider sqlMetadataProvider, RuntimeConfig runtimeConfig, HttpContext httpContext)
        {
            JsonElement jsonElement = jsonDoc.RootElement.Clone();

            // When there are no rows returned from the database, the jsonElement will be an empty array.
            // In that case, the response is returned as is.
            if (jsonElement.ValueKind is JsonValueKind.Array && jsonElement.GetArrayLength() == 0)
            {
                return OkResponse(jsonElement);
            }

            HashSet<string> extraFieldsInResponse = (jsonElement.ValueKind is not JsonValueKind.Array)
                                                  ? DetermineExtraFieldsInResponse(jsonElement, context)
                                                  : DetermineExtraFieldsInResponse(jsonElement.EnumerateArray().First(), context);

            // If the results are not a collection or if the query does not have a next page
            // no nextLink is needed. So, the response is returned after removing the extra fields.
            if (jsonElement.ValueKind is not JsonValueKind.Array || !SqlPaginationUtil.HasNext(jsonElement, context.First))
            {
                // If there are no additional fields present, the response is returned directly. When there
                // are extra fields, they are removed before returning the response.
                if (extraFieldsInResponse.Count == 0)
                {
                    return OkResponse(jsonElement);
                }
                else
                {
                    return jsonElement.ValueKind is JsonValueKind.Array ? OkResponse(JsonSerializer.SerializeToElement(RemoveExtraFieldsInResponseWithMultipleItems(jsonElement.EnumerateArray().ToList(), extraFieldsInResponse)))
                                                                        : OkResponse(RemoveExtraFieldsInResponseWithSingleItem(jsonElement, extraFieldsInResponse));
                }
            }

            List<JsonElement> rootEnumerated = jsonElement.EnumerateArray().ToList();

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
            string path = UriHelper.GetEncodedUrl(httpContext!.Request).Split('?')[0];

            // If the base route is not empty, we need to insert it into the URI before the rest path.
            string? baseRoute = runtimeConfig.Runtime.BaseRoute;
            if (!string.IsNullOrWhiteSpace(baseRoute))
            {
                HttpRequest request = httpContext!.Request;

                // Path is of the form ....restPath/pathNameForEntity. We want to insert the base route before the restPath.
                // Finally, it will be of the form: .../baseRoute/restPath/pathNameForEntity.
                path = UriHelper.BuildAbsolute(
                    scheme: request.Scheme,
                    host: request.Host,
                    pathBase: baseRoute,
                    path: request.Path);
            }

            JsonElement nextLink = SqlPaginationUtil.CreateNextLink(
                                  path,
                                  nvc: context!.ParsedQueryString,
                                  after);

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
        /// <param name="context">FindRequestContext for the GET request.</param>
        /// <returns>Additional fields that are present in the response</returns>
        private static HashSet<string> DetermineExtraFieldsInResponse(JsonElement response, FindRequestContext context)
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
            return fieldsPresentInResponse.Except(context.FieldsToBeReturned).ToHashSet();
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

            IEnumerable<JsonElement> resultEnumerated = jsonResult.EnumerateArray();
            // More than 0 records, and the last element is of type array, then we have pagination
            if (resultEnumerated.Count() > 0 && resultEnumerated.Last().ValueKind == JsonValueKind.Array)
            {
                // Get the nextLink
                // resultEnumerated will be an array of the form
                // [{object1}, {object2},...{objectlimit}, [{nextLinkObject}]]
                // if the last element is of type array, we know it is nextLink
                // we strip the "[" and "]" and then save the nextLink element
                // into a dictionary with a key of "nextLink" and a value that
                // represents the nextLink data we require.
                string nextLinkJsonString = JsonSerializer.Serialize(resultEnumerated.Last());
                Dictionary<string, object> nextLink = JsonSerializer.Deserialize<Dictionary<string, object>>(nextLinkJsonString[1..^1])!;
                IEnumerable<JsonElement> value = resultEnumerated.Take(resultEnumerated.Count() - 1);
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
    }
}
