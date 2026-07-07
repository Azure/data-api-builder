// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable

using System.IO;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for <c>DmlToolsConfigConverter</c> covering the boolean and object JSON
/// formats supported for MCP DML tool configuration.
/// </summary>
[TestClass]
public class DmlToolsConfigConverterTests
{
    private static JsonSerializerOptions GetOptions()
    {
        return RuntimeConfigLoader.GetSerializationOptions();
    }

    /// <summary>
    /// Serializes a DmlToolsConfig the same way the parent McpRuntimeOptions converter does:
    /// within a containing JSON object (the converter writes a "dml-tools" property).
    /// </summary>
    private static string SerializeWithinObject(DmlToolsConfig config)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
        {
            writer.WriteStartObject();
            JsonSerializer.Serialize(writer, config, GetOptions());
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [TestMethod]
    public void Deserialize_BooleanTrue_EnablesAllTools()
    {
        DmlToolsConfig config = JsonSerializer.Deserialize<DmlToolsConfig>("true", GetOptions());

        Assert.IsNotNull(config);
        Assert.IsTrue(config.AllToolsEnabled);
        Assert.IsTrue(config.CreateRecord);
        Assert.IsTrue(config.ReadRecords);
        Assert.IsTrue(config.UpdateRecord);
        Assert.IsTrue(config.DeleteRecord);
        Assert.IsTrue(config.ExecuteEntity);
        Assert.IsTrue(config.DescribeEntities);
        Assert.IsTrue(config.AggregateRecords);
    }

    [TestMethod]
    public void Deserialize_BooleanFalse_DisablesAllTools()
    {
        DmlToolsConfig config = JsonSerializer.Deserialize<DmlToolsConfig>("false", GetOptions());

        Assert.IsNotNull(config);
        Assert.IsFalse(config.AllToolsEnabled);
        Assert.IsFalse(config.CreateRecord);
        Assert.IsFalse(config.ReadRecords);
        Assert.IsFalse(config.UpdateRecord);
        Assert.IsFalse(config.DeleteRecord);
        Assert.IsFalse(config.ExecuteEntity);
        Assert.IsFalse(config.DescribeEntities);
        Assert.IsFalse(config.AggregateRecords);
    }

    [TestMethod]
    public void Deserialize_ObjectWithIndividualSettings_AppliesOverrides()
    {
        string json = @"{
            ""create-record"": false,
            ""delete-record"": false,
            ""read-records"": true
        }";

        DmlToolsConfig config = JsonSerializer.Deserialize<DmlToolsConfig>(json, GetOptions());

        Assert.IsNotNull(config);
        Assert.IsFalse(config.CreateRecord);
        Assert.IsFalse(config.DeleteRecord);
        Assert.IsTrue(config.ReadRecords);
        // Unspecified tools default to enabled.
        Assert.IsTrue(config.UpdateRecord);
        Assert.IsTrue(config.ExecuteEntity);
        Assert.IsTrue(config.DescribeEntities);
    }

    [TestMethod]
    public void Deserialize_AggregateRecordsBoolean_IsRead()
    {
        string json = @"{ ""aggregate-records"": false }";

        DmlToolsConfig config = JsonSerializer.Deserialize<DmlToolsConfig>(json, GetOptions());

        Assert.IsNotNull(config);
        Assert.IsFalse(config.AggregateRecords);
        Assert.IsTrue(config.UserProvidedAggregateRecords);
    }

    [TestMethod]
    public void Deserialize_AggregateRecordsObject_ReadsEnabledAndTimeout()
    {
        string json = @"{
            ""aggregate-records"": {
                ""enabled"": true,
                ""query-timeout"": 60
            }
        }";

        DmlToolsConfig config = JsonSerializer.Deserialize<DmlToolsConfig>(json, GetOptions());

