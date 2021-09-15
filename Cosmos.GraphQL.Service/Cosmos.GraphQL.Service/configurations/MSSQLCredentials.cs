﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.configurations
{
    /// <summary>
    /// Representation of MSSQL specific credential properties
    /// </summary>
    public class MSSQLCredentials : IDatabaseCredentials
    {
        /// <summary>
        /// Fully constructed connection string with user credentials. 
        /// </summary>
        public string ConnectionString { get; set; }
    }
}
