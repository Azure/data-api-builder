// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
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
        /// Takes in the runtime configuration, configuration overrides, schema and access token configures the runtime.
        /// If the runtime is already configured, it will return a conflict result.
        /// </summary>
        /// <param name="configuration">Runtime configuration, config overrides, schema and access token.</param>
        /// <returns>Ok in case of success, Bad request on bad config
        /// or Conflict if the runtime is already configured </returns>
        [HttpPost("v2")]
        public async Task<ActionResult> Index([FromBody] ConfigurationPostParametersV2 configuration)
        {
            if (_configurationProvider.TryGetConfig(out _))
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

                if (initResult && _configurationProvider.TryGetConfig(out _))
                {
                    return Ok();
                }
                else
                {
                    _logger.LogError(
                        message: "{correlationId} Failed to initialize configuration.",
                        HttpContextExtensions.GetLoggerCorrelationId(HttpContext));
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    exception: e,
                    message: "{correlationId} Exception during configuration initialization.",
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContext));
            }

            return BadRequest();
        }

        /// <summary>
        /// Takes in the runtime configuration, schema, connection string and access token and configures the runtime.
        /// If the runtime is already configured, it will return a conflict result.
        /// </summary>
        /// <param name="configuration">Runtime configuration, schema, connection string and access token.</param>
        /// <returns>Ok in case of success, Bad request on bad config
        /// or Conflict if the runtime is already configured </returns>
        public async Task<ActionResult> Index([FromBody] ConfigurationPostParameters configuration)
        {
            if (_configurationProvider.TryGetConfig(out _))
            {
                return new ConflictResult();
            }

            try
            {
                bool initResult = await _configurationProvider.Initialize(
                    configuration.Configuration,
                    configuration.Schema,
                    configuration.ConnectionString,
                    configuration.AccessToken,
                    replacementSettings: new(azureKeyVaultOptions: null, doReplaceEnvVar: false, doReplaceAKVVar: false, envFailureMode: Config.Converters.EnvironmentVariableReplacementFailureMode.Ignore)
                );

                if (initResult && _configurationProvider.TryGetConfig(out _))
                {
                    return Ok();
                }
                else
                {
                    _logger.LogError(
                        message: "{correlationId} Failed to initialize configuration.",
                        HttpContextExtensions.GetLoggerCorrelationId(HttpContext));
                }
            }
            catch (Exception e)
            {
                _logger.LogError(
                    exception: e,
                    message: "{correlationId} Exception during configuration initialization.",
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContext));
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
