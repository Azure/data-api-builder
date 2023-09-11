// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Resolvers.Factories
{
    /// <summary>
    /// MutationEngineFactory interface.
    /// Used in DI container to get the IMutationEngine based on database type.
    /// </summary>
    public interface IMutationEngineFactory
    {
        /// <summary>
        /// Gets the MutationEngine based on database type.
        /// </summary>
        /// <param name="databaseType">databaseType.</param>
        /// <returns>IMutationEngine based on database type..</returns>
        public IMutationEngine GetMutationEngine(DatabaseType databaseType);
    }
}
