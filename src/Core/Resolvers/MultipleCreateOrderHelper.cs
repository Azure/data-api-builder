// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Helper class to determine the order of insertion for a multiple create mutation. For insertion in related entity using
    /// multiple create, the insertion needs to be performed first in the referenced entity followed by insertion in the referencing entity.
    /// </summary>
    public class MultipleCreateOrderHelper
    {
        /// <summary>
        /// Given a source and target entity with their metadata and request input data,
        /// returns the referencing entity's name for the pair of (source, target) entities.
        ///
        /// When visualized as a graphQL mutation request, 
        ///   Source entity refers to the top level entity in whose configuration the relationship is defined.
        ///   Target entity refers to the related entity.
        ///   
        /// This method handles the logic to determine the referencing entity for relationships from (source, target) with cardinalities:
        /// 1. 1:N - Target entity is the referencing entity
        /// 2. N:1 - Source entity is the referencing entity
        /// 3. 1:1 - Determined based on foreign key constraint/request input data.
        /// 4. M:N - None of the source/target entity is the referencing entity. Instead, linking table acts as the referencing entity.
        /// </summary>
        /// <param name="context">GraphQL request context.</param>
        /// <param name="relationshipName">Configured relationship name in the config file b/w source and target entity.</param>
        /// <param name="sourceEntityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="metadataProvider">Metadata provider.</param>
        /// <param name="columnDataInSourceBody">Column name/value for backing columns present in the request input for the source entity.</param>
        /// <param name="targetNodeValue">Input GraphQL value for target node (could be an object or array).</param>
        /// <param name="nestingLevel">Nesting level of the entity in the mutation request.</param>
        /// <param name="isMNRelationship">true if the relationship is a Many-Many relationship.</param>
        public static string GetReferencingEntityName(
            IMiddlewareContext context,
            string relationshipName,
            string sourceEntityName,
            string targetEntityName,
            ISqlMetadataProvider metadataProvider,
            Dictionary<string, IValueNode?> columnDataInSourceBody,
            IValueNode? targetNodeValue,
            int nestingLevel,
            bool isMNRelationship = false)
        {
            if (!metadataProvider.GetEntityNamesAndDbObjects().TryGetValue(sourceEntityName, out DatabaseObject? sourceDbObject) ||
                !metadataProvider.GetEntityNamesAndDbObjects().TryGetValue(targetEntityName, out DatabaseObject? targetDbObject))
            {
                // This should not be hit ideally.
                throw new DataApiBuilderException(
                    message: $"Could not determine definition for source: {sourceEntityName} and target: {targetEntityName} entities for " +
                    $"relationship: {relationshipName} at level: {nestingLevel}",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            if (sourceDbObject.GetType() != typeof(DatabaseTable) || targetDbObject.GetType() != typeof(DatabaseTable))
            {
                throw new DataApiBuilderException(
                message: $"Cannot execute multiple-create for relationship: {relationshipName} at level: {nestingLevel} because currently DAB supports" +
                $"multiple-create only for entities backed by tables.",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
            }

            DatabaseTable sourceDbTable = (DatabaseTable)sourceDbObject;
            DatabaseTable targetDbTable = (DatabaseTable)targetDbObject;
            if (sourceDbTable.Equals(targetDbTable))
            {
                //  DAB does not yet support multiple-create for self referencing relationships where both the source and
                //  target entities are backed by same database table.
                throw new DataApiBuilderException(
                message: $"Multiple-create for relationship: {relationshipName} at level: {nestingLevel} is not supported because the " +
                $"source entity: {sourceEntityName} and the target entity: {targetEntityName} are backed by the same database table.",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
            }

            if (isMNRelationship)
            {
                // For M:N relationships, neither the source nor the target entity act as the referencing entity.
                // Instead, the linking table act as the referencing entity.
                return string.Empty;
            }

            if (TryDetermineReferencingEntityBasedOnEntityRelationshipMetadata(
                    sourceEntityName: sourceEntityName,
                    targetEntityName: targetEntityName,
                    sourceDbTable: sourceDbTable,
                    referencingEntityName: out string? referencingEntityNameBasedOnEntityMetadata))
            {
                return referencingEntityNameBasedOnEntityMetadata;
            }
            else
            {
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
                    relationshipName: relationshipName,
                    sourceEntityName: sourceEntityName,
                    targetEntityName: targetEntityName,
                    sourceDbObject: sourceDbObject,
                    targetDbObject: targetDbObject,
                    columnDataInSourceBody: columnDataInSourceBody,
                    columnDataInTargetBody: columnDataInTargetBody,
                    nestingLevel: nestingLevel);
            }
        }

        /// <summary>
        /// Helper method to determine the referencing entity from a pair of (source, target) entities based on the foreign key metadata collected during startup.
        /// The method successfully determines the referencing entity if the relationship between the (source, target) entities is defined in the database
        /// via a Foreign Key constraint.
        /// </summary>
        /// <param name="sourceEntityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="sourceDbTable">Database table for source entity.</param>
        /// <param name="referencingEntityName">Stores the determined referencing entity name to be returned to the caller.</param>
        /// <returns>True when the referencing entity name can be determined based on the foreign key constraint defined in the database;
        /// else false.</returns>
        private static bool TryDetermineReferencingEntityBasedOnEntityRelationshipMetadata(
            string sourceEntityName,
            string targetEntityName,
            DatabaseTable sourceDbTable,
            [NotNullWhen(true)] out string? referencingEntityName)
        {
            SourceDefinition sourceDefinition = sourceDbTable.SourceDefinition;

            List<ForeignKeyDefinition> targetEntityForeignKeys = sourceDefinition.SourceEntityRelationshipMap[sourceEntityName].TargetEntityToFkDefinitionMap[targetEntityName];
            HashSet<string> referencingEntityNames = new();

            // Loop over all the Foreign key definitions inferred for the target entity's relationship with the source entity.
            // In the case when the relationship between the two entities is backed by an FK constraint, only one of the source/target
            // entity should be present in the inferred foreign keys as the referencing table.
            // However, if the relationship is not backed by an FK constraint, there will be 2 different FK definitions present
            // in which both the source/target entities would be acting as referencing entities.
            foreach (ForeignKeyDefinition targetEntityForeignKey in targetEntityForeignKeys)
            {
                if (targetEntityForeignKey.ReferencingColumns.Count == 0)
                {
                    continue;
                }

                string referencingEntityNameForThisFK = targetEntityForeignKey.Pair.ReferencingDbTable.Equals(sourceDbTable) ? sourceEntityName : targetEntityName;
                referencingEntityNames.Add(referencingEntityNameForThisFK);
            }

            // If the count of referencing entity names != 1, it indicates we have entries for both source and target entities acting as the referencing table
            // in the relationship. This can only happen for relationships which are not backed by an FK constraint. For such relationships, we rely on request body
            // to help determine the referencing entity.
            if (referencingEntityNames.Count == 1)
            {
                referencingEntityName = referencingEntityNames.First();
                return true;
            }
            else
            {
                referencingEntityName = null;
                return false;
            }
        }

        /// <summary>
        /// Helper method to determine the referencing entity from a pair of (source, target) entities for which the relationship is defined in the config,
        /// but no FK constraint exists in the database. In such a case, we rely on the request input data for the source and target entities to determine the referencing entity.
        /// </summary>
        /// <param name="relationshipName">Configured relationship name in the config file b/w source and target entity.</param>
        /// <param name="sourceEntityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="sourceDbObject">Database object for source entity.</param>
        /// <param name="targetDbObject">Database object for target entity.</param>
        /// <param name="columnDataInSourceBody">Column name/value for backing columns present in the request input for the source entity.</param>
        /// <param name="columnDataInTargetBody">Column name/value for backing columns present in the request input for the target entity.</param>
        /// <param name="nestingLevel">Nesting level of the entity in the mutation request.</param>
        /// <returns>Name of the referencing entity.</returns>
        /// <exception cref="DataApiBuilderException">Thrown when:
        /// 1. Either the provided input data for source/target entities is insufficient.
        /// 2. A conflict occurred such that both entities need to be considered as referencing entity.</exception>
        private static string DetermineReferencingEntityBasedOnRequestBody(
            string relationshipName,
            string sourceEntityName,
            string targetEntityName,
            DatabaseObject sourceDbObject,
            DatabaseObject targetDbObject,
            Dictionary<string, IValueNode?> columnDataInSourceBody,
            Dictionary<string, IValueNode?> columnDataInTargetBody,
            int nestingLevel)
        {
            RelationshipFields relationshipFields = GetRelationshipFieldsInSourceAndTarget(
                    sourceEntityName: sourceEntityName,
                    targetEntityName: targetEntityName,
                    sourceDbObject: sourceDbObject);

            List<string> sourceFields = relationshipFields.SourceFields;
            List<string> targetFields = relationshipFields.TargetFields;

            // Collect column metadata for source/target columns.
            Dictionary<string, ColumnDefinition> sourceColumnDefinitions = sourceDbObject.SourceDefinition.Columns;
            Dictionary<string, ColumnDefinition> targetColumnDefinitions = targetDbObject.SourceDefinition.Columns;

            bool doesSourceContainAnyAutogenRelationshipField = false;
            bool doesTargetContainAnyAutogenRelationshipField = false;
            bool doesSourceBodyContainAnyRelationshipField = false;
            bool doesTargetBodyContainAnyRelationshipField = false;

            // Set to false when source body can't assume a non-null value for one or more relationship fields.
            // For the current relationship column to process, the value can be assumed when:
            // 1.The relationship column is autogenerated, or
            // 2.The request body provides a value for the relationship column.
            bool canSourceAssumeAllRelationshipFieldValues = true;

            // Set to false when target body can't assume a non-null value for one or more relationship fields.
            bool canTargetAssumeAllRelationshipFieldsValues = true;

            // Loop over all the relationship fields in source/target to appropriately set the above variables.
            for (int idx = 0; idx < sourceFields.Count; idx++)
            {
                string relationshipFieldInSource = sourceFields[idx];
                string relationshipFieldInTarget = targetFields[idx];

                // Determine whether the source/target relationship fields for this pair are autogenerated.
                bool isSourceRelationshipFieldAutogenerated = sourceColumnDefinitions[relationshipFieldInSource].IsAutoGenerated;
                bool isTargetRelationshipFieldAutogenerated = targetColumnDefinitions[relationshipFieldInTarget].IsAutoGenerated;

                // Update whether source/target contains any relationship field which is autogenerated.
                doesSourceContainAnyAutogenRelationshipField = doesSourceContainAnyAutogenRelationshipField || isSourceRelationshipFieldAutogenerated;
                doesTargetContainAnyAutogenRelationshipField = doesTargetContainAnyAutogenRelationshipField || isTargetRelationshipFieldAutogenerated;

                // When both source/target entities contain an autogenerated relationship field, 
                // DAB can't determine the referencing entity. That's because a referencing entity's
                // referencing fields are derived from the insertion of the referenced entity but
                // we cannot assign a derived value to an autogenerated field.
                if (doesSourceContainAnyAutogenRelationshipField && doesTargetContainAnyAutogenRelationshipField)
                {
                    throw new DataApiBuilderException(
                        message: $"Cannot execute multiple-create because both the source entity: {sourceEntityName} and the target entity: " +
                        $"{targetEntityName} contain autogenerated fields for relationship: {relationshipName} at level: {nestingLevel}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // Determine whether the input data for source/target contain a value (could be null) for this pair of relationship fields.
                bool doesSourceBodyContainThisRelationshipField = columnDataInSourceBody.TryGetValue(relationshipFieldInSource, out IValueNode? sourceColumnvalue);
                bool doesTargetBodyContainThisRelationshipField = columnDataInTargetBody.TryGetValue(relationshipFieldInTarget, out IValueNode? targetColumnvalue);

                // Update whether input data for source/target contains any relationship field.
                doesSourceBodyContainAnyRelationshipField = doesSourceBodyContainAnyRelationshipField || doesSourceBodyContainThisRelationshipField;
                doesTargetBodyContainAnyRelationshipField = doesTargetBodyContainAnyRelationshipField || doesTargetBodyContainThisRelationshipField;

                // If the source entity contains a relationship field in request body which suggests we perform the insertion first in source entity,
                // and there is an autogenerated relationship field in the target entity which suggests we perform the insertion first in target entity,
                // we cannot determine a valid order of insertion.
                if (doesSourceBodyContainAnyRelationshipField && doesTargetContainAnyAutogenRelationshipField)
                {
                    throw new DataApiBuilderException(
                        message: $"Input for source entity: {sourceEntityName} for the relationship: {relationshipName} at level: {nestingLevel} cannot contain the field: {relationshipFieldInSource}.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // If the target entity contains a relationship field in request body which suggests we perform the insertion first in target entity,
                // and there is an autogenerated relationship field in the source entity which suggests we perform the insertion first in source entity,
                // we cannot determine a valid order of insertion.
                if (doesTargetBodyContainAnyRelationshipField && doesSourceContainAnyAutogenRelationshipField)
                {
                    throw new DataApiBuilderException(
                        message: $"Input for target entity: {targetEntityName} for the relationship: {relationshipName} at level: {nestingLevel} cannot contain the field: {relationshipFieldInSource}.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // If relationship columns are present in the input for both the source and target entities,
                // we cannot choose one entity as the referencing entity. This is because for a referencing entity,
                // the values for all the referencing fields should be derived from the insertion in the referenced entity.
                // However, here both entities contain atleast one relationship field whose value is provided in the request.
                if (doesSourceBodyContainAnyRelationshipField && doesTargetBodyContainAnyRelationshipField)
                {
                    throw new DataApiBuilderException(
                        message: $"The relationship fields must be specified for either the source entity: {sourceEntityName} or the target entity: {targetEntityName} " +
                        $"for the relationship: {relationshipName} at level: {nestingLevel}.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // The source/target entities can assume a value for insertion for a relationship field if:
                // 1. The field is autogenerated, or
                // 2. The field is given a non-null value by the user - since we don't allow null values for a relationship field.
                bool canSourceAssumeThisFieldValue = isSourceRelationshipFieldAutogenerated || sourceColumnvalue is not null;
                bool canTargetAssumeThisFieldValue = isTargetRelationshipFieldAutogenerated || targetColumnvalue is not null;

                // Update whether all the values(non-null) for relationship fields are available for source/target.
                canSourceAssumeAllRelationshipFieldValues = canSourceAssumeAllRelationshipFieldValues && canSourceAssumeThisFieldValue;
                canTargetAssumeAllRelationshipFieldsValues = canTargetAssumeAllRelationshipFieldsValues && canTargetAssumeThisFieldValue;

                // If the values for all relationship fields cannot be assumed for neither source nor target,
                // the multiple create request cannot be executed.
                if (!canSourceAssumeAllRelationshipFieldValues && !canTargetAssumeAllRelationshipFieldsValues)
                {
                    throw new DataApiBuilderException(
                        message: $"Neither source entity: {sourceEntityName} nor the target entity: {targetEntityName} for the relationship: {relationshipName} at level: {nestingLevel} " +
                        $"provide sufficient data to perform a multiple-create (related insertion) on the entities.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }

            return canSourceAssumeAllRelationshipFieldValues ? targetEntityName : sourceEntityName;
        }

        /// <summary>
        /// Helper method to determine the relationship fields in the source and the target entities. Here, we don't really care about which of the entities between
        /// source and target are going to act as referencing/referenced entities in the mutation.
        /// We just want to determine which of the source entity and target entity's columns are involved in the relationship.
        /// </summary>
        /// <param name="sourceEntityName">Source entity name.</param>
        /// <param name="targetEntityName">Target entity name.</param>
        /// <param name="sourceDbObject">Database object for source entity.</param>
        /// <returns>Relationship fields in source, target entities.</returns>
        private static RelationshipFields GetRelationshipFieldsInSourceAndTarget(
            string sourceEntityName,
            string targetEntityName,
            DatabaseObject sourceDbObject)
        {
            List<string> relationshipFieldsInSource, relationshipFieldsInTarget;
            DatabaseTable sourceDbTable = (DatabaseTable)sourceDbObject;
            SourceDefinition sourceDefinition = sourceDbObject.SourceDefinition;
            List<ForeignKeyDefinition> targetEntityForeignKeys = sourceDefinition.SourceEntityRelationshipMap[sourceEntityName].TargetEntityToFkDefinitionMap[targetEntityName];
            foreach (ForeignKeyDefinition targetEntityForeignKey in targetEntityForeignKeys)
            {
                if (targetEntityForeignKey.ReferencingColumns.Count == 0)
                {
                    continue;
                }

                if (targetEntityForeignKey.Pair.ReferencingDbTable.Equals(sourceDbTable))
                {
                    relationshipFieldsInSource = targetEntityForeignKey.ReferencingColumns;
                    relationshipFieldsInTarget = targetEntityForeignKey.ReferencedColumns;
                }
                else
                {
                    relationshipFieldsInSource = targetEntityForeignKey.ReferencedColumns;
                    relationshipFieldsInTarget = targetEntityForeignKey.ReferencingColumns;
                }

                return new RelationshipFields(sourceFields: relationshipFieldsInSource, targetFields: relationshipFieldsInTarget);
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
