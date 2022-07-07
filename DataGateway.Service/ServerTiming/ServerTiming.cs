namespace Azure.DataGateway.Service.ServerTiming
{
    using System.Collections.Generic;

    internal class ServerTiming : IServerTiming
    {
        public ICollection<ServerTimingMetric> Metrics { get; }

        public ServerTiming()
        {
            Metrics = new List<ServerTimingMetric>();
        }
    }
}
