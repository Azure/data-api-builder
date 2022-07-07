using System.Collections.Generic;

namespace Azure.DataGateway.Service.ServerTiming
{
    internal class ServerTiming : IServerTiming
    {
        public ICollection<ServerTimingMetric> Metrics { get; }

        public ServerTiming()
        {
            Metrics = new List<ServerTimingMetric>();
        }
    }
}
