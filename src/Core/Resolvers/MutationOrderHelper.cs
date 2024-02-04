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
    public  class MutationOrderHelper
    {
        public static Tuple<string, List<string>> GetReferencingEntityNameAndColumns(
            IMiddlewareContext context,
            string sourceEntityName,
            string targetEntityName,
            ISqlMetadataProvider metadataProvider,
            HashSet<string> derivableBackingColumnsInSource,
            IValueNode? iValue)
        {
            if (metadataProvider.GetEntityNamesAndDbObjects().TryGetValue(sourceEntityName, out DatabaseObject? sourceDbObject) &&
                metadataProvider.GetEntityNamesAndDbObjects().TryGetValue(targetEntityName, out DatabaseObject? targetDbObject))
            {
                Dictionary<string, List<string>> entityToRelationshipFields = new();
                DatabaseTable sourceDbTable = (DatabaseTable)sourceDbObject;
                DatabaseTable targetDbTable = (DatabaseTable)targetDbObject;
                RelationShipPair sourceTargetPair = new(sourceDbTable, targetDbTable);
                SourceDefinition sourceDefinition = sourceDbObject.SourceDefinition;
                SourceDefinition targetDefinition = targetDbObject.SourceDefinition;
                List<ForeignKeyDefinition> foreignKeys = sourceDefinition.SourceEntityRelationshipMap[sourceEntityName].TargetEntityToFkDefinitionMap[targetEntityName];
                List<string> relationshipFieldsInSource = new();
                List<string> relationshipFieldsInTarget = new();

                foreach (ForeignKeyDefinition foreignKey in foreignKeys)
                {
                    if (foreignKey.ReferencingColumns.Count == 0)
                    {
                        continue;
                    }

                    if (foreignKey.Pair.Equals(sourceTargetPair))
                    {
                        relationshipFieldsInSource = new(foreignKey.ReferencingColumns);
                        relationshipFieldsInTarget = new(foreignKey.ReferencedColumns);
                    }
                    else
                    {
                        relationshipFieldsInTarget = new(foreignKey.ReferencingColumns);
                        relationshipFieldsInSource = new(foreignKey.ReferencedColumns);
                    }

                    break;
                }

                entityToRelationshipFields.Add(sourceEntityName, relationshipFieldsInSource);
                entityToRelationshipFields.Add(targetEntityName, relationshipFieldsInTarget);

                string referencingEntityNameBasedOnEntityMetadata = DetermineReferencingEntityBasedOnEntityMetadata(sourceEntityName, targetEntityName, sourceDbObject, targetDbObject);

                if (!string.IsNullOrEmpty(referencingEntityNameBasedOnEntityMetadata))
                {
                    return new(referencingEntityNameBasedOnEntityMetadata, entityToRelationshipFields[referencingEntityNameBasedOnEntityMetadata]);
                }

                ObjectValueNode? objectValueNode = (ObjectValueNode?)iValue;
                HashSet<string> derivableBackingColumnsInTarget = GetBackingColumnsFromFields(context, targetEntityName, objectValueNode!.Fields, metadataProvider);

                string referencingEntityNameBasedOnAutoGenField = DetermineReferencingEntityBasedOnAutoGenField(
                    sourceEntityName,
                    targetEntityName,
                    metadataProvider);

                string referencingEntityNameBasedOnPresenceInRequestBody = DetermineReferencingEntityBasedOnPresenceInRequestBody(
                    sourceEntityName,
                    targetEntityName,
                    relationshipFieldsInSource,
                    relationshipFieldsInTarget,
                    derivableBackingColumnsInSource,
                    derivableBackingColumnsInTarget);

                if (!string.IsNullOrEmpty(referencingEntityNameBasedOnAutoGenField) || !string.IsNullOrEmpty(referencingEntityNameBasedOnPresenceInRequestBody))
                {
                    if (string.IsNullOrEmpty(referencingEntityNameBasedOnAutoGenField))
                    {
                        return new(referencingEntityNameBasedOnPresenceInRequestBody, entityToRelationshipFields[referencingEntityNameBasedOnPresenceInRequestBody]);
                    }

                    if (string.IsNullOrEmpty(referencingEntityNameBasedOnPresenceInRequestBody))
                    {
                        return new(referencingEntityNameBasedOnAutoGenField, entityToRelationshipFields[referencingEntityNameBasedOnAutoGenField]);
                    }

                    if (!referencingEntityNameBasedOnAutoGenField.Equals(referencingEntityNameBasedOnPresenceInRequestBody))
                    {
                        throw new DataApiBuilderException(
                            message: $"No valid order exists.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    return new(referencingEntityNameBasedOnAutoGenField, entityToRelationshipFields[referencingEntityNameBasedOnAutoGenField]);
                }

                // If we hit this point in code, it implies that:
                // 1. Neither source nor target entity has an autogenerated column,
                // 2. Neither source nor target contain a value for any relationship field. This can happen because we make all the relationship fields nullable, so that their
                // values can be derived from the insertion in referenced entity.
                string referencingEntityNameBasedOnAssumableValues = DetermineReferencingEntityBasedOnAssumableValues(
                    sourceEntityName,
                    targetEntityName,
                    sourceDefinition,
                    targetDefinition,
                    relationshipFieldsInSource,
                    relationshipFieldsInTarget);
                return new(referencingEntityNameBasedOnAssumableValues, entityToRelationshipFields[referencingEntityNameBasedOnAssumableValues]);
            }

            return new(string.Empty, new());
        }

        private static string DetermineReferencingEntityBasedOnAssumableValues(
            string sourceEntityName,
            string targetEntityName,
            SourceDefinition sourceDefinition,
            SourceDefinition targetDefinition,
            List<string> relationshipFieldsInSource,
            List<string> relationshipFieldsInTarget)
        {
            bool canAssumeValuesForAllRelationshipFieldsInSource = true, canAssumeValuesForAllRelationshipFieldsInTarget = true;

            foreach (string relationshipFieldInSource in relationshipFieldsInSource)
            {
                ColumnDefinition columnDefinition = sourceDefinition.Columns[relationshipFieldInSource];
                canAssumeValuesForAllRelationshipFieldsInSource = canAssumeValuesForAllRelationshipFieldsInSource && (columnDefinition.HasDefault || columnDefinition.IsAutoGenerated);
            }

            foreach (string relationshipFieldInTarget in relationshipFieldsInTarget)
            {
                ColumnDefinition columnDefinition = targetDefinition.Columns[relationshipFieldInTarget];
                canAssumeValuesForAllRelationshipFieldsInTarget = canAssumeValuesForAllRelationshipFieldsInTarget && (columnDefinition.HasDefault || columnDefinition.IsAutoGenerated);
            }

            if (canAssumeValuesForAllRelationshipFieldsInSource)
            {
                return sourceEntityName;
            }

            if (canAssumeValuesForAllRelationshipFieldsInTarget)
            {
                return targetEntityName;
            }

            throw new DataApiBuilderException(
                message: $"Insufficient data",
                statusCode: HttpStatusCode.BadRequest,
                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
        }

        private static string DetermineReferencingEntityBasedOnPresenceInRequestBody(
            string sourceEntityName, string targetEntityName,
            List<string> relationshipFieldsInSource,
            List<string> relationshipFieldsInTarget,
            HashSet<string> derivableBackingColumnsInSource,
            HashSet<string> derivableBackingColumnsInTarget)
        {
            bool doesSourceContainRelationshipField = false, doesTargetContainRelationshipField = false;
            for (int idx = 0; idx < relationshipFieldsInSource.Count; idx++)
            {
                string relationshipFieldInSource = relationshipFieldsInSource[idx];
                string relationshipFieldInTarget = relationshipFieldsInTarget[idx];
                doesSourceContainRelationshipField = doesSourceContainRelationshipField || derivableBackingColumnsInSource.Contains(relationshipFieldInSource);
                doesTargetContainRelationshipField = doesTargetContainRelationshipField || derivableBackingColumnsInTarget.Contains(relationshipFieldInTarget);

                if (doesSourceContainRelationshipField && doesTargetContainRelationshipField)
                {
                    throw new DataApiBuilderException(
                        message: $"The relationship fields can be present in either source entity: {sourceEntityName} or target entity: {targetEntityName}.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }

            if (doesSourceContainRelationshipField)
            {
                return targetEntityName;
            }

            if (doesTargetContainRelationshipField)
            {
                return sourceEntityName;
            }

            return string.Empty;
        }

        private static string DetermineReferencingEntityBasedOnAutoGenField(
            string sourceEntityName,
            string targetEntityName,
            ISqlMetadataProvider metadataProvider)
        {
            bool doesSourceContainAutoGenColumn = false, doesTargetContainAutoGenColumn = false;
            foreach (ColumnDefinition columnDefinition in metadataProvider.GetSourceDefinition(sourceEntityName).Columns.Values)
            {
                if (columnDefinition.IsAutoGenerated)
                {
                    doesSourceContainAutoGenColumn = true;
                    break;
                }
            }

            foreach (ColumnDefinition columnDefinition in metadataProvider.GetSourceDefinition(targetEntityName).Columns.Values)
            {
                if (columnDefinition.IsAutoGenerated)
                {
                    doesSourceContainAutoGenColumn = true;
                    break;
                }
            }

            if (doesSourceContainAutoGenColumn && doesTargetContainAutoGenColumn)
            {
                throw new DataApiBuilderException(
                    message: $"Both contain autogen columns.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            if (doesSourceContainAutoGenColumn)
            {
                return targetEntityName;
            }

            if (doesTargetContainAutoGenColumn)
            {
                return sourceEntityName;
            }

            return string.Empty;
        }

        private static string DetermineReferencingEntityBasedOnEntityMetadata(
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
            List<string> referencingColumns = new();
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
                    referencingEntityName = string.Empty;
                    break; // Need to handle this logic.
                }

                referencingColumns.AddRange(foreignKey.ReferencingColumns);
                referencingEntityName = foreignKey.Pair.Equals(sourceTargetPair) ? sourceEntityName : targetEntityName;
            }

            return referencingEntityName;
        }

        public static HashSet<string> GetBackingColumnsFromFields(
            IMiddlewareContext context,
            string entityName,
            IReadOnlyList<ObjectFieldNode> fieldNodes,
            ISqlMetadataProvider metadataProvider)
        {
            HashSet<string> backingColumns = new();
            foreach (ObjectFieldNode field in fieldNodes)
            {
                Tuple<IValueNode?, SyntaxKind> fieldDetails = GraphQLUtils.GetFieldDetails(field.Value, context.Variables);
                SyntaxKind fieldKind = fieldDetails.Item2;
                if (GraphQLUtils.IsScalarField(fieldKind) && metadataProvider.TryGetBackingColumn(entityName, field.Name.Value, out string? backingColumnName))
                {
                    backingColumns.Add(backingColumnName);
                }
            }

            return backingColumns;
        }
    }
}
