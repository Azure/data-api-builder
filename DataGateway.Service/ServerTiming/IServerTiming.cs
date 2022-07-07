namespace Azure.DataGateway.Service.ServerTiming
{
    using System.Collections.Generic;

    public interface IServerTiming
    {
        ICollection<ServerTimingMetric> Metrics { get; }
    }
}
