// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Definitions;

internal sealed class ResolverTypeInterceptor : TypeInterceptor
{
    private readonly FieldMiddlewareDefinition _queryMiddleware;
    private readonly FieldMiddlewareDefinition _mutationMiddleware;
    private readonly PureFieldDelegate _leafFieldResolver;
    
    public ResolverTypeInterceptor(ExecutionHelper executionHelper)
    {
        _queryMiddleware = 
            new FieldMiddlewareDefinition(
                next => async context =>
                {
                    await executionHelper.ExecuteMutateAsync(context).ConfigureAwait(false);
                    await next(context).ConfigureAwait(false);
                });
        
        _mutationMiddleware = 
            new FieldMiddlewareDefinition(
                next => async context =>
                {
                    await executionHelper.ExecuteMutateAsync(context).ConfigureAwait(false);
                    await next(context).ConfigureAwait(false);
                });

        _leafFieldResolver = ctx => ExecutionHelper.ExecuteLeafFieldAsync(ctx);
    }
    
    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        DefinitionBase? definition,
        IDictionary<string, object?> contextData)
    {
        // We are only interested in object types here as only object types can have resolvers.
        if (definition is not ObjectTypeDefinition objectTypeDef)
        {
            return;
        }

        if (completionContext.IsQueryType ?? false)
        {
            foreach (ObjectFieldDefinition field in objectTypeDef.Fields)
            {
                field.MiddlewareDefinitions.Add(_mutationMiddleware);
            }
        }
        else if (completionContext.IsMutationType ?? false)
        {
            foreach (ObjectFieldDefinition field in objectTypeDef.Fields)
            {
                field.MiddlewareDefinitions.Add(_queryMiddleware);
            }
        }
        else if (completionContext.IsSubscriptionType ?? false)
        {
            throw new NotSupportedException();
        }
        else
        {
            foreach (ObjectFieldDefinition field in objectTypeDef.Fields)
            {
                // In order to inspect the type we need to resolve the type reference on the definition.
                // If its null or cannot be resolved something is wrong, but we skip over this and let
                // the type validation deal with schema errors.
                if (field.Type is not null &&
                    completionContext.TryGetType(field.Type, out IType? type))
                {
                    if (type.IsLeafType())
                    {
                        field.PureResolver = _leafFieldResolver;
                    }
                    else if()
                }
                
            }
        }
    }
}
