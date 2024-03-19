// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// It contains the path to the entity with entity name.
    /// It is used to build the query from OdataParser as it would use path to an entity as prefix in the condition.
    /// </summary>
    public record EntityPrefix
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

        public EntityPrefix(string Path, string? EntityName, string? ColumnName = null, string? Alias = null)
        {
            this.Path = Path;
            this.ColumnName = ColumnName;
            this.Alias = Alias;
            this.EntityName = EntityName;
        }
    }
}
