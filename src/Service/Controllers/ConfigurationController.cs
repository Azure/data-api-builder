// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ConfigurationController : Controller
    {
        RuntimeConfigProvider _configurationProvider;
        private readonly ILogger<ConfigurationController> _logger;

        public ConfigurationController(RuntimeConfigProvider configurationProvider, ILogger<ConfigurationController> logger)
        {
            _configurationProvider = configurationProvider;
            _logger = logger;
        }

        /// <summary>
        /// Takes in the runtime configuration and the configuration overrides and configures the runtime.
        /// If the runtime is already configured, it will return a conflict result.
        /// </summary>
        /// <param name="configuration">Runtime configuration, schema, resolvers and connection string.</param>
        /// <returns>Ok in case of success, Bad request on bad config
        /// or Conflict if the runtime is already configured </returns>
        [HttpPost("v2")]
        public async Task<ActionResult> Index([FromBody] ConfigurationPostParametersV2 configuration)
        {
            if (_configurationProvider.TryGetRuntimeConfiguration(out _))
            {
                return new ConflictResult();
            }

            try
            {
                string mergedConfiguration = MergeJsonProvider.Merge(configuration.Configuration, configuration.ConfigurationOverrides);

                bool initResult = await _configurationProvider.Initialize(
                    mergedConfiguration,
                    configuration.Schema,
                    configuration.AccessToken);

                if (initResult && _configurationProvider.TryGetRuntimeConfiguration(out _))
                {
                    return Ok();
                }
                else
                {
                    _logger.LogError($"Failed to initialize configuration.");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Exception during configuration initialization. {e}");
            }

            return BadRequest();
        }

        public async Task<ActionResult> Index([FromBody] ConfigurationPostParameters configuration)
        {
            if (_configurationProvider.TryGetRuntimeConfiguration(out _))
            {
                return new ConflictResult();
            }

            try
            {
                bool initResult = await _configurationProvider.Initialize(
                    configuration.Configuration,
                    configuration.Schema,
                    configuration.ConnectionString,
                    configuration.AccessToken);

                if (initResult && _configurationProvider.TryGetRuntimeConfiguration(out _))
                {
                    return Ok();
                }
                else
                {
                    _logger.LogError($"Failed to initialize configuration.");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Exception during configuration initialization. {e}");
            }

            return BadRequest();
        }
    }
    /// <summary>
    /// The required parameters required to configure the runtime.
    /// </summary>
    /// <param name="Configuration">The runtime configuration.</param>
    /// <param name="Schema">The GraphQL schema. Can be left empty for SQL databases.</param>
    /// <param name="ConnectionString">The database connection string.</param>
    /// <param name="AccessToken">The managed identity access token (if any) used to connect to the database.</param>
    /// <param name="Database"> The name of the database to be used for Cosmos</param>
    public record class ConfigurationPostParameters(
        string Configuration,
        string? Schema,
        string ConnectionString,
        string? AccessToken)
    { }

    /// <summary>
    /// The required parameters required to configure the runtime.
    /// </summary>
    /// <param name="Configuration">The runtime configuration.</param>
    /// <param name="ConfigurationOverrides">Configuration parameters that override the options from the Configuration file.</param>
    /// <param name="Schema">The GraphQL schema. Can be left empty for SQL databases.</param>
    /// <param name="AccessToken">The managed identity access token (if any) used to connect to the database.</param>
    public record class ConfigurationPostParametersV2(
        string Configuration,
        string ConfigurationOverrides,
        string? Schema,
        string? AccessToken)
    { }
}
