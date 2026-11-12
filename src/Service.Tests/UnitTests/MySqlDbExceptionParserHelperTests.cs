// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using System.Reflection;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySqlConnector;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlDbExceptionParserHelperTests
    {
        [DataTestMethod]
        [DataRow(1020, true)]
        [DataRow(9999, false)]
        public void IsTransientException_UsesMySqlErrorNumber(int errorNumber, bool expected)
        {
            MySqlDbExceptionParser parser = CreateParser();

            Assert.AreEqual(expected, parser.IsTransientException(CreateException(errorNumber)));
        }

        [TestMethod]
        public void GetHttpStatusCodeForException_UnknownNumberReturnsInternalServerError()
        {
            MySqlDbExceptionParser parser = CreateParser();

            Assert.AreEqual(
                HttpStatusCode.InternalServerError,
                parser.GetHttpStatusCodeForException(CreateException(9999)));
        }

        private static MySqlException CreateException(int errorNumber)
        {
            ConstructorInfo constructor = typeof(MySqlException).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(MySqlErrorCode), typeof(string) },
                modifiers: null)!;
            return (MySqlException)constructor.Invoke(new object[]
            {
                (MySqlErrorCode)errorNumber,
                "Test MySQL exception."
            });
        }

        private static MySqlDbExceptionParser CreateParser()
        {
            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.MySQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);
            return new MySqlDbExceptionParser(configProvider);
        }
    }
}
