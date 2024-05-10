// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// Identifies a specific value pair:
    /// 1. entity name
    /// 2. relationship name
    /// Which can be used to uniquely identify a relationship (ForeignKeyDefinition object(s)).
    /// </summary>
    [DebuggerDisplay("{EntityName} - {RelationshipName}")]
    public class EntityRelationshipKey
    {
        /// <summary>
        /// Source entity name which contains the relationship configuration.
        /// </summary>
        public string EntityName { get; set; }
        public string RelationshipName { get; set; }

        public EntityRelationshipKey(string entityName, string relationshipName)
        {
            EntityName = entityName;
            RelationshipName = relationshipName;
        }

        public override bool Equals(object? other)
        {
            return Equals(other as EntityRelationshipKey);
        }

        public bool Equals(EntityRelationshipKey? other)
        {
            if (other == null)
            {
                return false;
            }

            return EntityName.Equals(other.EntityName) && RelationshipName.Equals(other.RelationshipName);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(EntityName, RelationshipName);
        }
    }
}
