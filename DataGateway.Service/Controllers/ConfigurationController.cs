using System.Diagnostics;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.ServerTiming;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ConfigurationController : Controller
    {
        RuntimeConfigProvider _configurationProvider;
        private readonly IServerTiming _serverTiming;

        public ConfigurationController(RuntimeConfigProvider configurationProvider, IServerTiming serverTiming)
        {
            _configurationProvider = configurationProvider;
            _serverTiming = serverTiming;
        }

        /// <summary>
        /// Takes in the runtime configuration, schema, connection string and optionally the
        /// resolvers and configures the runtime. If the runtime is already configured, it will
        /// return a conflict result.
        /// </summary>
        /// <param name="configuration">Runtime configuration, schema, resolvers and connection string.</param>
        /// <returns>Ok in case of success or Conflict with the key:value.</returns>
        [HttpPost]
        public ActionResult Index([FromBody] ConfigurationPostParameters configuration)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (_configurationProvider.TryGetRuntimeConfiguration(out _))
            {
                return new ConflictResult();
            }

            _configurationProvider.Initialize(
                configuration.Configuration,
                configuration.Schema,
                configuration.ConnectionString,
                configuration.AadToken,
                configuration.AccountEndpoint);
            stopwatch.Stop();
            _serverTiming.Metrics.Add(new ServerTimingMetric("Graphql-BindAsync", stopwatch.ElapsedMilliseconds, ""));

            return new OkResult();
        }
    }

    /// <summary>
    /// The required parameters required to configure the runtime.
    /// </summary>
    /// <param name="Configuration">The runtime configuration.</param>
    /// <param name="Schema">The GraphQL schema. Can be left empty for SQL databases.</param>
    /// <param name="ConnectionString">The database connection string.</param>
    /// <param name="AadToken">Client AAD token</param>
    public record class ConfigurationPostParameters(
        string Configuration,
        string? Schema,
        string ConnectionString,
        string AadToken,
        string AccountEndpoint)
    { }
}
