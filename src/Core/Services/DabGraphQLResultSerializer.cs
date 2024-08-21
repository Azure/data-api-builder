// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.AspNetCore.Serialization;
using HotChocolate.Execution;

namespace Azure.DataApiBuilder.Core.Services;

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
