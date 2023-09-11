// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Resolvers.Factories
{
    /// <summary>
    /// QueryEngineFactory interface.
    /// Used in DI container to retrieve appropriate queryEngine
    /// </summary>
    public interface IQueryEngineFactory
    {
        /// <summary>
        /// Gets the QueryEngine based on database type.
        /// </summary>
        /// <param name="databaseType">databaseType.</param>
        /// <returns>QueryEngine based on database type.</returns>
        public IQueryEngine GetQueryEngine(DatabaseType databaseType);
    }
}
