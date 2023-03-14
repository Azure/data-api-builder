// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PermissionOperation = Azure.DataApiBuilder.Config.PermissionOperation;

namespace Azure.DataApiBuilder.Service.Tests.Authorization
{
    /// <summary>
    /// Common helpers and constants used in Authorization test methods.
    /// </summary>
    public static class AuthorizationHelpers
    {
        public const string TEST_ENTITY = "SampleEntity";
        public const string TEST_ROLE = "Writer";
        public const string GRAPHQL_AUTHORIZATION_ERROR = "AUTH_NOT_AUTHORIZED";

        /// <summary>
        /// Creates stub AuthorizationResolver object from provided runtimeConfig object.
        /// </summary>
        /// <param name="runtimeConfig">Configuration with Authorization metadata</param>
        /// <returns>AuthorizationResolver object</returns>
        public static AuthorizationResolver InitAuthorizationResolver(RuntimeConfig runtimeConfig)
        {
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(runtimeConfig);
            Mock<ISqlMetadataProvider> metadataProvider = new();
            Mock<ILogger<AuthorizationResolver>> logger = new();
            SourceDefinition sampleTable = CreateSampleTable();
            metadataProvider.Setup(x => x.GetSourceDefinition(TEST_ENTITY)).Returns(sampleTable);
            metadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.mssql);

            string? outParam;
            Dictionary<string, Dictionary<string, string>> _exposedNameToBackingColumnMapping = CreateColumnMappingTable();
            metadataProvider.Setup(x => x.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out outParam))
                              .Callback(new metaDataCallback((string entity, string exposedField, out string? backingColumn) => _ = _exposedNameToBackingColumnMapping[entity].TryGetValue(exposedField, out backingColumn)))
                              .Returns((string entity, string exposedField, string? backingColumn) => _exposedNameToBackingColumnMapping[entity].TryGetValue(exposedField, out backingColumn));

            return new AuthorizationResolver(runtimeConfigProvider, metadataProvider.Object, logger.Object);
        }

        /// <summary>
        /// Creates a stub RuntimeConfig object with user/test defined values
        /// that set AuthorizationMetadata.
        /// </summary>
        /// <param name="entityName">Top level entity name</param>
        /// <param name="entitySource">Database name for entity</param>
        /// <param name="roleName">Role permitted to access entity</param>
        /// <param name="operation">Operation permitted for role</param>
        /// <param name="includedCols">columns allowed for operation</param>
        /// <param name="excludedCols">columns NOT allowed for operation</param>
        /// <param name="databasePolicy">database policy for operation</param>
        /// <param name="requestPolicy">request policy for operation</param>
        /// <returns></returns>
        public static RuntimeConfig InitRuntimeConfig(
            string entityName = TEST_ENTITY,
            object? entitySource = null,
            string roleName = "Reader",
            Config.Operation operation = Config.Operation.Create,
            HashSet<string>? includedCols = null,
            HashSet<string>? excludedCols = null,
            string? databasePolicy = null,
            string? requestPolicy = null,
            string authProvider = "AppService",
            DatabaseType dbType = DatabaseType.mssql
            )
        {
            Field? fieldsForRole = null;

            if (entitySource is null)
            {
                entitySource = TEST_ENTITY;
            }

            if (includedCols is not null || excludedCols is not null)
            {
                // Only create object for Fields if inc/exc cols is not null.
                fieldsForRole = new(
                    include: includedCols,
                    exclude: excludedCols);
            }

            Policy policy = new(requestPolicy, databasePolicy);

            PermissionOperation actionForRole = new(
                Name: operation,
                Fields: fieldsForRole,
                Policy: policy);

            PermissionSetting permissionForEntity = new(
                role: roleName,
                operations: new object[] { JsonSerializer.SerializeToElement(actionForRole) });

            Entity sampleEntity = new(
                Source: entitySource,
                Rest: null,
                GraphQL: null,
                Permissions: new PermissionSetting[] { permissionForEntity },
                Relationships: null,
                Mappings: null
                );

            Dictionary<string, Entity> entityMap = new()
            {
                { entityName, sampleEntity }
            };

            // Create runtime settings for the config.
            Dictionary<GlobalSettingsType, object> runtimeSettings = new();
            AuthenticationConfig authenticationConfig = new(Provider: authProvider);
            HostGlobalSettings hostGlobal = new(Authentication: authenticationConfig);
            JsonElement hostGlobalJson = JsonSerializer.SerializeToElement(hostGlobal);
            RestGlobalSettings restGlobalSettings = new();
            JsonElement restGlobalJson = JsonSerializer.SerializeToElement(restGlobalSettings);
            runtimeSettings.Add(GlobalSettingsType.Host, hostGlobalJson);
            runtimeSettings.Add(GlobalSettingsType.Rest, restGlobalJson);

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(DatabaseType: dbType),
                RuntimeSettings: runtimeSettings,
                Entities: entityMap
                );

            runtimeConfig.DetermineGlobalSettings();

            return runtimeConfig;
        }

        /// <summary>
        /// Helper which creates a TableDefinition with the number of columns defined.
        /// Column names will be of form "colX" where x is an integer starting at 1.
        /// There will be as many columns as denoted in the columnCount parameter.
        /// </summary>
        /// <param name="columnCount">Number of columns to create.</param>
        /// <returns>Sample TableDefinition object</returns>
        public static SourceDefinition CreateSampleTable(int columnCount = 4)
        {
            Assert.IsTrue(columnCount > 0);

            SourceDefinition tableDefinition = new();

            for (int count = 1; count <= columnCount; count++)
            {
                string columnName = $"col{count}";
                tableDefinition.Columns.Add(key: columnName, value: new ColumnDefinition());
            }

            return tableDefinition;
        }

        /// <summary>
        /// Creates Mock mapping of ExposedColumnNames to BackingColumnNames.
        /// Requests only contain ExposedColumnNames and must be translated
        /// to BackingColumnNames since authorization configuration denotes
        /// BackingColumnNames
        /// </summary>
        /// <param name="columnCount">Number of columns to create.
        /// Defaults to 6 to account for max number of columns testsed in AuthorizationResolverUnitTests.</param>
        /// <returns>ExposedColumnNames to BackingColumnNames Dictionary.</returns>
        public static Dictionary<string, Dictionary<string, string>> CreateColumnMappingTable(int columnCount = 6)
        {
            Dictionary<string, Dictionary<string, string>> _exposedNameToBackingColumnMapping = new();
            _exposedNameToBackingColumnMapping.Add(key: TEST_ENTITY, value: new());

            for (int count = 1; count <= columnCount; count++)
            {
                string columnName = $"col{count}";
                _exposedNameToBackingColumnMapping[TEST_ENTITY].Add(key: columnName, value: columnName);
            }

            return _exposedNameToBackingColumnMapping;
        }

        /// <summary>
        /// Needed for the callback that is required
        /// to make use of out parameter with mocking.
        /// Without use of delegate the out param will
        /// not be populated with the correct value.
        /// This delegate is for the callback used
        /// with the mocked MetadataProvider.
        /// </summary>
        /// <param name="entity">Name of entity.</param>
        /// <param name="exposedField">Exposed field name.</param>
        /// <param name="backingColumn">Out param for backing column name.</param>
        delegate void metaDataCallback(string entity, string exposedField, out string? backingColumn);
    }
}
