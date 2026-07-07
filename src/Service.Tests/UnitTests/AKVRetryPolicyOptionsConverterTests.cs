// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for <c>AKVRetryPolicyOptionsConverterFactory</c> covering JSON
/// read/write of Azure Key Vault retry policy options.
/// </summary>
[TestClass]
public class AKVRetryPolicyOptionsConverterTests
{
    private static JsonSerializerOptions GetOptions()
    {
        return RuntimeConfigLoader.GetSerializationOptions();
    }

    [TestMethod]
    public void Deserialize_AllProperties_AreReadCorrectly()
    {
        string json = @"{
            ""mode"": ""Fixed"",
            ""max-count"": 5,
            ""delay-seconds"": 2,
            ""max-delay-seconds"": 30,
            ""network-timeout-seconds"": 45
        }";

        AKVRetryPolicyOptions options = JsonSerializer.Deserialize<AKVRetryPolicyOptions>(json, GetOptions());

        Assert.IsNotNull(options);
        Assert.AreEqual(AKVRetryPolicyMode.Fixed, options.Mode);
        Assert.AreEqual(5, options.MaxCount);
        Assert.AreEqual(2, options.DelaySeconds);
        Assert.AreEqual(30, options.MaxDelaySeconds);
        Assert.AreEqual(45, options.NetworkTimeoutSeconds);
        Assert.IsTrue(options.UserProvidedMode);
        Assert.IsTrue(options.UserProvidedMaxCount);
        Assert.IsTrue(options.UserProvidedDelaySeconds);
        Assert.IsTrue(options.UserProvidedMaxDelaySeconds);
        Assert.IsTrue(options.UserProvidedNetworkTimeoutSeconds);
    }

    [TestMethod]
    public void Deserialize_NullValues_FallBackToDefaults()
    {
        string json = @"{
            ""mode"": null,
            ""max-count"": null,
            ""delay-seconds"": null,
            ""max-delay-seconds"": null,
            ""network-timeout-seconds"": null
        }";

        AKVRetryPolicyOptions options = JsonSerializer.Deserialize<AKVRetryPolicyOptions>(json, GetOptions());

        Assert.IsNotNull(options);
        Assert.AreEqual(AKVRetryPolicyOptions.DEFAULT_MODE, options.Mode);
        Assert.AreEqual(AKVRetryPolicyOptions.DEFAULT_MAX_COUNT, options.MaxCount);
        Assert.AreEqual(AKVRetryPolicyOptions.DEFAULT_DELAY_SECONDS, options.DelaySeconds);
        Assert.AreEqual(AKVRetryPolicyOptions.DEFAULT_MAX_DELAY_SECONDS, options.MaxDelaySeconds);
        Assert.AreEqual(AKVRetryPolicyOptions.DEFAULT_NETWORK_TIMEOUT_SECONDS, options.NetworkTimeoutSeconds);
        Assert.IsFalse(options.UserProvidedMode);
        Assert.IsFalse(options.UserProvidedMaxCount);
        Assert.IsFalse(options.UserProvidedDelaySeconds);
        Assert.IsFalse(options.UserProvidedMaxDelaySeconds);
        Assert.IsFalse(options.UserProvidedNetworkTimeoutSeconds);
    }

    [TestMethod]
    public void Deserialize_EmptyObject_ReturnsDefaults()
    {
        AKVRetryPolicyOptions options = JsonSerializer.Deserialize<AKVRetryPolicyOptions>("{}", GetOptions());

        Assert.IsNotNull(options);
        Assert.IsFalse(options.UserProvidedMode);
        Assert.IsFalse(options.UserProvidedMaxCount);
    }

    [DataTestMethod]
    [DataRow(@"{ ""max-count"": -1 }", DisplayName = "Negative max-count")]
    [DataRow(@"{ ""delay-seconds"": 0 }", DisplayName = "Zero delay-seconds")]
    [DataRow(@"{ ""max-delay-seconds"": 0 }", DisplayName = "Zero max-delay-seconds")]
    [DataRow(@"{ ""network-timeout-seconds"": -5 }", DisplayName = "Negative network-timeout-seconds")]
    public void Deserialize_InvalidValues_ThrowJsonException(string json)
    {
        Assert.ThrowsException<JsonException>(
            () => JsonSerializer.Deserialize<AKVRetryPolicyOptions>(json, GetOptions()));
    }

    [TestMethod]
    public void Deserialize_NonObjectToken_ThrowsJsonException()
    {
        Assert.ThrowsException<JsonException>(
            () => JsonSerializer.Deserialize<AKVRetryPolicyOptions>("\"not-an-object\"", GetOptions()));
    }

    [TestMethod]
    public void Serialize_OnlyWritesUserProvidedProperties()
    {
        AKVRetryPolicyOptions options = new(mode: AKVRetryPolicyMode.Exponential, maxCount: 7);

        string json = JsonSerializer.Serialize(options, GetOptions());
        JObject jObject = JObject.Parse(json);

        Assert.IsTrue(jObject.ContainsKey("mode"));
        Assert.IsTrue(jObject.ContainsKey("max-count"));
        Assert.IsFalse(jObject.ContainsKey("delay-seconds"));
        Assert.IsFalse(jObject.ContainsKey("max-delay-seconds"));
        Assert.IsFalse(jObject.ContainsKey("network-timeout-seconds"));
        Assert.AreEqual(7, jObject["max-count"].Value<int>());
    }

    [TestMethod]
    public void Serialize_NoUserProvidedProperties_WritesEmptyObject()
    {
        AKVRetryPolicyOptions options = new();

        string json = JsonSerializer.Serialize(options, GetOptions());
        JObject jObject = JObject.Parse(json);

        Assert.AreEqual(0, jObject.Count);
    }

    [TestMethod]
    public void RoundTrip_PreservesUserProvidedValues()
    {
        AKVRetryPolicyOptions original = new(
            mode: AKVRetryPolicyMode.Fixed,
            maxCount: 4,
            delaySeconds: 3,
            maxDelaySeconds: 20,
            networkTimeoutSeconds: 50);

        string json = JsonSerializer.Serialize(original, GetOptions());
        AKVRetryPolicyOptions result = JsonSerializer.Deserialize<AKVRetryPolicyOptions>(json, GetOptions());

        Assert.AreEqual(original.Mode, result.Mode);
        Assert.AreEqual(original.MaxCount, result.MaxCount);
        Assert.AreEqual(original.DelaySeconds, result.DelaySeconds);
        Assert.AreEqual(original.MaxDelaySeconds, result.MaxDelaySeconds);
        Assert.AreEqual(original.NetworkTimeoutSeconds, result.NetworkTimeoutSeconds);
    }
}
