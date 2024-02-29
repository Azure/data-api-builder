// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using System.Net;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Helper class to determine the order of insertion for a nested insertion. For a nested insertion, the insertion needs to be performed first
    /// in the referenced entity followed by insertion in the referencing entity.
    /// </summary>
    public class NestedCreateOrderHelper
    {
        /// <summary>
        /// Given a source and target entity with their metadata and request input data,
        /// returns the referencing entity's name for the pair of (source, target) entities.
        /// This method handles the logic to determine the referencing entity for relationships from (source, target) with cardinalities:
        /// 1. 1:N - Target entity is the referencing entity
        /// 2. N:1 - Source entity is the referencing entity
        /// 3. 1:1 - Determined based on foreign key constraint/request input data.
        /// </summary>
        /// <param name="context">GraphQL request context.</param>
        /// <param name="sourceEntityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="metadataProvider">Metadata provider.</param>
        /// <param name="columnDataInSourceBody">Column name/value for backing columns present in the request input for the source entity.</param>
        /// <param name="targetNodeValue">Input GraphQL value for target node (could be an object or array).</param>
        public static string GetReferencingEntityName(
            IMiddlewareContext context,
            string sourceEntityName,
            string targetEntityName,
            ISqlMetadataProvider metadataProvider,
            Dictionary<string, IValueNode?> columnDataInSourceBody,
            IValueNode? targetNodeValue)
        {
            if (!metadataProvider.GetEntityNamesAndDbObjects().TryGetValue(sourceEntityName, out DatabaseObject? sourceDbObject) ||
                !metadataProvider.GetEntityNamesAndDbObjects().TryGetValue(targetEntityName, out DatabaseObject? targetDbObject))
            {
                // This should not be hit ideally.
                throw new Exception("Could not determine definition for source/target entities");
            }

            string referencingEntityNameBasedOnEntityMetadata = DetermineReferencingEntityBasedOnEntityRelationshipMetadata(
                    sourceEntityName: sourceEntityName,
                    targetEntityName: targetEntityName,
                    sourceDbObject: sourceDbObject,
                    targetDbObject: targetDbObject);

            if (!string.IsNullOrEmpty(referencingEntityNameBasedOnEntityMetadata))
            {
                return referencingEntityNameBasedOnEntityMetadata;
            }

            // Had the target node represented an array value, it would have been an 1:N relationship from (source, target).
            // For that case, we would not hit this code because the entity metadata would have been sufficient to tell us that the target entity
            // is the referencing entity. Hence we conclude that the target node must represent a single input object corresponding to N:1 or 1:1 relationship types.
            ObjectValueNode? objectValueNode = (ObjectValueNode?)targetNodeValue;
            Dictionary<string, IValueNode?> columnDataInTargetBody = GetBackingColumnDataFromFields(
                context: context,
                entityName: targetEntityName,
                fieldNodes: objectValueNode!.Fields,
                metadataProvider: metadataProvider);

            return DetermineReferencingEntityBasedOnRequestBody(
                sourceEntityName: sourceEntityName,
                targetEntityName: targetEntityName,
                sourceDbObject: sourceDbObject,
                targetDbObject: targetDbObject,
                columnDataInSourceBody: columnDataInSourceBody,
                columnDataInTargetBody: columnDataInTargetBody);
        }

        /// <summary>
        /// Helper method to determine the referencing entity from a pair of (source, target) entities based on the metadata collected during startup.
        /// The method successfully determines the referencing entity if the relationship between the (source, target) entities is defined in the database
        /// via a Foreign Key constraint.
        /// </summary>
        /// <param name="sourceEntityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="sourceDbObject">Database object for source entity.</param>
        /// <param name="targetDbObject">Database object for target entity.</param>
        /// <returns>Referencing entity name (when the relationship is defined in the database),
        /// or an empty string (when the relationship is defined only in the config).</returns>
        private static string DetermineReferencingEntityBasedOnEntityRelationshipMetadata(
            string sourceEntityName,
            string targetEntityName,
            DatabaseObject sourceDbObject,
            DatabaseObject targetDbObject)
        {
            DatabaseTable sourceDbTable = (DatabaseTable)sourceDbObject;
            DatabaseTable targetDbTable = (DatabaseTable)targetDbObject;
            RelationShipPair sourceTargetPair = new(sourceDbTable, targetDbTable);
            RelationShipPair targetSourcePair = new(targetDbTable, sourceDbTable);
            SourceDefinition sourceDefinition = sourceDbObject.SourceDefinition;
            string referencingEntityName = string.Empty;
            List<ForeignKeyDefinition> foreignKeys = sourceDefinition.SourceEntityRelationshipMap[sourceEntityName].TargetEntityToFkDefinitionMap[targetEntityName];
            foreach (ForeignKeyDefinition foreignKey in foreignKeys)
            {
                if (foreignKey.ReferencingColumns.Count == 0)
                {
                    continue;
                }

                if (foreignKey.Pair.Equals(targetSourcePair) && referencingEntityName.Equals(sourceEntityName) ||
                    foreignKey.Pair.Equals(sourceTargetPair) && referencingEntityName.Equals(targetEntityName))
                {
                    // This indicates that we have 2 ForeignKeyDefinitions in which for one of them, the referencing entity is the source entity
                    // and for the other, the referencing entity is the target entity. This is only possible when the relationship is defined only in the config
                    // and the right cardinality for the relationship between (source, target) is 1. In such a case, we cannot determine which entity is going
                    // to be considered as referencing entity based on the relationship metadata. Instead, we will have to rely on the input data for source/target entities.
                    referencingEntityName = string.Empty;
                    break;
                }

                referencingEntityName = foreignKey.Pair.Equals(sourceTargetPair) ? sourceEntityName : targetEntityName;
            }

            return referencingEntityName;
        }

        /// <summary>
        /// Helper method to determine the referencing entity from a pair of (source, target) entities for which the relationship is defined in the config,
        /// but no relationship exists in the database. In such a case, we rely on the request input data for the source and target entities to determine the referencing entity.
        /// </summary>
        /// <param name="sourceEntityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="sourceDbObject">Database object for source entity.</param>
        /// <param name="targetDbObject">Database object for target entity.</param>
        /// <param name="columnDataInSourceBody">Column name/value for backing columns present in the request input for the source entity.</param>
        /// <param name="columnDataInTargetBody">Column name/value for backing columns present in the request input for the target entity.</param>
        /// <returns>Name of the referencing entity.</returns>
        /// <exception cref="DataApiBuilderException">Thrown when:
        /// 1. Either the provided input data for source/target entities is insufficient.
        /// 2. A conflict occurred such that both entities need to be considered as referencing entity.</exception>
        private static string DetermineReferencingEntityBasedOnRequestBody(
            string sourceEntityName,
            string targetEntityName,
            DatabaseObject sourceDbObject,
            DatabaseObject targetDbObject,
            Dictionary<string, IValueNode?> columnDataInSourceBody,
            Dictionary<string, IValueNode?> columnDataInTargetBody)
        {
            (List<string> relationshipFieldsInSource, List<string> relationshipFieldsInTarget) = GetRelationshipFieldsInSourceAndTarget(
                    sourceEntityName: sourceEntityName,
                    targetEntityName: targetEntityName,
                    sourceDbObject: sourceDbObject,
                    targetDbObject: targetDbObject);

            // Collect column metadata for source/target columns.
            Dictionary<string, ColumnDefinition> sourceColumnDefinitions = sourceDbObject.SourceDefinition.Columns;
            Dictionary<string, ColumnDefinition> targetColumnDefinitions = targetDbObject.SourceDefinition.Columns;

            // Set to true when any relationship field is autogenerated in the source.
            bool doesSourceContainAnyAutogenRelationshipField = false;
            // Set to true when any relationship field is autogenerated in the target.
            bool doesTargetContainAnyAutogenRelationshipField = false;

            // Set to true when source body contains any relationship field.
            bool doesSourceBodyContainAnyRelationshipField = false;
            // Set to true when target body contains any relationship field.
            bool doesTargetBodyContainAnyRelationshipField = false;

            // Set to false when source body can't assume a non-null value for one or more relationship fields.
            bool canSourceAssumeAllRelationshipFieldValues = true;
            // Set to false when target body can't assume a non-null value for one or more relationship fields.
            bool canTargetAssumeAllRelationshipFieldsValues = true;

            // Loop over all the relationship fields in source/target to appropriately set the above variables.
            for (int idx = 0; idx < relationshipFieldsInSource.Count; idx++)
            {
                string relationshipFieldInSource = relationshipFieldsInSource[idx];
                string relationshipFieldInTarget = relationshipFieldsInTarget[idx];

                // Determine whether the source/target relationship fields for this pair are autogenerated.
                bool isSourceRelationshipColumnAutogenerated = sourceColumnDefinitions[relationshipFieldInSource].IsAutoGenerated;
                bool isTargetRelationshipColumnAutogenerated = targetColumnDefinitions[relationshipFieldInTarget].IsAutoGenerated;

                // Update whether source/target contains any relationship field which is autogenerated.
                doesSourceContainAnyAutogenRelationshipField = doesSourceContainAnyAutogenRelationshipField || isSourceRelationshipColumnAutogenerated;
                doesTargetContainAnyAutogenRelationshipField = doesTargetContainAnyAutogenRelationshipField || isTargetRelationshipColumnAutogenerated;

                // When both source/target entities contain an autogenerated relationship field, we cannot choose one entity as a referencing entity.
                // This is because for a referencing entity, the values for all the referencing fields should be derived from the insertion in the referenced entity.
                // However, here we would not be able to assign value to an autogenerated relationship field in the referencing entity.
                if (doesSourceContainAnyAutogenRelationshipField && doesTargetContainAnyAutogenRelationshipField)
                {
                    throw new DataApiBuilderException(
                        message: $"Both source entity: {sourceEntityName} and target entity: {targetEntityName} contain autogenerated fields.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // If the source entity contains a relationship field in request body which suggests us to do the insertion first in source entity,
                // and there is an autogenerated relationship field in the target entity which suggests us to do insertion first in target entity,
                // we cannot determine a valid order of insertion.
                if (doesSourceBodyContainAnyRelationshipField && doesTargetContainAnyAutogenRelationshipField)
                {
                    throw new DataApiBuilderException(
                        message: $"The source entity: {sourceEntityName} cannot contain the field: {relationshipFieldInSource}.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // If the target entity contains a relationship field in request body which suggests us to do the insertion first in target entity,
                // and there is an autogenerated relationship field in the source entity which suggests us to do insertion first in source entity,
                // we cannot determine a valid order of insertion.
                if (doesTargetBodyContainAnyRelationshipField && doesSourceContainAnyAutogenRelationshipField)
                {
                    throw new DataApiBuilderException(
                        message: $"The target entity: {sourceEntityName} cannot contain the field: {relationshipFieldInTarget}.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // Determine whether the input data for source/target contain a value (could be null) for this pair of relationship fields. 
                bool doesSourceBodyContainThisRelationshipColumn = columnDataInSourceBody.TryGetValue(relationshipFieldInSource, out IValueNode? sourceColumnvalue);
                bool doesTargetBodyContainThisRelationshipColumn = columnDataInTargetBody.TryGetValue(relationshipFieldInTarget, out IValueNode? targetColumnvalue);

                // Update whether input data for source/target contains any relationship field.
                doesSourceBodyContainAnyRelationshipField = doesSourceBodyContainAnyRelationshipField || doesSourceBodyContainThisRelationshipColumn;
                doesTargetBodyContainAnyRelationshipField = doesTargetBodyContainAnyRelationshipField || doesTargetBodyContainThisRelationshipColumn;

                // If relationship columns are presence in the input for both the source and target entities, we cannot choose one entity as a referencing
                // entity. This is because for a referencing entity, the values for all the referencing fields should be derived from the insertion in the referenced entity.
                // However, here both entities contain atleast one relationship field whose value is provided in the request.
                if (doesSourceBodyContainAnyRelationshipField && doesTargetBodyContainAnyRelationshipField)
                {
                    throw new DataApiBuilderException(
                        message: $"The relationship fields can be present in either source entity: {sourceEntityName} or target entity: {targetEntityName}.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // The source/target entities can assume a value for insertion for a relationship field if:
                // 1. The field is autogenerated, or
                // 2. The field is given a non-null value by the user - since we don't allow null values for a relationship field.
                bool canSourceAssumeThisFieldValue = isSourceRelationshipColumnAutogenerated || sourceColumnvalue is not null;
                bool canTargetAssumeThisFieldValue = isTargetRelationshipColumnAutogenerated || targetColumnvalue is not null;

                // Update whether all the values(non-null) for relationship fields are available for source/target.
                canSourceAssumeAllRelationshipFieldValues = canSourceAssumeAllRelationshipFieldValues && canSourceAssumeThisFieldValue;
                canTargetAssumeAllRelationshipFieldsValues = canTargetAssumeAllRelationshipFieldsValues && canTargetAssumeThisFieldValue;

                // If the values for all relationship fields cannot be assumed for neither source nor target, the nested insertion cannot be performed.
                if (!canSourceAssumeAllRelationshipFieldValues && !canTargetAssumeAllRelationshipFieldsValues)
                {
                    throw new DataApiBuilderException(
                        message: $"Insufficient data.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }

            return canSourceAssumeAllRelationshipFieldValues ? targetEntityName : sourceEntityName;
        }

        /// <summary>
        /// Helper method to determine the relationship fields in the source and the target entities. Here, we don't really care about which of the entities between
        /// source and target are referencing/referenced entities. We just want to determine what all columns from the entity are involved in the relationship.
        /// </summary>
        /// <param name="sourceEntityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="sourceDbObject">Database object for source entity.</param>
        /// <param name="targetDbObject">Database object for target entity.</param>
        /// <returns>Tuple of relationship fields in source, target entities.</returns>
        private static Tuple<List<string>, List<string>> GetRelationshipFieldsInSourceAndTarget(
            string sourceEntityName,
            string targetEntityName,
            DatabaseObject sourceDbObject,
            DatabaseObject targetDbObject)
        {
            List<string> relationshipFieldsInSource, relationshipFieldsInTarget;
            DatabaseTable sourceDbTable = (DatabaseTable)sourceDbObject;
            DatabaseTable targetDbTable = (DatabaseTable)targetDbObject;
            RelationShipPair sourceTargetPair = new(sourceDbTable, targetDbTable);
            SourceDefinition sourceDefinition = sourceDbObject.SourceDefinition;
            List<ForeignKeyDefinition> foreignKeys = sourceDefinition.SourceEntityRelationshipMap[sourceEntityName].TargetEntityToFkDefinitionMap[targetEntityName];
            foreach (ForeignKeyDefinition foreignKey in foreignKeys)
            {
                if (foreignKey.ReferencingColumns.Count == 0)
                {
                    continue;
                }

                if (foreignKey.Pair.Equals(sourceTargetPair))
                {
                    relationshipFieldsInSource = foreignKey.ReferencingColumns;
                    relationshipFieldsInTarget = foreignKey.ReferencedColumns;
                }
                else
                {
                    relationshipFieldsInTarget = foreignKey.ReferencingColumns;
                    relationshipFieldsInSource = foreignKey.ReferencedColumns;
                }

                return new(relationshipFieldsInSource, relationshipFieldsInTarget);
            }

            throw new Exception("Did not find FK definition");
        }

        /// <summary>
        /// Helper method to determine the backing column name/value for all the columns which have been assigned a value by the user in the request input data
        /// for the entity.
        /// </summary>
        /// <param name="context">GraphQL request context.</param>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="fieldNodes">Set of fields belonging to the input value for the entity.</param>
        /// <param name="metadataProvider">Metadata provider</param>
        public static Dictionary<string, IValueNode?> GetBackingColumnDataFromFields(
            IMiddlewareContext context,
            string entityName,
            IReadOnlyList<ObjectFieldNode> fieldNodes,
            ISqlMetadataProvider metadataProvider)
        {
            Dictionary<string, IValueNode?> backingColumnData = new();
            foreach (ObjectFieldNode field in fieldNodes)
            {
                Tuple<IValueNode?, SyntaxKind> fieldDetails = GraphQLUtils.GetFieldDetails(field.Value, context.Variables);
                SyntaxKind fieldKind = fieldDetails.Item2;
                if (GraphQLUtils.IsScalarField(fieldKind) && metadataProvider.TryGetBackingColumn(entityName, field.Name.Value, out string? backingColumnName))
                {
                    backingColumnData.Add(backingColumnName, fieldDetails.Item1);
                }
            }

            return backingColumnData;
        }
    }
}
