// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="EnumExtensions"/> string-to-enum conversion helpers,
    /// covering both plain enum names and <c>EnumMember</c>-attributed values.
    /// Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class EnumExtensionsTests
    {
        [TestMethod]
        public void Deserialize_EnumMemberValue_IsResolved()
        {
            Assert.AreEqual(EntitySourceType.StoredProcedure, EnumExtensions.Deserialize<EntitySourceType>("stored-procedure"));
            Assert.AreEqual(EntitySourceType.Table, EnumExtensions.Deserialize<EntitySourceType>("table"));
        }

        [DataTestMethod]
        [DataRow("Fixed", AKVRetryPolicyMode.Fixed, DisplayName = "Exact enum name")]
        [DataRow("exponential", AKVRetryPolicyMode.Exponential, DisplayName = "Lower-case enum name")]
        [DataRow("EXPONENTIAL", AKVRetryPolicyMode.Exponential, DisplayName = "Upper-case enum name")]
        public void Deserialize_EnumName_IsCaseInsensitive(string value, AKVRetryPolicyMode expected)
        {
            Assert.AreEqual(expected, EnumExtensions.Deserialize<AKVRetryPolicyMode>(value));
        }

        [TestMethod]
        public void Deserialize_InvalidValue_ThrowsJsonException()
        {
            Assert.ThrowsException<JsonException>(
                () => EnumExtensions.Deserialize<AKVRetryPolicyMode>("not-a-mode"));
        }

        [TestMethod]
        public void TryDeserialize_ValidValue_ReturnsTrue()
        {
            bool ok = EnumExtensions.TryDeserialize("Fixed", out AKVRetryPolicyMode? mode);

            Assert.IsTrue(ok);
            Assert.AreEqual(AKVRetryPolicyMode.Fixed, mode);
        }

        [TestMethod]
        public void TryDeserialize_InvalidValue_ReturnsFalse()
        {
            bool ok = EnumExtensions.TryDeserialize("bogus", out AKVRetryPolicyMode? mode);

            Assert.IsFalse(ok);
            Assert.IsNull(mode);
        }

        [TestMethod]
        public void GenerateMessageForInvalidInput_ListsValidValues()
        {
            string message = EnumExtensions.GenerateMessageForInvalidInput<AKVRetryPolicyMode>("bogus");

            StringAssert.Contains(message, "bogus");
            StringAssert.Contains(message, "Fixed");
            StringAssert.Contains(message, "Exponential");
        }
    }
}
