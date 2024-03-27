// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    [DebuggerDisplay("{EntityName} - {RelationshipName}")]
    public class EntityRelationshipKey
    {
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

            return EntityName == other.EntityName && RelationshipName == other.RelationshipName;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(EntityName, RelationshipName);
        }
    }
}
