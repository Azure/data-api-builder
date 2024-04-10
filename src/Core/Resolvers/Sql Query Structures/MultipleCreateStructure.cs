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
        /// Entities that need to be inserted before the current entity. Current entity references these entites and needs the values of referenced columns to construct its INSERT SQL statement.
        /// </summary>
        public List<Tuple<string, object?>> ReferencedEntities;

        /// <summary>
        /// Entities that need to be inserted after the current entity. Current entity is referenced by these entities and the values of referenced columns needs to be passed to
        /// these entities to construct the INSERT SQL statement.
        /// </summary>
        public List<Tuple<string, object?>> ReferencingEntities;

        /// <summary>
        /// Fields belonging to the current entity.
        /// </summary>
        public IDictionary<string, object?>? CurrentEntityParams;

        /// <summary>
        /// Fields belonging to the linking table.
        /// </summary>
        public IDictionary<string, object?>? LinkingTableParams;

        /// <summary>
        /// PK of the record created in the table backing the current entity. 
        /// </summary>
        public Dictionary<string, object?>? CurrentEntityPKs;

        /// <summary>
        /// Entity name for which this wrapper is created.
        /// </summary>
        public string EntityName;

        /// <summary>
        /// PK of the record created in the table backing the immediate higher level entity.
        /// This gets utilized by entities referencing the current entity.
        /// </summary>
        public Dictionary<string, object?>? HigherLevelEntityPKs;

        /// <summary>
        /// Name of the immediate higher level entity.
        /// </summary>
        public string HigherLevelEntityName;

        /// <summary>
        /// Input parameters parsed from the graphQL mutation operation.
        /// </summary>
        public object? InputMutParams;

        public MultipleCreateStructure(
               string entityName,
               string higherLevelEntityName,
               Dictionary<string, object?>? higherLevelEntityPKs,
               object? inputMutParams = null,
               bool isLinkingTableInsertionRequired = false)
        {
            EntityName = entityName;
            InputMutParams = inputMutParams;
            HigherLevelEntityName = higherLevelEntityName;
            HigherLevelEntityPKs = higherLevelEntityPKs;

            ReferencedEntities = new();
            ReferencingEntities = new();

            IsLinkingTableInsertionRequired = isLinkingTableInsertionRequired;
            if (IsLinkingTableInsertionRequired)
            {
                LinkingTableParams = new Dictionary<string, object?>();
            }
        }
    }
}