        Assert.IsNotNull(config);
        Assert.IsTrue(config.AggregateRecords);
        Assert.AreEqual(60, config.AggregateRecordsQueryTimeout);
        Assert.IsTrue(config.UserProvidedAggregateRecordsQueryTimeout);
    }

    [TestMethod]
    public void Deserialize_AggregateRecordsObject_NullTimeout_IsIgnored()
    {
        string json = @"{
            ""aggregate-records"": {
                ""enabled"": false,
                ""query-timeout"": null
            }
        }";

        DmlToolsConfig config = JsonSerializer.Deserialize<DmlToolsConfig>(json, GetOptions());

        Assert.IsNotNull(config);
        Assert.IsFalse(config.AggregateRecords);
        Assert.IsNull(config.AggregateRecordsQueryTimeout);
    }

    [TestMethod]
    public void Deserialize_AggregateRecordsObject_UnknownSubProperty_IsSkipped()
    {
        string json = @"{
            ""aggregate-records"": {
                ""enabled"": true,
                ""bogus"": 123
            }
        }";

        DmlToolsConfig config = JsonSerializer.Deserialize<DmlToolsConfig>(json, GetOptions());

        Assert.IsNotNull(config);
        Assert.IsTrue(config.AggregateRecords);
    }

    [TestMethod]
    public void Deserialize_UnknownProperty_IsSkipped()
    {
        string json = @"{ ""unknown-tool"": true, ""create-record"": false }";

        DmlToolsConfig config = JsonSerializer.Deserialize<DmlToolsConfig>(json, GetOptions());

        Assert.IsNotNull(config);
        Assert.IsFalse(config.CreateRecord);
    }

    [TestMethod]
    public void Deserialize_NonBooleanForKnownProperty_ThrowsJsonException()
    {
        string json = @"{ ""create-record"": ""yes"" }";

        Assert.ThrowsException<JsonException>(
            () => JsonSerializer.Deserialize<DmlToolsConfig>(json, GetOptions()));
    }

    [TestMethod]
    public void Deserialize_AggregateRecordsInvalidType_ThrowsJsonException()
    {
        string json = @"{ ""aggregate-records"": ""bad"" }";

        Assert.ThrowsException<JsonException>(
            () => JsonSerializer.Deserialize<DmlToolsConfig>(json, GetOptions()));
    }

    [TestMethod]
    public void Serialize_FromBooleanTrue_WritesBooleanForm()
    {
        DmlToolsConfig config = DmlToolsConfig.FromBoolean(true);

        string json = SerializeWithinObject(config);
        JObject jObject = JObject.Parse(json);

        Assert.IsTrue(jObject.ContainsKey("dml-tools"));
        Assert.AreEqual(JTokenType.Boolean, jObject["dml-tools"].Type);
        Assert.IsTrue(jObject["dml-tools"].Value<bool>());
    }

    [TestMethod]
    public void Serialize_IndividualSettings_WritesObjectForm()
    {
        DmlToolsConfig config = new(createRecord: false, readRecords: true);

        string json = SerializeWithinObject(config);
        JObject jObject = JObject.Parse(json);

        JObject dmlTools = jObject["dml-tools"].Value<JObject>();
        Assert.IsNotNull(dmlTools);
        Assert.IsFalse(dmlTools["create-record"].Value<bool>());
        Assert.IsTrue(dmlTools["read-records"].Value<bool>());
        Assert.IsFalse(dmlTools.ContainsKey("update-record"), "Only user-provided tools should be written.");
    }

    [TestMethod]
    public void Serialize_AggregateRecordsWithTimeout_WritesObjectForm()
    {
        DmlToolsConfig config = new(aggregateRecords: true, aggregateRecordsQueryTimeout: 45);

        string json = SerializeWithinObject(config);
        JObject jObject = JObject.Parse(json);

        JObject aggregate = jObject["dml-tools"]["aggregate-records"].Value<JObject>();
        Assert.IsNotNull(aggregate);
        Assert.IsTrue(aggregate["enabled"].Value<bool>());
        Assert.AreEqual(45, aggregate["query-timeout"].Value<int>());
    }

    [TestMethod]
    public void Serialize_DefaultConfig_WritesNothing()
    {
        DmlToolsConfig config = DmlToolsConfig.Default;

        string json = SerializeWithinObject(config);
        JObject jObject = JObject.Parse(json);

        Assert.IsFalse(jObject.ContainsKey("dml-tools"), "Default config should not be written.");
    }
}
