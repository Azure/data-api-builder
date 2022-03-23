using System.Collections.Generic;
using Azure.DataGateway.Service.Configurations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Azure.DataGateway.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ConfigurationController : Controller
    {
        InMemoryUpdateableConfigurationProvider _configurationProvider;
        IConfiguration _configuration;
        public ConfigurationController(
            InMemoryUpdateableConfigurationProvider configurationProvider,
            IConfiguration configuration)
        {
            _configuration = configuration;
            _configurationProvider = configurationProvider;
        }

        [HttpGet]
        public ActionResult Index([FromQuery] string key)
        {
            if (_configurationProvider.TryGet(key, out string value))
            {
                return new OkObjectResult(value);
            }
            else
            {
                return new NotFoundObjectResult(key);
            }
        }

        /// <summary>
        /// Takes in KeyValuePairs and sets them all. In case of conflict with an
        /// existing key, this will return a Conflict result with the conflicting key:value.
        /// </summary>
        /// <param name="configuration">The list of configurations to set.</param>
        /// <returns>Ok in case of success or Conflict with the key:value.</returns>
        [HttpPost]
        public ActionResult Index([FromBody] Dictionary<string, string> configuration)
        {
            foreach ((string key, string value) in configuration)
            {
                string configValue = _configuration.GetValue<string>(key);
                if (!string.IsNullOrWhiteSpace(configValue))
                {
                    return new ConflictObjectResult($"{key}:{value}");
                }
            }

            _configurationProvider.SetManyAndReload(configuration);

            return new OkResult();
        }
    }
}
