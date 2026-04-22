// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

[TestClass]
public class ExecutionHelperUnitTests
{
    [TestMethod]
    public void ExtractValueFromIValueNode_DateTimeLiteral_ReturnsUtcDateTime()
    {
        Mock<IInputValueDefinition> argumentSchema = CreateArgumentSchema(new DateTimeType());
        Mock<IVariableValueCollection> variables = new();

        object result = ExecutionHelper.ExtractValueFromIValueNode(
            new StringValueNode("2026-04-22T10:15:30-07:00"),
            argumentSchema.Object,
            variables.Object);

        Assert.IsInstanceOfType<DateTime>(result);
        Assert.AreEqual(
            new DateTime(2026, 4, 22, 17, 15, 30, DateTimeKind.Utc),
            (DateTime)result);
    }

    [TestMethod]
    public void ExtractValueFromIValueNode_DateTimeVariable_ReturnsUtcDateTime()
    {
        Mock<IInputValueDefinition> argumentSchema = CreateArgumentSchema(new DateTimeType());
        Mock<IVariableValueCollection> variables = new();
        variables
            .Setup(v => v.GetValue<IValueNode>("createdAt"))
            .Returns(new StringValueNode("2026-04-22T10:15:30Z"));

        object result = ExecutionHelper.ExtractValueFromIValueNode(
            new VariableNode("createdAt"),
            argumentSchema.Object,
            variables.Object);

        Assert.IsInstanceOfType<DateTime>(result);
        Assert.AreEqual(
            new DateTime(2026, 4, 22, 10, 15, 30, DateTimeKind.Utc),
            (DateTime)result);
    }

    private static Mock<IInputValueDefinition> CreateArgumentSchema(IInputType type)
    {
        Mock<IInputValueDefinition> argumentSchema = new();
        argumentSchema.SetupGet(a => a.Type).Returns(type);
        return argumentSchema;
    }
}