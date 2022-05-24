using Azure.DataGateway.Service.Configurations;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ConfigurationController : Controller
    {
        RuntimeConfigProvider _configurationProvider;
        public ConfigurationController(RuntimeConfigProvider configurationProvider)
        {
            _configurationProvider = configurationProvider;
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
            if (_configurationProvider.RuntimeConfiguration != null)
            {
                return new ConflictResult();
            }

            _configurationProvider.Initialize(
                configuration.Configuration,
                configuration.SchemaJson,
                configuration.ConnectionString,
                configuration.Resolvers);

            return new OkResult();
        }
    }

    public record class ConfigurationPostParameters
    {
        public string Configuration { get; set; }
        public string SchemaJson { get; set; }
        public string ConnectionString { get; set; }
        public string? Resolvers { get; set; }
    }
}
