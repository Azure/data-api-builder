// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// It contains the path to the entity with entity name.
    /// It is used to build the query from OdataParser as it would use path to an entity as prefix in the condition.
    /// </summary>
    public record EntityDbPolicyCosmosModel
    {
        /// <summary>
        /// Path to the given entity with "." delimiter
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Column name of the entity
        /// </summary>
        public string? ColumnName { get; }

        /// <summary>
        /// If entity is of array type then we would store alias of the entity
        /// </summary>
        public string? Alias { get; }

        /// <summary>
        /// Entity Name
        /// </summary>
        public string? EntityName { get; }

        /// <summary>
        /// Contains pre-generated JOIN statement for the entity
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
