// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data.Common;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Database exception used to exercise provider-independent MCP error handling.
    /// </summary>
    internal sealed class FakeDbException : DbException
    {
        public FakeDbException() : base("fake db error")
        {
        }

        public FakeDbException(string message) : base(message)
        {
        }

        public FakeDbException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
