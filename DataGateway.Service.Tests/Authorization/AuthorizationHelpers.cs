using System;
using System.Collections.Generic;
using System.Text.Json;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Action = Azure.DataGateway.Config.Action;

namespace Azure.DataGateway.Service.Tests.Authorization
{
    /// <summary>
    /// Common helpers and constants used in Authorization test methods.
    /// </summary>
    public static class AuthorizationHelpers
    {
        public const string TEST_ENTITY = "SampleEntity";
        public const string TEST_ROLE = "Writer";

        /// <summary>
        /// Creates stub AuthorizationResolver object from provided runtimeConfig object.
        /// </summary>
        /// <param name="runtimeConfig">Configuration with Authorization metadata</param>
        /// <returns>AuthorizationResolver object</returns>
        public static AuthorizationResolver InitAuthorizationResolver(RuntimeConfig runtimeConfig)
        {
            RuntimeConfigPath configPath = new()
            {
                ConfigValue = runtimeConfig
            };

            Mock<IOptionsMonitor<RuntimeConfigPath>> runtimeConfigProvider = new();
            runtimeConfigProvider.Setup(x => x.CurrentValue).Returns(configPath);

            Mock<ISqlMetadataProvider> metadataProvider = new();
            TableDefinition sampleTable = CreateSampleTable();
            metadataProvider.Setup(x => x.GetTableDefinition(TEST_ENTITY)).Returns(sampleTable);

            return new AuthorizationResolver(runtimeConfigProvider.Object, metadataProvider.Object);
        }

        /// <summary>
        /// Creates a stub RuntimeConfig object with user/test defined values
        /// that set AuthorizationMetadata.
        /// </summary>
        /// <param name="entityName"></param>
        /// <param name="roleName"></param>
        /// <param name="actionName"></param>
        /// <param name="includedCols"></param>
        /// <param name="excludedCols"></param>
        /// <returns></returns>
        public static RuntimeConfig InitRuntimeConfig(
            string entityName = "SampleEntity",
            string roleName = "Reader",
            string actionName = ActionType.CREATE,
            string[] includedCols = null,
            string[] excludedCols = null
            )
        {
            Field fieldsForRole = new(
                Include: includedCols,
                Exclude: excludedCols);

            Action actionForRole = new(
                Name: actionName,
                Fields: fieldsForRole,
                Policy: null);

            PermissionSetting permissionForEntity = new(
                Role: roleName,
                Actions: new object[] { JsonSerializer.SerializeToElement(actionForRole) });

            Entity sampleEntity = new(
                Source: new String("SQL"),
                Rest: null,
                GraphQL: null,
                Permissions: new PermissionSetting[] { permissionForEntity },
                Relationships: null,
                Mappings: null
                );

            Dictionary<string, Entity> entityMap = new();
            entityMap.Add(entityName, sampleEntity);

            RuntimeConfig runtimeConfig = new(
                Schema: "UnitTestSchema",
                MsSql: null,
                CosmosDb: null,
                PostgreSql: null,
                MySql: null,
                DataSource: new DataSource(DatabaseType: DatabaseType.mssql),
                RuntimeSettings: new Dictionary<GlobalSettingsType, object>(),
                Entities: entityMap
                );

            return runtimeConfig;
        }

        /// <summary>
        /// Helper which creates a TableDefinition with the number of columns defined.
        /// Column names will be of form "colX" where x is an integer starting at 1.
        /// There will be as many columns as denoted in the columnCount parameter.
        /// </summary>
        /// <param name="columnCount">Number of columns to create.</param>
        /// <returns>Sample TableDefinition object</returns>
        public static TableDefinition CreateSampleTable(int columnCount = 4)
        {
            Assert.IsTrue(columnCount > 0);

            TableDefinition tableDefinition = new();

            for (int count = 1; count <= columnCount; count++)
            {
                string columnName = "col" + count;
                tableDefinition.Columns.Add(key: columnName, value: new ColumnDefinition());
            }

            return tableDefinition;
        }
    }
}
