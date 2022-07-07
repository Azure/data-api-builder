using System.Collections.Generic;

namespace Azure.DataGateway.Service.ServerTiming
{
    public class ServerTimingHeaderValue
    {
        public ICollection<ServerTimingMetric> Metrics { get; }

        public ServerTimingHeaderValue()
        {
            Metrics = new List<ServerTimingMetric>();
        }

        public override string ToString()
        {
            return string.Join(",", Metrics);
        }
    }
}
