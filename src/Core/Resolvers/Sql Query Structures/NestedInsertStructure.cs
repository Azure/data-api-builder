// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Resolvers.Sql_Query_Structures
{
    internal class NestedInsertStructure
    {
        /// <summary>
        /// 
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
        /// 
        /// </summary>
        public IDictionary<string, object?>? CurrentEntityParams;

        /// <summary>
        /// 
        /// </summary>
        public List<IDictionary<string, object?>>? LinkingTableParams;

        /// <summary>
        /// 
        /// </summary>
        public Dictionary<string, object?>? CurrentEntityPKs;

        /// <summary>
        /// 
        /// </summary>
        public string EntityName;

        /// <summary>
        /// 
        /// </summary>
        public Dictionary<string, object?>? HigherLevelEntityPKs;

        /// <summary>
        /// 
        /// </summary>
        public string HigherLevelEntityName;

        /// <summary>
        /// 
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

        }

    }
}
