// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Resolvers.Factories
{
    /// <summary>
    /// AbstractQueryManager Factory.
    /// Used to get IQueryBuilder, IQueryExecutor and DbExceptionParser based on database type.
    /// </summary>
    public interface IAbstractQueryManagerFactory
    {
        /// <summary>
        /// Gets Query Builder based on database type.
        /// </summary>
        /// <param name="databaseType">databaseType.</param>
        /// <returns>IQuerybuilder based on databaseType.</returns>
        public IQueryBuilder GetQueryBuilder(DatabaseType databaseType);

        /// <summary>
        /// Gets Query Executor based on database type.
        /// </summary>
        /// <param name="databaseType">databaseType.</param>
        /// <returns>IQueryExecutor based on databaseType.</returns>
        public IQueryExecutor GetQueryExecutor(DatabaseType databaseType);

        /// <summary>
        /// Gets DbExceptionParser based on database type.
        /// </summary>
        /// <param name="databaseType">databaseType.</param>
        /// <returns>DBExceptionParser based on databaseType</returns>
        public DbExceptionParser GetDbExceptionParser(DatabaseType databaseType);
    }
}
