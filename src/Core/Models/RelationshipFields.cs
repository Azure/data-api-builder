// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models
{
    /// <summary>
    /// Class to represent a set of source/target fields for a relationship between source and target entities.
    /// </summary>
    public class RelationshipFields
    {
        // Relationship fields in source entity.
        public List<string> SourceFields { get; }

        // Relationship fields in target entity.
        public List<string> TargetFields { get; }

        public RelationshipFields(List<string> sourceFields, List<string> targetFields)
        {
            SourceFields = sourceFields;
            TargetFields = targetFields;
        }
    }
}
