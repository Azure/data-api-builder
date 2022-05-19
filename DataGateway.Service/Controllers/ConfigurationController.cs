using Azure.DataGateway.Service.Configurations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Azure.DataGateway.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ConfigurationController : Controller
    {
        RuntimeConfigProvider _configurationProvider;
        IConfiguration _configuration;
        public ConfigurationController(
            RuntimeConfigProvider configurationProvider,
            IConfiguration configuration)
        {
            _configuration = configuration;
            _configurationProvider = configurationProvider;
        }

        /// <summary>
        /// Takes in KeyValuePairs and sets them all. In case of conflict with an
        /// existing key, this will return a Conflict result with the conflicting key:value.
        /// </summary>
        /// <param name="configuration">The list of configurations to set.</param>
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
