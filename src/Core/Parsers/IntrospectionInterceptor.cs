// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Configurations;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Parsers
{
    /// <summary>
    /// Custom HotChocolate request interceptor which enables hooking into protocol-specific events.
    /// This class enables intercepting incoming HTTP requests.
    /// </summary>
    public class IntrospectionInterceptor : DefaultHttpRequestInterceptor
    {
        private RuntimeConfigProvider _runtimeConfigProvider;

        /// <summary>
        /// Constructor injects RuntimeConfigProvider to allow
        /// HotChocolate to attempt to retrieve the runtime config
        /// when evaluating GraphQL requests.
        /// </summary>
        /// <param name="runtimeConfigProvider"></param>
        public IntrospectionInterceptor(RuntimeConfigProvider runtimeConfigProvider)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Request interceptor allowing GraphQL introspection requests
        /// to continue only if the runtime config (if available) sets allow-introspection
        /// to true.
        /// AllowIntrospection() adds the key WellKnownContextData.IntrospectionAllowed
        /// to the GraphQL request context. That way, the IntrospectionAllowed validation rule
        /// added by .AllowIntrospection(false) in Startup.cs checks for the key on each request,
        /// and allows introspection if it is present.
        /// The WellKnownContextData.IntrospectionAllowed key is not configurable in client requests.
        /// Per Hot Chocolate documentation, the base implementation must be called
        /// with the included parameters.
        /// </summary>
        /// <param name="context">HttpContext</param>
        /// <param name="requestExecutor">GraphQL request executor.</param>
        /// <param name="requestBuilder">GraphQL request pipeline builder</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <seealso cref="https://chillicream.com/docs/hotchocolate/server/introspection/#allowlisting-requests"/>
        /// <returns></returns>
        public override ValueTask OnCreateAsync(
            HttpContext context,
            IRequestExecutor requestExecutor,
            OperationRequestBuilder requestBuilder,
            CancellationToken cancellationToken)
        {
            if (_runtimeConfigProvider.GetConfig().AllowIntrospection)
            {
                requestBuilder.AllowIntrospection();
            }

            return base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
        }
    }
}
