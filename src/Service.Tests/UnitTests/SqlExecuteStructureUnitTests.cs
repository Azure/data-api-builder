// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class SqlExecuteStructureUnitTests
    {
        [TestMethod]
        public void ConfigDefaultDateTimeParameterIsResolvedAsDateTime()
        {
            const string entityName = "GetRecordsByDate";
            const string parameterName = "YearEndDate";
            const string envVarName = "year-end";
            DateTime expectedDate = new(year: 2024, month: 6, day: 30, hour: 0, minute: 0, second: 0, kind: DateTimeKind.Utc);

            Environment.SetEnvironmentVariable(envVarName, "2024-06-30");
            try
            {
                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(
                    GetStoredProcedureConfigWithEnvDefault(),
                    out RuntimeConfig runtimeConfig,
                    replacementSettings: new DeserializationVariableReplacementSettings(
                        azureKeyVaultOptions: null,
                        doReplaceEnvVar: true,
                        doReplaceAkvVar: false)));

                StoredProcedureDefinition storedProcedureDefinition = new();
                storedProcedureDefinition.Parameters.Add(
                    parameterName,
                    new ParameterDefinition
                    {
                        SystemType = typeof(DateTime),
                        DbType = DbType.DateTime,
                        HasConfigDefault = true,
                        ConfigDefaultValue = runtimeConfig.Entities[entityName].Source.Parameters[0].Default
                    });

                DatabaseStoredProcedure storedProcedure = new(schemaName: "dbo", tableName: "get_records_by_date")
                {
                    SourceType = EntitySourceType.StoredProcedure,
                    StoredProcedureDefinition = storedProcedureDefinition
                };

                Mock<ISqlMetadataProvider> metadataProvider = new();
                metadataProvider.SetupGet(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>
                {
                    { entityName, storedProcedure }
                });
                metadataProvider.Setup(x => x.GetStoredProcedureDefinition(entityName)).Returns(storedProcedureDefinition);
                metadataProvider.Setup(x => x.GetSourceDefinition(entityName)).Returns(storedProcedureDefinition);

                SqlExecuteStructure executeStructure = new(
                    entityName,
                    metadataProvider.Object,
                    Mock.Of<IAuthorizationResolver>(),
                    null,
                    new Dictionary<string, object?>());

                string dbConnectionParameterName = (string)executeStructure.ProcedureParameters[parameterName];
                Assert.AreEqual(expectedDate, executeStructure.Parameters[dbConnectionParameterName].Value);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, null);
            }
        }

        private static string GetStoredProcedureConfigWithEnvDefault()
        {
            return @"
            {
                ""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""Server=tcp:127.0.0.1,1433;Persist Security Info=False;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connection Timeout=5;""
                },
                ""entities"": {
                    ""GetRecordsByDate"": {
                        ""source"": {
                            ""object"": ""get_records_by_date"",
                            ""type"": ""stored-procedure"",
                            ""parameters"": {
                                ""YearEndDate"": ""@env('year-end')""
                            }
                        },
                        ""permissions"": [
                            {
                                ""role"": ""anonymous"",
                                ""actions"": [
                                    {
                                        ""action"": ""execute""
                                    }
                                ]
                            }
                        ]
                    }
                }
            }";
        }
    }
}
