// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ConfigurationController : Controller
    {
        RuntimeConfigProvider _configurationProvider;
        private readonly ILogger<ConfigurationController> _logger;
        private readonly IAuthenticationSchemeProvider _schemeProvider;
        private readonly IOptionsMonitor<JwtBearerOptions> _bearerOptionsMonitor;
        private readonly IOptionsMonitorCache<JwtBearerOptions> _bearerOptionsMonitorCache;
        private readonly IConfiguration _configuration;
        private readonly IPostConfigureOptions<JwtBearerOptions> _jwtPostConfigureOptions;
        //private readonly IOptionsChangeTokenSource<JwtBearerOptions> _tokenSource;

        public ConfigurationController(
            IAuthenticationSchemeProvider schemeProvider,
            RuntimeConfigProvider configurationProvider,
            ILogger<ConfigurationController> logger,
            IOptionsMonitor<JwtBearerOptions> bearerOptionsMonitor,
            IOptionsMonitorCache<JwtBearerOptions> bearerOptionsMonitorCache,
            IConfiguration configuration,
            IPostConfigureOptions<JwtBearerOptions> jwtPostConfigureOptions)
            //IOptionsChangeTokenSource<JwtBearerOptions> tokenSource)
        {
            _configurationProvider = configurationProvider;
            _logger = logger;
            _schemeProvider = schemeProvider;
            _bearerOptionsMonitor = bearerOptionsMonitor;
            _bearerOptionsMonitorCache = bearerOptionsMonitorCache;
            _configuration = configuration;
            _jwtPostConfigureOptions = jwtPostConfigureOptions;
            //_tokenSource = tokenSource;
        }

        [HttpPost("changeJwtProvider")]
        public ActionResult ChangeJwtProvider([FromBody] JwtConfigPostParameters jwtConfig)
        {
            try
            {
                JwtBearerOptions jwtOptions = _bearerOptionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);

                Console.WriteLine("Old Audience: " + jwtOptions.Audience);
                //Console.WriteLine("Current options: " + jwtOptions.Audience);
                if (_bearerOptionsMonitorCache.TryRemove(JwtBearerDefaults.AuthenticationScheme))
                {
                    jwtOptions.Audience = jwtConfig.Audience;
                    jwtOptions.TokenValidationParameters.ValidAudience = jwtConfig.Audience;
                    jwtOptions.Authority = jwtConfig.Authority;
                    _jwtPostConfigureOptions.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtOptions);

                    _bearerOptionsMonitorCache.GetOrAdd(JwtBearerDefaults.AuthenticationScheme, () => jwtOptions);
                    Console.WriteLine("New Audience: " + _bearerOptionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme).Audience);
                    return Ok();
                }
                else
                {
                    _logger.LogError(                        
                        message: "{correlationId} Failed to swap out jwtbeareroptions in ioptionsmonitorcache",
                        HttpContextExtensions.GetLoggerCorrelationId(HttpContext));
                    return BadRequest();
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
                    replaceEnvVar: false,
                    replacementFailureMode: Config.Converters.EnvironmentVariableReplacementFailureMode.Ignore);

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

    public record class JwtConfigPostParameters(
        string SchemeName,
        string Audience,
        string Authority)
    { }
}
