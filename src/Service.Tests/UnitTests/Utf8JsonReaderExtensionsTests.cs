// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config.Converters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for <c>Utf8JsonReaderExtensions.DeserializeString</c>.
/// Pure logic; no database required.
/// </summary>
[TestClass]
public class Utf8JsonReaderExtensionsTests
{
    private static string? ReadFirstString(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        Utf8JsonReader reader = new(bytes);
        reader.Read();
        return reader.DeserializeString(replacementSettings: null);
    }

    [TestMethod]
    public void DeserializeString_StringToken_ReturnsValue()
    {
        Assert.AreEqual("hello", ReadFirstString("\"hello\""));
    }

    [TestMethod]
    public void DeserializeString_NullToken_ReturnsNull()
    {
        Assert.IsNull(ReadFirstString("null"));
    }

    [TestMethod]
    public void DeserializeString_NonStringToken_ThrowsJsonException()
    {
        Assert.ThrowsException<JsonException>(() => ReadFirstString("42"));
    }
}
