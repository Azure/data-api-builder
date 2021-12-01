
namespace Azure.DataGateway.Service.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using System;
    using System.Text.Json.Serialization;

    internal sealed class ContainerMetadata
    {
        internal ContainerMetadata()
        {
            if (Instance != null)
            {
                throw new InvalidOperationException();
            }

            Instance = this;
        }

        public static ContainerMetadata Instance { get; private set; }

        public DateTime AllocatedTime { set; get; }

        public DateTime LastHeartbeatTime { get; set; }

        public string SessionToken { set; get; }
    }

    public sealed class StatusResource
    {
        [JsonPropertyName("started")]
        public DateTime Started { get; set; }

        [JsonPropertyName("last_activity")]
        public DateTime LastActivity { get; set; }

        [JsonPropertyName("last_heartbeat")]
        public DateTime LastHeartbeat { get; set; }

        [JsonPropertyName("allocated")]
        public DateTime Allocated { get; set; }

        [JsonPropertyName("connections")]
        public int Connections { get; set; }

        [JsonPropertyName("kernels")]
        public int Kernels { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public sealed class StatusController : ControllerBase
    {
        public StatusController()
        {
        }

        [HttpGet]
#pragma warning disable CA1024 // Use properties where appropriate
        public ActionResult<StatusResource> GetStatus()
#pragma warning restore CA1024 // Use properties where appropriate
        {
            int connections;
            DateTime lastActivity;

            lock (Startup.CurrentConnectionsLock)
            {
                connections = Startup.CurrentConnections;
                lastActivity = Startup.LastActivity;
            }

            var statusResource = new StatusResource
            {
                Started = Startup.StartupTime,
                LastActivity = lastActivity,
                Connections = connections,
                Kernels = 0,
                Allocated = ContainerMetadata.Instance.AllocatedTime,
                LastHeartbeat = lastActivity // In our case we don't have a heartbeat, so we just take the last activity for this as well.
            };

            return statusResource;
        }
    }
}
