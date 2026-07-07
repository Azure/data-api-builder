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
/// Unit tests for <c>AzureKeyVaultOptionsConverterFactory</c> covering JSON
/// read/write of Azure Key Vault options including nested retry policy.
/// </summary>
[TestClass]
public class AzureKeyVaultOptionsConverterTests
{
    private static JsonSerializerOptions GetOptions()
    {
        return RuntimeConfigLoader.GetSerializationOptions();
    }

    [TestMethod]
    public void Deserialize_EndpointAndRetryPolicy_AreReadCorrectly()
    {
        string json = @"{
            ""endpoint"": ""https://my-vault.vault.azure.net/"",
            ""retry-policy"": {
                ""mode"": ""Fixed"",
                ""max-count"": 5
            }
        }";

        AzureKeyVaultOptions options = JsonSerializer.Deserialize<AzureKeyVaultOptions>(json, GetOptions());

        Assert.IsNotNull(options);
        Assert.AreEqual("https://my-vault.vault.azure.net/", options.Endpoint);
        Assert.IsTrue(options.UserProvidedEndpoint);
        Assert.IsTrue(options.UserProvidedRetryPolicy);
        Assert.IsNotNull(options.RetryPolicy);
        Assert.AreEqual(AKVRetryPolicyMode.Fixed, options.RetryPolicy.Mode);
        Assert.AreEqual(5, options.RetryPolicy.MaxCount);
    }

    [TestMethod]
    public void Deserialize_OnlyEndpoint_RetryPolicyIsNull()
    {
        string json = @"{ ""endpoint"": ""https://my-vault.vault.azure.net/"" }";

        AzureKeyVaultOptions options = JsonSerializer.Deserialize<AzureKeyVaultOptions>(json, GetOptions());

        Assert.IsNotNull(options);
        Assert.IsTrue(options.UserProvidedEndpoint);
        Assert.IsFalse(options.UserProvidedRetryPolicy);
        Assert.IsNull(options.RetryPolicy);
    }

    [TestMethod]
    public void Deserialize_EmptyObject_HasNoUserProvidedValues()
    {
        AzureKeyVaultOptions options = JsonSerializer.Deserialize<AzureKeyVaultOptions>("{}", GetOptions());

        Assert.IsNotNull(options);
        Assert.IsFalse(options.UserProvidedEndpoint);
        Assert.IsFalse(options.UserProvidedRetryPolicy);
    }

    [TestMethod]
    public void Deserialize_NullToken_ReturnsNull()
    {
        AzureKeyVaultOptions options = JsonSerializer.Deserialize<AzureKeyVaultOptions>("null", GetOptions());

        Assert.IsNull(options);
    }

    [TestMethod]
    public void Deserialize_UnexpectedProperty_ThrowsJsonException()
    {
        string json = @"{ ""unexpected"": ""value"" }";

        Assert.ThrowsException<JsonException>(
            () => JsonSerializer.Deserialize<AzureKeyVaultOptions>(json, GetOptions()));
    }

    [TestMethod]
    public void Deserialize_NonObjectToken_ThrowsJsonException()
    {
        Assert.ThrowsException<JsonException>(
            () => JsonSerializer.Deserialize<AzureKeyVaultOptions>("\"not-an-object\"", GetOptions()));
    }

    [TestMethod]
    public void Serialize_OnlyWritesUserProvidedProperties()
    {
        AzureKeyVaultOptions options = new(endpoint: "https://my-vault.vault.azure.net/");

        string json = JsonSerializer.Serialize(options, GetOptions());
        JObject jObject = JObject.Parse(json);

        Assert.IsTrue(jObject.ContainsKey("endpoint"));
        Assert.IsFalse(jObject.ContainsKey("retry-policy"));
    }

    [TestMethod]
    public void Serialize_NoUserProvidedProperties_WritesEmptyObject()
    {
        AzureKeyVaultOptions options = new();

        string json = JsonSerializer.Serialize(options, GetOptions());
        JObject jObject = JObject.Parse(json);

        Assert.AreEqual(0, jObject.Count);
    }

    [TestMethod]
    public void RoundTrip_PreservesEndpointAndRetryPolicy()
    {
        AzureKeyVaultOptions original = new(
            endpoint: "https://my-vault.vault.azure.net/",
            retryPolicy: new AKVRetryPolicyOptions(mode: AKVRetryPolicyMode.Exponential, maxCount: 6));

        string json = JsonSerializer.Serialize(original, GetOptions());
        AzureKeyVaultOptions result = JsonSerializer.Deserialize<AzureKeyVaultOptions>(json, GetOptions());

        Assert.AreEqual(original.Endpoint, result.Endpoint);
        Assert.IsNotNull(result.RetryPolicy);
        Assert.AreEqual(original.RetryPolicy.Mode, result.RetryPolicy.Mode);
        Assert.AreEqual(original.RetryPolicy.MaxCount, result.RetryPolicy.MaxCount);
    }
}
