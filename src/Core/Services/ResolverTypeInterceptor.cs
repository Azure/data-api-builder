// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors.Configurations;

internal sealed class ResolverTypeInterceptor : TypeInterceptor
{
    private readonly FieldMiddlewareConfiguration _queryMiddleware;
    private readonly FieldMiddlewareConfiguration _mutationMiddleware;
    private readonly PureFieldDelegate _leafFieldResolver;
    private readonly PureFieldDelegate _objectFieldResolver;
    private readonly PureFieldDelegate _listFieldResolver;

    private ObjectType? _queryType;
    private ObjectType? _mutationType;
    private ObjectType? _subscriptionType;

    public ResolverTypeInterceptor(ExecutionHelper executionHelper)
    {
        _queryMiddleware =
            new FieldMiddlewareConfiguration(
                next => async context =>
                {
                    await executionHelper.ExecuteQueryAsync(context).ConfigureAwait(false);
                    await next(context).ConfigureAwait(false);
                });

        _mutationMiddleware =
            new FieldMiddlewareConfiguration(
                next => async context =>
                {
                    await executionHelper.ExecuteMutateAsync(context).ConfigureAwait(false);
                    await next(context).ConfigureAwait(false);
                });

        _leafFieldResolver = ExecutionHelper.ExecuteLeafField;
        _objectFieldResolver = executionHelper.ExecuteObjectField;
        _listFieldResolver = executionHelper.ExecuteListField;
    }

    public override void OnAfterResolveRootType(
        ITypeCompletionContext completionContext,
        ObjectTypeConfiguration definition,
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
        TypeSystemConfiguration? definition)
    {
        // We are only interested in object types here as only object types can have resolvers.
        if (definition is not ObjectTypeConfiguration objectTypeConfig)
        {
            return;
        }

        if (ReferenceEquals(completionContext.Type, _queryType))
        {
            foreach (ObjectFieldConfiguration field in objectTypeConfig.Fields)
            {
                field.MiddlewareConfigurations.Add(_queryMiddleware);
            }
        }
        else if (ReferenceEquals(completionContext.Type, _mutationType))
        {
            foreach (ObjectFieldConfiguration field in objectTypeConfig.Fields)
            {
                field.MiddlewareConfigurations.Add(_mutationMiddleware);
            }
        }
        else if (ReferenceEquals(completionContext.Type, _subscriptionType))
        {
            throw new NotSupportedException();
        }
        else
        {
            foreach (ObjectFieldConfiguration field in objectTypeConfig.Fields)
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
