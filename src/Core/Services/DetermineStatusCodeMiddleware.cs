// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Net;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Execution;

/// <summary>
/// The VerifyResultMiddleware inspects the IExecutionResult created by HotChocolate
/// and determines the appropriate HTTP error code to return based on the errors in the result.
/// By Default, without this serializer, HotChocolate will return a 500 status code when database errors
/// exist. However, there is a specific error code we check for that should return a 400 status code:
/// - DatabaseInputError. This indicates that the client can make a change to request contents to influence
/// a change in the response.
/// </summary>
public sealed class DetermineStatusCodeMiddleware(RequestDelegate next)
{
    private const string ERROR_CODE = nameof(DataApiBuilderException.SubStatusCodes.DatabaseInputError);

    public async ValueTask InvokeAsync(IRequestContext context)
    {
        await next(context).ConfigureAwait(false);

        if (context.Result is OperationResult { Errors.Count: > 0 } singleResult)
        {
            if (singleResult.Errors.Any(static error => error.Code == ERROR_CODE))
            {
                ImmutableDictionary<string, object?>.Builder contextData =
                    ImmutableDictionary.CreateBuilder<string, object?>();

                if (singleResult.ContextData is not null)
                {
                    contextData.AddRange(singleResult.ContextData);
                }

                contextData[WellKnownContextData.HttpStatusCode] = HttpStatusCode.BadRequest;
                context.Result = singleResult.WithContextData(contextData.ToImmutable());
            }
        }
    }
}
