// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models
{
    /// <summary>
    /// Class to represent input for an entity in a multiple-create request.
    /// </summary>
    public class MultipleMutationEntityInputValidationContext
    {
        /// <summary>
        /// Current entity name.
        /// </summary>
        public string EntityName { get; }

        /// <summary>
        /// Parent entity name. For the topmost entity, this will be set as an empty string.
        /// </summary>
        public string ParentEntityName { get; }

        /// <summary>
        /// Set of columns in the current entity whose values are derived from insertion in the parent entity
        /// (i.e. parent entity would have been the referenced entity).
        /// For the topmost entity, this will be an empty set.</param>
        /// </summary>
        public HashSet<string> ColumnsDerivedFromParentEntity { get; }

        /// <summary>
        /// Set of columns in the current entity whose values are to be derived from insertion in the entity or its subsequent
        /// referenced entities and returned to the parent entity so as to provide values for the corresponding referencing fields
        /// (i.e. parent entity would have been the referencing entity)
        ///  For the topmost entity, this will be an empty set.
        /// </summary>
        public HashSet<string> ColumnsToBeDerivedFromEntity { get; }

        public MultipleMutationEntityInputValidationContext(
            string entityName,
            string parentEntityName,
            HashSet<string> columnsDerivedFromParentEntity,
            HashSet<string> columnsToBeDerivedFromEntity)
        {
            EntityName = entityName;
            ParentEntityName = parentEntityName;
            ColumnsDerivedFromParentEntity = columnsDerivedFromParentEntity;
            ColumnsToBeDerivedFromEntity = columnsToBeDerivedFromEntity;
        }
    }
}
