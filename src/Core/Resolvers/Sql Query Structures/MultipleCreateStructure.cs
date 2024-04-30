// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Resolvers.Sql_Query_Structures
{
    /// <summary>
    /// Wrapper class for the current entity to help with multiple create operation.
    /// </summary>
    internal class MultipleCreateStructure
    {
        /// <summary>
        /// Field to indicate whehter a record needs to created in the linking table after
        /// creating a record in the table backing the current entity.
        /// Linking table and consequently this field is applicable only for M:N relationship type.
        /// </summary>
        public bool IsLinkingTableInsertionRequired;

        /// <summary>
        /// Relationships that need to be processed before the current entity. Current entity references these entites
        /// and needs the values of referenced columns to construct its INSERT SQL statement.
        /// </summary>
        public List<Tuple<string, object?>> ReferencedRelationships;

        /// <summary>
        /// Relationships that need to be processed after the current entity. Current entity is referenced by these entities
        /// and the values of referenced columns needs to be passed to
        /// these entities to construct the INSERT SQL statement.
        /// </summary>
        public List<Tuple<string, object?>> ReferencingRelationships;

        /// <summary>
        /// Fields belonging to the current entity.
        /// </summary>
        public Dictionary<string, object?> CurrentEntityParams;

        /// <summary>
        /// Fields belonging to the linking table.
        /// </summary>
        public Dictionary<string, object?> LinkingTableParams;

        /// <summary>
        /// Values in the record created in the table backing the current entity. 
        /// </summary>
        public Dictionary<string, object?>? CurrentEntityCreatedValues;

        /// <summary>
        /// Entity name for which this wrapper is created.
        /// </summary>
        public string EntityName;

        /// <summary>
        /// Name of the immediate higher level entity.
        /// </summary>
        public string ParentEntityName;

        /// <summary>
        /// Input parameters parsed from the graphQL mutation operation.
        /// The parsed input parameters of the multiple create mutation result will be
        /// assigned to this field.
        /// Type of the object assigned depends on the type of the multiple create operation.
        /// 1. Point multiple create - Dictionary<string, object?>
        /// 2. Many multiple create - List<Dictionary<string, object?>>
        /// </summary>
        public object? InputMutParams;

        public MultipleCreateStructure(
               string entityName,
               string parentEntityName,
               object? inputMutParams = null,
               bool isLinkingTableInsertionRequired = false)
        {
            EntityName = entityName;
            InputMutParams = inputMutParams;
            ParentEntityName = parentEntityName;

            ReferencedRelationships = new();
            ReferencingRelationships = new();
            CurrentEntityParams = new Dictionary<string, object?>();
            LinkingTableParams = new Dictionary<string, object?>();

            IsLinkingTableInsertionRequired = isLinkingTableInsertionRequired;
            if (IsLinkingTableInsertionRequired)
            {
                LinkingTableParams = new Dictionary<string, object?>();
            }
        }
    }
}
