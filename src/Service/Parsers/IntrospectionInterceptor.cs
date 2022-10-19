using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Service.Parsers
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
            IQueryRequestBuilder requestBuilder,
            CancellationToken cancellationToken   
            )
        {
            if (_runtimeConfigProvider.TryGetRuntimeConfiguration(out RuntimeConfig? runtimeConfig) &&
                runtimeConfig.GraphQLGlobalSettings.AllowIntrospection)
            {
                requestBuilder.AllowIntrospection();
            }

            return base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
        }
    }
}
