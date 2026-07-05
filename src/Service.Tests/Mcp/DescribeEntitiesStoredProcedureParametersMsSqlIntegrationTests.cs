// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Integration tests for DescribeEntitiesTool's stored-procedure parameter projection.
    /// Verifies the end-to-end behavior:
    /// The actual merge of config-side <c>ParameterMetadata</c> onto DB-introspected
    /// <c>ParameterDefinition</c>s, performed by
    /// <see cref="Core.Services.MetadataProviders.SqlMetadataProvider.FillSchemaForStoredProcedureAsync"/>.
    ///
    /// Scenarios (reuse SPs already defined in DatabaseSchema-MsSql.sql / dab-config.MsSql.json):
    ///   - <c>GetBooks</c>     -> SP <c>get_books</c>, zero DB params, no config overrides.
    ///   - <c>GetBook</c>      -> SP <c>get_book_by_id @id int</c>, no config overrides.
    ///   - <c>InsertBook</c>   -> SP <c>insert_book @title, @publisher_id</c>, config sets
    ///                            both to required=false with defaults.
    ///   - Negative path      -> a config that declares an SP parameter the DB does not
    ///                            have must fail at <c>InitializeAsync</c>, which is the
    ///                            load-bearing invariant DescribeEntitiesTool relies on
    ///                            (it no longer carries a config-only fallback path).
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class DescribeEntitiesStoredProcedureParametersMsSqlIntegrationTests : SqlTestBase
    {
        // The base config is captured once at ClassInitialize so test methods do not
        // depend on the ASP_NETCORE_ENVIRONMENT variable still being set when they run
        // (other test classes may unset it).
        private static RuntimeConfig _baseConfig;

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
            _baseConfig = SqlTestHelper.SetupRuntimeConfig();
        }

        /// <summary>
        /// SP with zero parameters in the DB and no config-side parameters projects an empty array.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_StoredProcedureWithNoDbOrConfigParameters_EmitsEmptyParametersArray()
        {
            JsonElement parameters = await DescribeEntityParametersAsync("GetBooks");

            Assert.AreEqual(JsonValueKind.Array, parameters.ValueKind);
            Assert.AreEqual(0, parameters.GetArrayLength());
        }

        /// <summary>
        /// SP with one DB parameter and no config-side override: name comes from the DB,
        /// required defaults to true, default is null, description is empty.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_DbParameterWithoutConfigOverride_DefaultsToRequiredTrue()
        {
            JsonElement parameters = await DescribeEntityParametersAsync("GetBook");

            Assert.AreEqual(1, parameters.GetArrayLength());
            AssertParameter(parameters, name: "id", expectedRequired: true, expectedDefault: null, expectedDescription: string.Empty);
        }

        /// <summary>
        /// SP with two DB parameters and matching config-side overrides: each parameter's
        /// required / default values come from config; name still comes from the DB.
        /// This is the scenario that exercises the upstream merge end-to-end.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_DbParametersWithConfigOverrides_MergesConfigValuesOntoDbParameters()
        {
            JsonElement parameters = await DescribeEntityParametersAsync("InsertBook");

            Assert.AreEqual(2, parameters.GetArrayLength());
            AssertParameter(parameters, name: "title", expectedRequired: false, expectedDefault: "randomX", expectedDescription: string.Empty);
            AssertParameter(parameters, name: "publisher_id", expectedRequired: false, expectedDefault: "1234", expectedDescription: string.Empty);
        }

        /// <summary>
        /// Mixed merge: an SP has two DB parameters but the config only overrides one of them.
        /// The overridden param ('id') must reflect the config-side required/default; the
        /// non-overridden param ('title') must fall back to the merge defaults
        /// (required=true, default=null). This exercises the per-parameter branch in
        /// <c>FillSchemaForStoredProcedureAsync</c> that only applies overrides when the
        /// config-side entry exists for that name.
        ///
        /// The shared config already declares overrides for both parameters of
        /// <c>UpdateBookTitle</c>, so we rebuild a fresh metadata provider against a tampered
        /// config that keeps only the 'id' override, then restore the shared provider at the
        /// end so subsequent tests are unaffected.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_PartialConfigOverride_MergesOverriddenParamAndDefaultsTheOther()
        {
            RuntimeConfig baseConfig = _baseConfig;

            const string EntityName = "UpdateBookTitlePartial";
            Entity tamperedEntity = new(
                Source: new(
                    "update_book_title",
                    EntitySourceType.StoredProcedure,
                    Parameters: new List<ParameterMetadata>
                    {
                        // Only 'id' carries a config-side override; 'title' is intentionally
                        // omitted so the merge must default it to required=true, default=null.
                        new() { Name = "id", Required = false, Default = "42" }
                    },
                    KeyFields: null),
                GraphQL: new(EntityName, EntityName, Enabled: false),
                Rest: new(Enabled: false),
                Fields: null,
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: "anonymous",
                        Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                        })
                },
                Relationships: null,
                Mappings: null,
                Mcp: null);

            // Drop the original UpdateBookTitle entity from the rebuilt config so it does not
            // race with the tampered one for the same underlying SP.
            Dictionary<string, Entity> entityMap = baseConfig.Entities.Entities
                .Where(kvp => kvp.Key != "UpdateBookTitle")
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            entityMap[EntityName] = tamperedEntity;
            RuntimeConfig tamperedConfig = baseConfig with { Entities = new(entityMap) };

            // Rebuild the shared metadata provider against the tampered config and re-init.
            RuntimeConfigProvider tamperedProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(tamperedConfig);
            SetUpSQLMetadataProvider(tamperedProvider);
            await _sqlMetadataProvider.InitializeAsync();

            // Refresh the metadata-provider factory mock so DescribeEntitiesTool sees the
            // tampered provider through the standard service-provider wiring.
            _metadataProviderFactory = new Mock<IMetadataProviderFactory>();
            _metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(_sqlMetadataProvider);

            try
            {
                JsonElement parameters = await DescribeEntityParametersAsync(EntityName, tamperedConfig);

                Assert.AreEqual(2, parameters.GetArrayLength());
                AssertParameter(parameters, name: "id", expectedRequired: false, expectedDefault: "42", expectedDescription: string.Empty);
                AssertParameter(parameters, name: "title", expectedRequired: true, expectedDefault: null, expectedDescription: string.Empty);
            }
            finally
            {
                // Restore the shared fixture's provider/factory so subsequent tests are unaffected.
                RuntimeConfigProvider sharedProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(baseConfig);
                SetUpSQLMetadataProvider(sharedProvider);
                await _sqlMetadataProvider.InitializeAsync();
                _metadataProviderFactory = new Mock<IMetadataProviderFactory>();
                _metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(_sqlMetadataProvider);
            }
        }

        /// <summary>
        /// Locks in the invariant that DescribeEntitiesTool relies on: if a config declares an
        /// SP parameter that the DB schema does not have, the metadata provider must fail at
        /// <c>InitializeAsync</c> time (so describe_entities never reaches a half-merged state).
        ///
        /// Builds a fresh <c>MsSqlMetadataProvider</c> with a tampered config (an extra
        /// <c>bogus_param</c> on a real SP) and asserts that init throws.
        /// </summary>
        [TestMethod]
        public async Task SqlMetadataProvider_FailsAtInit_WhenConfigDeclaresParameterNotInDb()
        {
            RuntimeConfig baseConfig = _baseConfig;

            const string EntityName = "GetBookWithBogusParam";
            Entity tamperedEntity = new(
                Source: new(
                    "get_book_by_id",
                    EntitySourceType.StoredProcedure,
                    Parameters: new List<ParameterMetadata>
                    {
                        new() { Name = "id" },
                        // bogus_param is not declared on dbo.get_book_by_id; init must throw.
                        new() { Name = "bogus_param", Required = false, Default = "x" }
                    },
                    KeyFields: null),
                GraphQL: new(EntityName, EntityName, Enabled: false),
                Rest: new(Enabled: false),
                Fields: null,
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: "anonymous",
                        Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                        })
                },
                Relationships: null,
                Mappings: null,
                Mcp: null);

            Dictionary<string, Entity> entityMap = new(baseConfig.Entities.Entities)
            {
                [EntityName] = tamperedEntity
            };
            RuntimeConfig tamperedConfig = baseConfig with { Entities = new(entityMap) };

            // Bootstrap a fresh provider against the tampered config and verify init fails.
            RuntimeConfigProvider tamperedProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(tamperedConfig);
            SetUpSQLMetadataProvider(tamperedProvider);

            DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
                () => _sqlMetadataProvider.InitializeAsync());

            StringAssert.Contains(ex.Message, "bogus_param");
            StringAssert.Contains(ex.Message, "get_book_by_id");

            // Restore the shared fixture's provider so subsequent tests in the class are unaffected.
            RuntimeConfigProvider sharedProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(baseConfig);
            SetUpSQLMetadataProvider(sharedProvider);
            await _sqlMetadataProvider.InitializeAsync();
        }

        /// <summary>
        /// Invokes describe_entities with an <c>entities</c> filter for the given entity and
        /// returns its <c>parameters</c> array. Uses the shared fixture's base config by default.
        /// </summary>
        private static Task<JsonElement> DescribeEntityParametersAsync(string entityName) =>
            DescribeEntityParametersAsync(entityName, _baseConfig);

        /// <summary>
        /// Overload that lets a test inject a tampered config (e.g. for mixed-merge scenarios)
        /// so the tool reads from that config while still using the shared metadata-provider
        /// factory mock (which the caller is expected to have refreshed against the same config).
        /// </summary>
        private static async Task<JsonElement> DescribeEntityParametersAsync(string entityName, RuntimeConfig config)
        {
            IServiceProvider serviceProvider = BuildDescribeEntitiesServiceProvider(config);
            DescribeEntitiesTool tool = new();

            string argsJson = JsonSerializer.Serialize(new { entities = new[] { entityName } });
            using JsonDocument arguments = JsonDocument.Parse(argsJson);

            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            Assert.IsTrue(result.IsError == false || result.IsError == null,
                $"describe_entities returned an error for entity '{entityName}'. Content: {SerializeFirstContent(result)}");

            return GetEntityParameters(result, entityName);
        }

        /// <summary>
        /// Builds a service provider that wires DescribeEntitiesTool to the shared fixture's
        /// real <see cref="Core.Services.ISqlMetadataProvider"/> (already initialized against
        /// the live database by <see cref="SqlTestBase.InitializeTestFixture"/>) and the real
        /// authorization resolver, with a real <see cref="DefaultHttpContext"/> carrying the
        /// anonymous role header.
        /// </summary>
        private static IServiceProvider BuildDescribeEntitiesServiceProvider(RuntimeConfig config)
        {
            ServiceCollection services = new();

            // Real RuntimeConfigProvider populated from the provided config snapshot.
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            // Real metadata-provider factory backed by the shared fixture's live provider.
            services.AddSingleton(_metadataProviderFactory.Object);

            // Real authorization resolver wired by SqlTestBase against the live config + provider.
            services.AddSingleton(_authorizationResolver);

            // Real HttpContext carrying the anonymous role header that DescribeEntitiesTool reads.
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationResolver.ROLE_ANONYMOUS;
            IHttpContextAccessor httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
            services.AddSingleton(httpContextAccessor);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        private static JsonElement GetEntityParameters(CallToolResult result, string entityName)
        {
            Assert.IsNotNull(result.Content);
            Assert.IsTrue(result.Content.Count > 0);
            Assert.IsInstanceOfType(result.Content[0], typeof(TextContentBlock));

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            JsonElement root = JsonDocument.Parse(firstContent.Text).RootElement;
            JsonElement entities = root.GetProperty("entities");

            JsonElement entity = entities.EnumerateArray().Single(e =>
                string.Equals(e.GetProperty("name").GetString(), entityName, StringComparison.Ordinal));

            return entity.GetProperty("parameters");
        }

        private static string SerializeFirstContent(CallToolResult result)
        {
            if (result.Content is null || result.Content.Count == 0)
            {
                return "<no content>";
            }

            return result.Content[0] is TextContentBlock textBlock
                ? textBlock.Text ?? string.Empty
                : result.Content[0].GetType().Name;
        }

        private static void AssertParameter(
            JsonElement parameters,
            string name,
            bool expectedRequired,
            string expectedDefault,
            string expectedDescription)
        {
            JsonElement param = parameters.EnumerateArray()
                .Single(p => p.GetProperty("name").GetString() == name);

            Assert.AreEqual(expectedRequired, param.GetProperty("required").GetBoolean(),
                $"required mismatch for parameter '{name}'.");

            JsonElement defaultElement = param.GetProperty("default");
            if (expectedDefault is null)
            {
                Assert.AreEqual(JsonValueKind.Null, defaultElement.ValueKind,
                    $"default should be JSON null for parameter '{name}'.");
            }
            else
            {
                Assert.AreEqual(expectedDefault, defaultElement.GetString(),
                    $"default mismatch for parameter '{name}'.");
            }

            Assert.AreEqual(expectedDescription, param.GetProperty("description").GetString(),
                $"description mismatch for parameter '{name}'.");
        }
    }
}
