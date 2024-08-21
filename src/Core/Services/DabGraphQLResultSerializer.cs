// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.AspNetCore.Serialization;
using HotChocolate.Execution;

namespace Azure.DataApiBuilder.Core.Services;

/// <summary>
/// The DabGraphQLResultSerializer inspects the IExecutionResult created by HotChocolate
/// and determines the appropriate HTTP error code to return based on the errors in the result.
/// By Default, without this serializer, HotChocolate will return a 500 status code when database errors
/// exist. However, there is a specific error code we check for that should return a 400 status code:
/// - DatabaseInputError. This indicates that the client can make a change to request contents to influence
/// a change in the response.
/// </summary>
public class DabGraphQLResultSerializer : DefaultHttpResultSerializer
{
    public override HttpStatusCode GetStatusCode(IExecutionResult result)
    {
        if (result is IQueryResult queryResult && queryResult.Errors?.Count > 0)
        {
            if (queryResult.Errors.Any(error => error.Code == DataApiBuilderException.SubStatusCodes.DatabaseInputError.ToString()))
            {
                return HttpStatusCode.BadRequest;
            }
        }

        return base.GetStatusCode(result);
    }
}
