// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

[TestClass, TestCategory("OracleUnit")]
public class OracleMetadataValidationTests
{
    [DataTestMethod]
    [DataRow("LONG")]
    [DataRow("LONG RAW")]
    public void SkippedQuotedColumnCannotOverwriteSupportedColumnMetadata(string sqlType)
    {
        MetadataProbe metadata = CreateProvider();
        SourceDefinition source = new();
        source.Columns.Add("payload", new()
        {
            SystemType = typeof(decimal),
            DatabaseTypeName = "NUMBER",
            HasDefault = true,
            DefaultValue = 7m
        });

        metadata.Populate(source, Columns(sqlType));

        Assert.AreEqual(1, source.Columns.Count);
        Assert.AreEqual("NUMBER", source.Columns["payload"].DatabaseTypeName);
        Assert.IsTrue(source.Columns["payload"].HasDefault);
        Assert.AreEqual(7m, source.Columns["payload"].DefaultValue);
    }

    [DataTestMethod]
    [DataRow("LONG")]
    [DataRow("LONG RAW")]
    [DataRow("BFILE")]
    [DataRow("XMLTYPE")]
    [DataRow("INTERVAL DAY(2) TO SECOND(6)")]
    [DataRow("INTERVAL YEAR(2) TO MONTH")]
    [DataRow("CUSTOM_OBJECT")]
    public void UnsupportedOracleTypesAreExcludedFromSchemaProjection(string sqlType)
    {
        MetadataProbe metadata = CreateProvider();
        SourceDefinition source = new();

        // Once schema projection omits the field, catalog enrichment must not add it back.
        metadata.Populate(source, Columns(sqlType));

        Assert.IsFalse(OracleMetadataProvider.IsSupportedColumnType(sqlType));
        Assert.AreEqual(0, source.Columns.Count);
    }

    [DataTestMethod]
    [DataRow("NUMBER")]
    [DataRow("FLOAT")]
    [DataRow("BINARY_FLOAT")]
    [DataRow("BINARY_DOUBLE")]
    [DataRow("DATE")]
    [DataRow("TIMESTAMP(6)")]
    [DataRow("TIMESTAMP(9) WITH TIME ZONE")]
    [DataRow("TIMESTAMP(6) WITH LOCAL TIME ZONE")]
    [DataRow("CHAR")]
    [DataRow("NCHAR")]
    [DataRow("VARCHAR2")]
    [DataRow("NVARCHAR2")]
    [DataRow("CLOB")]
    [DataRow("NCLOB")]
    [DataRow("RAW")]
    [DataRow("BLOB")]
    public void SupportedOracleTypesRemainInSchemaProjection(string sqlType)
    {
        Assert.IsTrue(OracleMetadataProvider.IsSupportedColumnType(sqlType), sqlType);
    }

    [DataTestMethod]
    [DataRow("CLOB", typeof(string), null)]
    [DataRow("NCLOB", typeof(string), null)]
    [DataRow("VARCHAR2", typeof(string), null)]
    [DataRow("RAW", typeof(byte[]), 16)]
    [DataRow("BLOB", typeof(byte[]), -1)]
    public void NativeColumnTypesAreRetainedForProviderSemantics(string sqlType, Type systemType, int? expectedLength)
    {
        MetadataProbe metadata = CreateProvider();
        SourceDefinition source = new();
        source.Columns.Add("PAYLOAD", new() { SystemType = systemType });

        metadata.Populate(source, Columns(sqlType));

        Assert.AreEqual(sqlType, source.Columns["PAYLOAD"].DatabaseTypeName);
        Assert.AreEqual(expectedLength, source.Columns["PAYLOAD"].Length);
        Assert.AreEqual(systemType, source.Columns["PAYLOAD"].SystemType);
    }

    private static DataTable Columns(string sqlType)
    {
        DataTable columns = new();
        columns.Columns.Add("COLUMN_NAME", typeof(string));
        columns.Columns.Add("DATA_TYPE", typeof(string));
        columns.Columns.Add("DATA_LENGTH", typeof(int));
        columns.Columns.Add("IDENTITY_COLUMN", typeof(string));
        columns.Columns.Add("GENERATION_TYPE", typeof(string));
        columns.Columns.Add("COLUMN_DEFAULT", typeof(object));
        columns.Rows.Add("PAYLOAD", sqlType, 16, "NO", DBNull.Value, DBNull.Value);
        return columns;
    }

    private static MetadataProbe CreateProvider()
    {
        OracleTestFixture fixture = new();
        RuntimeConfigValidator validator = new(fixture.Config, new MockFileSystem(), NullLogger<RuntimeConfigValidator>.Instance);
        QueryManagerFactory factory = new(fixture.Config, NullLogger<IQueryExecutor>.Instance, fixture.Http, null);
        return new(fixture.Config, validator, factory);
    }

    private sealed class MetadataProbe(RuntimeConfigProvider config, RuntimeConfigValidator validator, QueryManagerFactory factory)
        : OracleMetadataProvider(config, validator, factory, NullLogger<ISqlMetadataProvider>.Instance, config.GetConfig().DefaultDataSourceName)
    {
        public void Populate(SourceDefinition source, DataTable columns) => PopulateColumnDefinitionWithHasDefaultAndDbType(source, columns);
    }
}
