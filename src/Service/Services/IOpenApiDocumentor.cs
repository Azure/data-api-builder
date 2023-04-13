// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Azure.DataApiBuilder.Service.Services
{
    public interface IOpenApiDocumentor
    {
        public bool TryGetDocument([NotNullWhen(true)] out string? document);
        public void CreateDocument();
    }
}
