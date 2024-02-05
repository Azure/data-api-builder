// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using System.Net;

namespace Azure.DataApiBuilder.Core.Services
{
    public class GraphQLRequestValidator
    {
        public static void ValidateGraphQLValueNode(
            IInputField schema,
            string entityName,
            IMiddlewareContext context,
            object? parameters,
            RuntimeConfig runtimeConfig,
            HashSet<string> derivedColumnsFromParentEntity,
            int nestingLevel,
            string parentEntityName,
            IMetadataProviderFactory sqlMetadataProviderFactory)
        {
            InputObjectType schemaObject = ResolverMiddleware.InputObjectTypeFromIInputField(schema);
            if (parameters is List<ObjectFieldNode> listOfObjectFieldNode)
            {
                // For the example createbook mutation written above, the object value for `item` is interpreted as a List<ObjectFieldNode> i.e.
                // all the fields present for item namely- title, reviews, publisher, authors are interpreted as ObjectFieldNode.
                ValidateObjectFieldNodes(
                    context: context,
                    entityName: entityName,
                    schemaObject: schemaObject,
                    objectFieldNodes: listOfObjectFieldNode,
                    runtimeConfig: runtimeConfig,
                    derivedColumnsFromParentEntity: derivedColumnsFromParentEntity,
                    nestingLevel: nestingLevel + 1,
                    parentEntityName: parentEntityName,
                    sqlMetadataProviderFactory: sqlMetadataProviderFactory);
            }
            else if (parameters is List<IValueNode> listOfIValueNode)
            {
                // For the example createbooks mutation written above, the list value for `items` is interpreted as a List<IValueNode>
                // i.e. items is a list of ObjectValueNode(s).
                listOfIValueNode.ForEach(iValueNode => ValidateGraphQLValueNode(
                    schema: schema,
                    entityName: entityName,
                    context: context,
                    parameters: iValueNode,
                    runtimeConfig: runtimeConfig,
                    derivedColumnsFromParentEntity: derivedColumnsFromParentEntity,
                    nestingLevel: nestingLevel,
                    parentEntityName: parentEntityName,
                    sqlMetadataProviderFactory: sqlMetadataProviderFactory));
            }
            else if (parameters is ObjectValueNode objectValueNode)
            {
                // For the example createbook mutation written above, the node for publisher field is interpreted as an ObjectValueNode.
                // Similarly the individual node (elements in the list) for the reviews, authors ListValueNode(s) are also interpreted as ObjectValueNode(s).
                ValidateObjectFieldNodes(
                    context: context,
                    entityName: entityName,
                    schemaObject: schemaObject,
                    objectFieldNodes: objectValueNode.Fields,
                    runtimeConfig: runtimeConfig,
                    derivedColumnsFromParentEntity: derivedColumnsFromParentEntity,
                    nestingLevel: nestingLevel + 1,
                    parentEntityName: parentEntityName,
                    sqlMetadataProviderFactory: sqlMetadataProviderFactory);
            }
            else if (parameters is ListValueNode listValueNode)
            {
                // For the example createbook mutation written above, the list values for reviews and authors fields are interpreted as ListValueNode.
                // All the nodes in the ListValueNode are parsed one by one.
                listValueNode.GetNodes().ToList().ForEach(objectValueNodeInListValueNode => ValidateGraphQLValueNode(
                    schema: schema,
                    entityName: entityName,
                    context: context,
                    parameters: objectValueNodeInListValueNode,
                    runtimeConfig: runtimeConfig,
                    derivedColumnsFromParentEntity: derivedColumnsFromParentEntity,
                    nestingLevel: nestingLevel,
                    parentEntityName: parentEntityName,
                    sqlMetadataProviderFactory: sqlMetadataProviderFactory));
            }
        }

