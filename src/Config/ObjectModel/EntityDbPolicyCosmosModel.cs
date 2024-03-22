// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// It contains Entity information along with Pre-Generated JOIN statements for all the entities.
    /// So that, it can be used for generating CosmosDB SQL queries.
    /// </summary>
    public record EntityDbPolicyCosmosModel
    {
        /// <summary>
        /// Path to the given entity with "." delimiter, it will be used as prefix while generating conditions for CosmosDB SQL queries.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Column name representation of the entity
        /// </summary>
        public string? ColumnName { get; }

        /// <summary>
        /// If entity is of array type then we would have a generated alias of the entity
        /// </summary>
        public string? Alias { get; }

        /// <summary>
        /// Entity Name
        /// </summary>
        public string? EntityName { get; }

        /// <summary>
        /// Pre-generated JOIN statement for the entity
        /// </summary>
        public string? JoinStatement { get; set; }

        public EntityDbPolicyCosmosModel(string Path, string? EntityName, string? ColumnName = null, string? Alias = null)
        {
            this.Path = Path;
            this.ColumnName = ColumnName;
            this.Alias = Alias;
            this.EntityName = EntityName;

            // Generate JOIN statement only when Alias is there
            if (!string.IsNullOrEmpty(Alias))
            {
                this.JoinStatement = $" {Alias} IN {Path}.{ColumnName}";
            }
        }
    }
}
