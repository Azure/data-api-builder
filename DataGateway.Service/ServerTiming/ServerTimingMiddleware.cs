using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.ServerTiming
{
    public sealed class ServerTimingMiddleware
    {
        private readonly RequestDelegate _next;

        private Task _completedTask = Task.FromResult<object>(null);

        public ServerTimingMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public Task Invoke(HttpContext context)
        {
            HandleServerTiming(context);

            return _next(context);
        }

        private void HandleServerTiming(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                IServerTiming serverTiming = context.RequestServices.GetRequiredService<IServerTiming>();

                if (serverTiming.Metrics.Count > 0)
                {
                    context.Response.SetServerTiming(serverTiming.Metrics.ToArray());
                }

                return _completedTask;
            });
        }
    }
}