        private static void ValidateObjectFieldNodes(
            IMiddlewareContext context,
            string entityName,
            InputObjectType schemaObject,
            IReadOnlyList<ObjectFieldNode> objectFieldNodes,
            RuntimeConfig runtimeConfig,
            HashSet<string> derivedColumnsFromParentEntity,
            int nestingLevel,
            string parentEntityName,
            IMetadataProviderFactory sqlMetadataProviderFactory)
        {
            Dictionary<string, HashSet<string>> derivedColumnsForRelationships = new();
            string dataSourceName = GraphQLUtils.GetDataSourceNameFromGraphQLContext(context, runtimeConfig);
            ISqlMetadataProvider metadataProvider = sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            Dictionary<string, IValueNode?> columnData = MutationOrderHelper.GetBackingColumnDataFromFields(context, entityName, objectFieldNodes, metadataProvider);
            HashSet<string> derivableBackingColumns = new(columnData.Keys);

           // When the parent entity is a referenced entity in a relationship, the values of the referencing columns
           // in the current entity is derived from the insertion in the parent entity. Hence, the input data for
           // current entity (referencing entity) must not contain values for referencing columns.
           ValidateAbsenceOfReferencingColumnsInChild(
                columnsInChildEntity: derivableBackingColumns,
                derivedColumnsFromParentEntity: derivedColumnsFromParentEntity,
                nestingLevel: nestingLevel,
                childEntityName: entityName,
                metadataProvider: metadataProvider);

            foreach (ObjectFieldNode fieldNode in objectFieldNodes)
            {
                (IValueNode? fieldValue, SyntaxKind fieldKind) = GraphQLUtils.GetFieldDetails(fieldNode.Value, context.Variables);
                if (GraphQLUtils.IsScalarField(fieldKind))
                {
                    // If the current field is a column/scalar field, continue.
                    continue;
                }

                string relationshipName = fieldNode.Name.Value;
                string targetEntityName = runtimeConfig.Entities![entityName].Relationships![relationshipName].TargetEntity;

                // A nested insert mutation like Book(parentEntityName) -> Publisher (entityName) -> Book(targetEntityName) does not make logical sense.
                // For such requests, where the same entity is present in the insertion hierarchy at a level X and a level X+2, we throw an exception.
                if (targetEntityName.Equals(parentEntityName))
                {
                    throw new DataApiBuilderException(
                        message: $"Exception!!!",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                string referencingEntityName = MutationOrderHelper.GetReferencingEntityName(
                    context: context,
                    sourceEntityName: entityName,
                    targetEntityName: targetEntityName,
                    metadataProvider: metadataProvider,
                    columnDataInSourceBody: columnData,
                    targetNodeValue: fieldValue);
                string referencedEntityName = referencingEntityName.Equals(entityName) ? targetEntityName : entityName;
                ForeignKeyDefinition fkDefinition = metadataProvider.GetFKDefinition(entityName, targetEntityName, referencedEntityName, referencingEntityName);

                // If the current entity is the referencing entity, we need to make sure that the input data for current entry does not
                // specify a value for a referencing column - as it's value will be derived from the insertion in the referenced (child) entity.
                if (referencingEntityName.Equals(entityName))
                {
                    foreach (string referencingColumn in fkDefinition.ReferencingColumns)
                    {
                        if (derivableBackingColumns.Contains(referencingColumn))
                        {
                            metadataProvider.TryGetExposedColumnName(entityName, referencingColumn, out string? exposedColumnName);
                            throw new DataApiBuilderException(
                                message: $"Either the field: {exposedColumnName} or the relationship field: {relationshipName} can be specified.",
                                statusCode: HttpStatusCode.BadRequest,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                        }

                        derivableBackingColumns.Add(referencingColumn);
                    }
                }
                else
                {
                    derivedColumnsForRelationships.Add(relationshipName, new(fkDefinition.ReferencingColumns));
                }
            }

            // For nested insertion, we make the schema such that the foreign key referencing columns become optional i.e.,
            // 1. Either the client provide the values (when it is not a nested insertion), or
            // 2. We derive the values via insertion in the referenced entity.
            // But we need to ensure that we either have a source (either via 1 or 2) for all the required columns required to do a successful insertion.
            ValidatePresenceOfRequiredColumnsForInsertion(derivableBackingColumns, entityName, metadataProvider.GetSourceDefinition(entityName));

            // Recurse to validate input data for the relationship fields.
            ValidateRelationshipFields(
                context: context,
                entityName: entityName,
                schemaObject: schemaObject,
                objectFieldNodes: objectFieldNodes,
                runtimeConfig: runtimeConfig,
                nestingLevel: nestingLevel,
                sqlMetadataProviderFactory: sqlMetadataProviderFactory,
                derivedColumnsForRelationships: derivedColumnsForRelationships);
        }

        private static void ValidatePresenceOfRequiredColumnsForInsertion(HashSet<string> derivableBackingColumns, string entityName, SourceDefinition sourceDefinition)
        {
            Dictionary<string, ColumnDefinition> columns = sourceDefinition.Columns;

            foreach((string columnName, ColumnDefinition columnDefinition) in columns)
            {
                // Must specify a value for a non-nullable column which does not have a default value.
                if (!columnDefinition.IsNullable && !columnDefinition.HasDefault && !derivableBackingColumns.Contains(columnName))
                {
                    throw new DataApiBuilderException(
                        message: $"No value found for non-null/non-default column for entity: {entityName}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }
        }

        private static void ValidateRelationshipFields(
            IMiddlewareContext context,
            string entityName,
            InputObjectType schemaObject,
            IReadOnlyList<ObjectFieldNode> objectFieldNodes,
            RuntimeConfig runtimeConfig, int nestingLevel,
            IMetadataProviderFactory sqlMetadataProviderFactory,
            Dictionary<string, HashSet<string>> derivedColumnsForRelationships)
        {
            foreach (ObjectFieldNode field in objectFieldNodes)
            {
                Tuple<IValueNode?, SyntaxKind> fieldDetails = GraphQLUtils.GetFieldDetails(field.Value, context.Variables);
                SyntaxKind fieldKind = fieldDetails.Item2;

                // For non-scalar fields, i.e. relationship fields, we have to recurse to process fields in the relationship field -
                // which represents input data for a related entity.
                if (!GraphQLUtils.IsScalarField(fieldKind))
                {
                    string relationshipName = field.Name.Value;
                    string targetEntityName = runtimeConfig.Entities![entityName].Relationships![relationshipName].TargetEntity;
                    HashSet<string>? derivedColumnsForTargetEntity;

                    // When the target entity is a referenced entity, there will be no corresponding entry for the relationship
                    // in the derivedColumnsForRelationships dictionary.
                    derivedColumnsForRelationships.TryGetValue(relationshipName, out derivedColumnsForTargetEntity);
                    ValidateGraphQLValueNode(
                        schema: schemaObject.Fields[relationshipName],
                        entityName: targetEntityName,
                        context: context,
                        parameters: fieldDetails.Item1,
                        runtimeConfig: runtimeConfig,
                        derivedColumnsFromParentEntity: derivedColumnsForTargetEntity ?? new(),
                        nestingLevel: nestingLevel,
                        parentEntityName: entityName,
                        sqlMetadataProviderFactory: sqlMetadataProviderFactory);
                }
            }
        }

        /// <summary>
        /// Helper method to validate that the referencing columns are not included in the input data for the child (referencing) entity -
        /// because the value for such referencing columns is derived from the insertion in the parent (referenced) entity.
        /// In case when a value for referencing column is also specified in the referencing entity, there can be two conflicting sources of truth,
        /// which we don't want to allow. In such a case, we throw an appropriate exception.
        /// </summary>
        /// <param name="columnsInChildEntity">Columns in the child (referencing) entity.</param>
        /// <param name="derivedColumnsFromParentEntity">Foreign key columns to be derived from the parent (referenced) entity.</param>
        /// <param name="nestingLevel">Current depth of nesting in the nested insertion.</param>
        /// <param name="childEntityName">Name of the child entity.</param>
        /// <param name="metadataProvider">Metadata provider.</param>
        private static void ValidateAbsenceOfReferencingColumnsInChild(
            HashSet<string> columnsInChildEntity,
            HashSet<string> derivedColumnsFromParentEntity,
            int nestingLevel,
            string childEntityName,
            ISqlMetadataProvider metadataProvider)
        {
            foreach (string derivedColumnFromParentEntity in derivedColumnsFromParentEntity)
            {
                if (columnsInChildEntity.Contains(derivedColumnFromParentEntity))
                {
                    metadataProvider.TryGetExposedColumnName(childEntityName, derivedColumnFromParentEntity, out string? exposedColumnName);
                    throw new DataApiBuilderException(
                        message: $"The field: {exposedColumnName} cannot be present for entity: {childEntityName} at level: {nestingLevel}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }
        }
    }
}
