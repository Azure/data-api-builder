// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Resolvers.Sql_Query_Structures
{
    /// <summary>
    /// Wrapper class for the current entity to help with nested insert operation.
    /// </summary>
    internal class NestedInsertStructure
    {
        /// <summary>
        /// Field to indicate whehter a record needs to created in the linking table after
        /// creating a record in the table backing the current entity.
        /// </summary>
        public bool IsLinkingTableInsertionRequired;

        /// <summary>
        /// 
        /// </summary>
        public List<Tuple<string, object?>> DependencyEntities;

        /// <summary>
        /// 
        /// </summary>
        public List<Tuple<string, object?>> DependentEntities;

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

        public NestedInsertStructure(
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
            IsLinkingTableInsertionRequired = isLinkingTableInsertionRequired;

            DependencyEntities = new();
            DependentEntities = new();
            if (IsLinkingTableInsertionRequired)
            {
                LinkingTableParams = new Dictionary<string, object?>();
            }
        }

    }
}
