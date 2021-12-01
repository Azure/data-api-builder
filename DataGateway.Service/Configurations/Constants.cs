using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.configurations
{
    public static class Constants
    {
        public static readonly int webSocketServerTimeoutInMinutes = 2;

        public static readonly string TenantId = "33e01921-4d64-4f8c-a055-5bdaffd5e33d";

        public static class HubMethodNames
        {
            public static readonly string ProcessContainerHttpResponse = "ProcessContainerHttpResponse";

            public static readonly string ContainerRegistered = "ContainerRegistered";

            public static readonly string ProcessContainerWebSocketResponseAsync = "ProcessContainerWebSocketResponseAsync";

            public static readonly string ProcessWebSocketClose = "ProcessWebSocketClose";
        }
    }
}
