namespace Azure.DataGateway.Service.ServerTiming
{
    using System.Collections.Generic;

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
