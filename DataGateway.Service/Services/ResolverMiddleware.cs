using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Resolvers;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataGateway.Services
{
    /// <summary>
    /// Interface for ResolverMiddleware factory
    /// </summary>
    public interface IResolverMiddlewareMaker
    {

        /// <summary>
        /// Creates a ResolverMiddleware with the FieldDelegate next
        /// </summary>
        public ResolverMiddleware MakeWith(FieldDelegate next);
    }

    /// <summary>
    /// The resolver middleware that is used by the schema executor to resolve
    /// the queries and mutations
    /// </summary>
    public abstract class ResolverMiddleware
    {
        internal readonly FieldDelegate _next;
        internal readonly IQueryEngine _queryEngine;
        internal readonly IMutationEngine _mutationEngine;
        internal readonly IMetadataStoreProvider _metadataStoreProvider;

        public ResolverMiddleware(FieldDelegate next,
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            IMetadataStoreProvider metadataStoreProvider)
        {
            _next = next;
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _metadataStoreProvider = metadataStoreProvider;
        }

        public ResolverMiddleware(FieldDelegate next)
        {
            _next = next;
        }

        public abstract Task InvokeAsync(IMiddlewareContext context);

        protected static bool TryGetPropertyFromParent(IMiddlewareContext context, out JsonElement jsonElement)
        {
            JsonDocument result = context.Parent<JsonDocument>();
            if (result == null)
            {
                jsonElement = default;
                return false;
            }

            return result.RootElement.TryGetProperty(context.Selection.Field.Name.Value, out jsonElement);
        }

        protected static bool IsInnerObject(IMiddlewareContext context)
        {
            return context.Selection.Field.Type.IsObjectType() && context.Parent<JsonDocument>() != default;
        }

        static private object ArgumentValue(IValueNode value)
        {
            if (value.Kind == SyntaxKind.IntValue)
            {
                IntValueNode intValue = (IntValueNode)value;
                return intValue.ToInt64();
            }
            else
            {
                return value.Value;
            }
        }

        public static IDictionary<string, object> GetParametersFromSchemaAndQueryFields(IObjectField schema, FieldNode query)
        {
            IDictionary<string, object> parameters = new Dictionary<string, object>();

            // Fill the parameters dictionary with the default argument values
            IFieldCollection<IInputField> availableArguments = schema.Arguments;
            foreach (IInputField argument in availableArguments)
            {
                if (argument.DefaultValue == null)
                {
                    parameters.Add(argument.Name.Value, null);
                }
                else
                {
                    parameters.Add(argument.Name.Value, ArgumentValue(argument.DefaultValue));
                }
            }

            // Overwrite the default values with the passed in arguments
            IReadOnlyList<ArgumentNode> passedArguments = query.Arguments;
            foreach (ArgumentNode argument in passedArguments)
            {
                parameters[argument.Name.Value] = ArgumentValue(argument.Value);
            }

            return parameters;
        }

        protected static IDictionary<string, object> GetParametersFromContext(IMiddlewareContext context)
        {
            return GetParametersFromSchemaAndQueryFields(context.Selection.Field, context.Selection.SyntaxNode);
        }
    }
}
