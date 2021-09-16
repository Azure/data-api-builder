using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.configurations
{
    /// <summary>
    /// This interface defines the common connection field utilized between Cosmos/SQL. 
    /// Classes that implement this interface can also add their own db specific fields.
    /// </summary>
    public interface IDatabaseCredentials
    {
        /// <summary>
        /// ConnectionString represents the minimal info needed to connect to a given database.
        /// It is the common factor between Cosmos DB and SQL DB. Pending Postgres.
        /// </summary>
        public string ConnectionString { get; set; }
    }
}
