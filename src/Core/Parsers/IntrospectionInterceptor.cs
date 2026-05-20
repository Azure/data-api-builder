// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Configurations;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Core.Parsers
{
    /// <summary>
    /// Custom HotChocolate request interceptor which enables hooking into protocol-specific events.
    /// This class enables intercepting incoming HTTP requests.
    /// </summary>
    public class IntrospectionInterceptor : DefaultHttpRequestInterceptor
    {
        /// <summary>
        /// Parameterless constructor.
        /// </summary>
        /// <remarks>
        /// Hot Chocolate v16 isolates schema services from the application's request services.
        /// Resolving constructor-injected app singletons (e.g. <see cref="RuntimeConfigProvider"/>)
        /// against the schema service provider therefore fails at executor session creation.
        /// We instead resolve dependencies from <see cref="HttpContext.RequestServices"/> in
        /// <see cref="OnCreateAsync"/>, where the application's request scope is in effect.
        /// </remarks>
        public IntrospectionInterceptor()
        {
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
            RuntimeConfigProvider runtimeConfigProvider =
                context.RequestServices.GetRequiredService<RuntimeConfigProvider>();

            if (runtimeConfigProvider.GetConfig().AllowIntrospection)
            {
                requestBuilder.AllowIntrospection();
            }

            return base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
        }
    }
}
