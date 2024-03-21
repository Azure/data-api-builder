// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

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
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new(runtimeConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(loader);
            Mock<ISqlMetadataProvider> metadataProvider = new();
            Mock<ILogger<AuthorizationResolver>> logger = new();
            SourceDefinition sampleTable = CreateSampleTable();
            metadataProvider.Setup(x => x.GetSourceDefinition(TEST_ENTITY)).Returns(sampleTable);
            metadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            string? outParam;
            Dictionary<string, Dictionary<string, string>> _exposedNameToBackingColumnMapping = CreateColumnMappingTable();
            metadataProvider.Setup(x => x.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out outParam))
                              .Callback(new metaDataCallback((string entity, string exposedField, out string? backingColumn) => _ = _exposedNameToBackingColumnMapping[entity].TryGetValue(exposedField, out backingColumn)))
                              .Returns((string entity, string exposedField, string? backingColumn) => _exposedNameToBackingColumnMapping[entity].TryGetValue(exposedField, out backingColumn));

            metadataProvider.Setup(x => x.GetEntityName(It.IsAny<string>()))
                .Returns((string entity) => entity);
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            string dataSourceName = runtimeConfigProvider.GetConfig().DefaultDataSourceName;
            metadataProviderFactory.Setup(x => x.GetMetadataProvider(dataSourceName)).Returns(metadataProvider.Object);

            return new AuthorizationResolver(runtimeConfigProvider, metadataProviderFactory.Object);
        }

        /// <summary>
        /// Creates a stub RuntimeConfig object with user/test defined values
        /// that set AuthorizationMetadata.
        /// </summary>
        /// <param name="entityName">Top level entity name</param>
        /// <param name="entitySource">Database source for entity</param>
        /// <param name="roleName">Role permitted to access entity</param>
        /// <param name="operation">Operation permitted for role</param>
        /// <param name="includedCols">columns allowed for operation</param>
        /// <param name="excludedCols">columns NOT allowed for operation</param>
        /// <param name="databasePolicy">database policy for operation</param>
        /// <param name="requestPolicy">request policy for operation</param>
        /// <param name="authProvider">Authentication provider</param>
        /// <param name="dbType">Database type configured.</param>
        /// <returns></returns>
        public static RuntimeConfig InitRuntimeConfig(
            EntitySource entitySource,
            string entityName = TEST_ENTITY,
            string roleName = "Reader",
            EntityActionOperation operation = EntityActionOperation.Create,
            HashSet<string>? includedCols = null,
            HashSet<string>? excludedCols = null,
            string? databasePolicy = null,
            string? requestPolicy = null,
            string authProvider = "AppService",
            DatabaseType dbType = DatabaseType.MSSQL
            )
        {
            EntityActionFields? fieldsForRole = null;

            if (includedCols is not null || excludedCols is not null)
            {
                // Only create object for Fields if inc/exc cols is not null.
                fieldsForRole = new(
                    Include: includedCols,
                    Exclude: excludedCols ?? new());
            }

            EntityActionPolicy policy = new(requestPolicy, databasePolicy);

            EntityAction actionForRole = new(
                Action: operation,
                Fields: fieldsForRole,
                Policy: policy);

            EntityPermission permissionForEntity = new(
                Role: roleName,
                Actions: new EntityAction[] { actionForRole });

            Entity sampleEntity = new(
                Source: entitySource,
                Rest: new(Array.Empty<SupportedHttpVerb>()),
                GraphQL: new(entityName.Singularize(), entityName.Pluralize()),
                Permissions: new EntityPermission[] { permissionForEntity },
                Relationships: null,
                Mappings: null
            );

            // Create runtime settings for the config.
            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                DataSource: new DataSource(dbType, "", new()),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Host: new(
                        Cors: null,
                        Authentication: new(authProvider, null)
                    )
                ),
                Entities: new(new Dictionary<string, Entity> { { entityName, sampleEntity } })
            );

            return runtimeConfig;
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
        /// <param name="authProvider">Authentication provider</param>
        /// <param name="dbType">Database type configured.</param>
        /// <returns></returns>
        public static RuntimeConfig InitRuntimeConfig(
            string entityName = TEST_ENTITY,
            string? entitySource = null,
            string roleName = "Reader",
            EntityActionOperation operation = EntityActionOperation.Create,
            HashSet<string>? includedCols = null,
            HashSet<string>? excludedCols = null,
            string? databasePolicy = null,
            string? requestPolicy = null,
            string authProvider = "AppService",
            DatabaseType dbType = DatabaseType.MSSQL
            )
        {
            return InitRuntimeConfig(
                entitySource: new EntitySource(entitySource ?? TEST_ENTITY, EntitySourceType.Table, null, null),
                entityName: entityName,
                roleName: roleName,
                operation: operation,
                includedCols: includedCols,
                excludedCols: excludedCols,
                databasePolicy: databasePolicy,
                requestPolicy: requestPolicy,
                authProvider: authProvider,
                dbType: dbType
            );
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
