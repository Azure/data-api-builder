using System.Collections.Generic;

namespace Azure.DataGateway.Service.ServerTiming
{
    public interface IServerTiming
    {
        ICollection<ServerTimingMetric> Metrics { get; }
    }
}
