// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors.Definitions;

internal sealed class ResolverTypeInterceptor : TypeInterceptor
{
    private readonly FieldMiddlewareDefinition _queryMiddleware;
    private readonly FieldMiddlewareDefinition _mutationMiddleware;
    private readonly PureFieldDelegate _leafFieldResolver;
    private readonly PureFieldDelegate _objectFieldResolver;
    private readonly PureFieldDelegate _listFieldResolver;

    private ObjectType? _queryType;
    private ObjectType? _mutationType;
    private ObjectType? _subscriptionType;

    public ResolverTypeInterceptor(ExecutionHelper executionHelper)
    {
        _queryMiddleware =
            new FieldMiddlewareDefinition(
                next => async context =>
                {
                    await executionHelper.ExecuteQueryAsync(context).ConfigureAwait(false);
                    await next(context).ConfigureAwait(false);
                });

        _mutationMiddleware =
            new FieldMiddlewareDefinition(
                next => async context =>
                {
                    await executionHelper.ExecuteMutateAsync(context).ConfigureAwait(false);
                    await next(context).ConfigureAwait(false);
                });

        _leafFieldResolver = ctx => ExecutionHelper.ExecuteLeafField(ctx);
        _objectFieldResolver = ctx => executionHelper.ExecuteObjectField(ctx);
        _listFieldResolver = ctx => executionHelper.ExecuteListField(ctx);
    }

    public override void OnAfterResolveRootType(
        ITypeCompletionContext completionContext,
        ObjectTypeDefinition definition,
        OperationType operationType)
    {
        switch (operationType)
        {
            // root types in GraphQL are always object types so we can safely cast here.
            case OperationType.Query:
                _queryType = (ObjectType)completionContext.Type;
                break;
            case OperationType.Mutation:
                _mutationType = (ObjectType)completionContext.Type;
                break;
            case OperationType.Subscription:
                _subscriptionType = (ObjectType)completionContext.Type;
                break;
            default:
                // GraphQL only specifies the operation types Query, Mutation, and Subscription,
                // so this case will never happen.
                throw new NotSupportedException(
                    "The specified operation type is not supported.");
        }
    }

    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        DefinitionBase? definition)
    {
        // We are only interested in object types here as only object types can have resolvers.
        if (definition is not ObjectTypeDefinition objectTypeDef)
        {
            return;
        }

        if (ReferenceEquals(completionContext.Type, _queryType))
        {
            foreach (ObjectFieldDefinition field in objectTypeDef.Fields)
            {
                field.MiddlewareDefinitions.Add(_queryMiddleware);
            }
        }
        else if (ReferenceEquals(completionContext.Type, _mutationType))
        {
            foreach (ObjectFieldDefinition field in objectTypeDef.Fields)
            {
                field.MiddlewareDefinitions.Add(_mutationMiddleware);
            }
        }
        else if (ReferenceEquals(completionContext.Type, _subscriptionType))
        {
            throw new NotSupportedException();
        }
        else
        {
            foreach (ObjectFieldDefinition field in objectTypeDef.Fields)
            {
                if (field.Type is not null &&
                    completionContext.TryGetType(field.Type, out IType? type))
                {
                    // Do not override a PureResolver when one is already set.
                    if (field.PureResolver is not null)
                    {
                        continue;
                    }

                    if (type.IsLeafType())
                    {
                        field.PureResolver = _leafFieldResolver;
                    }
                    else if (type.IsObjectType())
                    {
                        field.PureResolver = _objectFieldResolver;
                    }
                    else if (type.IsListType())
                    {
                        field.PureResolver = _listFieldResolver;
                    }
                }
            }
        }
    }
}
