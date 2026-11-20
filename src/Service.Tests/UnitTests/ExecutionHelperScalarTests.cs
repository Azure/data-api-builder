// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Reflection;
using System.Text.Json;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NodaTime;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class ExecutionHelperScalarTests
    {
        [TestMethod]
        public void CoerceJsonLeafValueToRuntimeType_ConvertsNumericTypes()
        {
            Assert.AreEqual((byte)7, Coerce("7", new UnsignedByteType()));
            Assert.AreEqual((short)-12, Coerce("-12", new ShortType()));
            Assert.AreEqual(42, Coerce("42", new IntType()));
            Assert.AreEqual(9007199254740991L, Coerce("9007199254740991", new LongType()));
            Assert.AreEqual(1.25d, Coerce("1.25", new FloatType()));
            Assert.AreEqual(1.25f, Coerce("1.25", new SingleType()));
            Assert.AreEqual(1.25m, Coerce("1.25", new DecimalType()));
        }

        [TestMethod]
        public void CoerceJsonLeafValueToRuntimeType_ConvertsTextualTypes()
        {
            Assert.AreEqual("text", Coerce("\"text\"", new StringType()));
            Assert.AreEqual(new Uri("https://example.test/path"), Coerce("\"https://example.test/path\"", new UrlType()));
            Guid id = Guid.NewGuid();
            Assert.AreEqual(id, Coerce($"\"{id}\"", new UuidType()));
            Assert.AreEqual(TimeSpan.FromMinutes(90), Coerce("\"PT1H30M\"", new DurationType()));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])Coerce("\"AQID\"", new Base64StringType())!);
            Assert.AreEqual("text", Coerce("\"text\"", new AnyType()));
        }

        [TestMethod]
        public void CoerceJsonLeafValueToRuntimeType_ConvertsTemporalAndBooleanTypes()
        {
            Assert.AreEqual(true, Coerce("true", new BooleanType()));
            Assert.AreEqual(false, Coerce("false", new BooleanType()));
            Assert.AreEqual(DateTimeOffset.Parse("2026-08-28T12:30:00Z"), Coerce("\"2026-08-28T12:30:00Z\"", new DateTimeType()));
            Assert.AreEqual(DateTimeOffset.Parse("2026-08-28"), Coerce("\"2026-08-28\"", new DateType()));
            Assert.AreEqual(new LocalTime(12, 30, 15), Coerce("\"12:30:15\"", new HotChocolate.Types.NodaTime.LocalTimeType()));
            Assert.IsNull(Coerce("\"null\"", new HotChocolate.Types.NodaTime.LocalTimeType()));
        }

        [TestMethod]
        public void CoerceJsonLeafValueToRuntimeType_NullAndInvalidTemporalValuesReturnNull()
        {
            Assert.IsNull(Coerce("null", new StringType()));
            Assert.IsNull(Coerce("\"not-a-date\"", new DateTimeType()));
            Assert.IsNull(Coerce("\"not-a-date\"", new DateType()));
        }

        [TestMethod]
        public void CoerceJsonLeafValueToRuntimeType_InvalidRepresentationThrowsMappedException()
        {
            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                Coerce("\"not-an-integer\"", new IntType()));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        private static object? Coerce(string json, ITypeDefinition type)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            MethodInfo method = typeof(ExecutionHelper).GetMethod(
                "CoerceJsonLeafValueToRuntimeType",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            return method.Invoke(null, new object[] { document.RootElement, type, "field" });
        }
    }
}
