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
        /// <summary>
        /// Recursive method to validate a GraphQL value node which can represent the input for item/items
        /// argument for the mutation node, or can represent an object value (*:1 relationship)
        /// or a list value (*:N relationship) node for related target entities.
        /// </summary>
        /// <param name="schema">Schema for the input field</param>
        /// <param name="entityName">Current entity's name.</param>
        /// <param name="context">Middleware Context.</param>
        /// <param name="parameters">Value for the input field.</param>
        /// <param name="runtimeConfig">Runtime config</param>
        /// <param name="columnsDerivedFromParentEntity">Set of columns in this entity whose values should be
        /// derived from insertion in the source entity (i.e. source entity would have been the referenced entity).</param>
        /// <param name="columnsToBeDerivedFromEntity">Set of columns in this entity whose values are to be
        /// derived from insertion in the this entity and returned to the source entity so as to provide values for
        /// the corresponding referencing fields (i.e. source entity would have been the referencing entity).</param>
        /// <param name="nestingLevel">Current depth of nesting in the multiple-create request.</param>
        /// <param name="parentEntityName">Parent entity's name.</param>
        /// <param name="sqlMetadataProviderFactory">Metadata provider factory.</param>
        /// <example>       1. mutation {
        ///                 createbook(
        ///                     item: {
        ///                         title: "book #1",
        ///                         reviews: [{ content: "Good book." }, { content: "Great book." }],
        ///                         publisher: { name: "Macmillan publishers" },
        ///                         authors: [{ birthdate: "1997-09-03", name: "Red house authors", author_name: "Dan Brown" }]
        ///                     })
        ///                 {
        ///                     id
        ///                 }
        ///                 2. mutation {
        ///                 createbooks(
        ///                     items: [{
        ///                         title: "book #1",
        ///                         reviews: [{ content: "Good book." }, { content: "Great book." }],
        ///                         publisher: { name: "Macmillan publishers" },
        ///                         authors: [{ birthdate: "1997-09-03", name: "Red house authors", author_name: "Dan Brown" }]
        ///                     },
        ///                     {
        ///                         title: "book #2",
        ///                         reviews: [{ content: "Awesome book." }, { content: "Average book." }],
        ///                         publisher: { name: "Pearson Education" },
        ///                         authors: [{ birthdate: "1990-11-04", name: "Penguin Random House", author_name: "William Shakespeare" }]
        ///                     }])
        ///                 {
        ///                     items{
        ///                         id
        ///                         title
        ///                     }
        ///                 }</example>
        public static void ValidateGraphQLValueNode(
            IInputField schema,
            string entityName,
            IMiddlewareContext context,
            object? parameters,
            RuntimeConfig runtimeConfig,
            HashSet<string> columnsDerivedFromParentEntity,
            HashSet<string> columnsToBeDerivedFromEntity,
            int nestingLevel,
            string parentEntityName,
            IMetadataProviderFactory sqlMetadataProviderFactory)
        {
            if (parameters is List<ObjectFieldNode> listOfObjectFieldNode)
            {
                // For the example createbook mutation written above, the object value for `item` is interpreted as a List<ObjectFieldNode> i.e.
                // all the fields present for item namely- title, reviews, publisher, authors are interpreted as ObjectFieldNode.
                ValidateObjectFieldNodes(
                    schemaObject: ResolverMiddleware.InputObjectTypeFromIInputField(schema),
                    entityName: entityName,
                    context: context,
                    objectFieldNodes: listOfObjectFieldNode,
                    runtimeConfig: runtimeConfig,
                    columnsDerivedFromSourceEntity: columnsDerivedFromParentEntity,
                    columnsToBeDerivedFromThisEntity: columnsToBeDerivedFromEntity,
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
                    columnsDerivedFromParentEntity: columnsDerivedFromParentEntity,
                    columnsToBeDerivedFromEntity: columnsToBeDerivedFromEntity,
                    nestingLevel: nestingLevel,
                    parentEntityName: parentEntityName,
                    sqlMetadataProviderFactory: sqlMetadataProviderFactory));
            }
            else if (parameters is ObjectValueNode objectValueNode)
            {
                // For the example createbook mutation written above, the node for publisher field is interpreted as an ObjectValueNode.
                // Similarly the individual node (elements in the list) for the reviews, authors ListValueNode(s) are also interpreted as ObjectValueNode(s).
                ValidateObjectFieldNodes(
                    schemaObject: ResolverMiddleware.InputObjectTypeFromIInputField(schema),
                    entityName: entityName,
                    context: context,
                    objectFieldNodes: objectValueNode.Fields,
                    runtimeConfig: runtimeConfig,
                    columnsDerivedFromSourceEntity: columnsDerivedFromParentEntity,
                    columnsToBeDerivedFromThisEntity: columnsToBeDerivedFromEntity,
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
                    columnsDerivedFromParentEntity: columnsDerivedFromParentEntity,
                    columnsToBeDerivedFromEntity: columnsToBeDerivedFromEntity,
                    nestingLevel: nestingLevel,
                    parentEntityName: parentEntityName,
                    sqlMetadataProviderFactory: sqlMetadataProviderFactory));
            }
        }

        /// <summary>
        /// Helper method to iterate over all the fields present in the input for the current field and
        /// validate that the presence/absence of fields make logical sense for the multiple-create request.
        /// </summary>
        /// <param name="schemaObject">Input object type for the field.</param>
        /// <param name="entityName">Current entity's name.</param>
        /// <param name="context">Middleware Context.</param>
        /// <param name="objectFieldNodes">List of ObjectFieldNodes for the the input field.</param>
        /// <param name="runtimeConfig">Runtime config</param>
        /// <param name="columnsDerivedFromSourceEntity">Set of columns in this entity whose values should be
        /// derived from insertion in the source entity (i.e. source entity would have been the referenced entity).</param>
        /// <param name="columnsToBeDerivedFromThisEntity">Set of columns in this entity whose values are to be
        /// derived from insertion in the this entity and returned to the source entity so as to provide values for
        /// the corresponding referencing fields (i.e. source entity would have been the referencing entity).</param>
        /// <param name="nestingLevel">Current depth of nesting in the multiple-create request.</param>
        /// <param name="parentEntityName">Parent entity's name.</param>
        /// <param name="sqlMetadataProviderFactory">Metadata provider factory.</param>
        private static void ValidateObjectFieldNodes(
            InputObjectType schemaObject,
            string entityName,
            IMiddlewareContext context,
            IReadOnlyList<ObjectFieldNode> objectFieldNodes,
            RuntimeConfig runtimeConfig,
            HashSet<string> columnsDerivedFromSourceEntity,
            HashSet<string> columnsToBeDerivedFromThisEntity,
            int nestingLevel,
            string parentEntityName,
            IMetadataProviderFactory sqlMetadataProviderFactory)
        {
            string dataSourceName = GraphQLUtils.GetDataSourceNameFromGraphQLContext(context, runtimeConfig);
            ISqlMetadataProvider metadataProvider = sqlMetadataProviderFactory.GetMetadataProvider(dataSourceName);
            SourceDefinition sourceDefinition = metadataProvider.GetSourceDefinition(entityName);
            Dictionary<string, IValueNode?> backingColumnData = MultipleCreateOrderHelper.GetBackingColumnDataFromFields(context, entityName, objectFieldNodes, metadataProvider);

            // Set of columns in the current entity whose values can be derived via:
            // a. User input
            // b. Insertion in the parent referenced entity (if current entity is a referencing entity for a relationship with its parent entity)
            // c. Insertion in the child referenced entity (if the current entity is a referencing entity for a relationship with its child entity)
            //
            // Initiliazed with the set of columns whose value is specified in the input (a).
            Dictionary<string, string> derivableColumnsFromRequestBody = new();
            foreach ((string backingColumnName, _) in backingColumnData)
            {
                derivableColumnsFromRequestBody.TryAdd(backingColumnName, entityName);
            }

            // When the parent entity is a referenced entity in a relationship, the values of the referencing columns
            // in the current entity is derived from the insertion in the parent entity. Hence, the input data for
            // current entity (referencing entity) must not contain values for referencing columns.
            ValidateAbsenceOfReferencingColumnsInTargetEntity(
                 columnsInTargetEntity: derivableColumnsFromRequestBody,
                 derivedColumnsFromSourceEntity: columnsDerivedFromSourceEntity,
                 nestingLevel: nestingLevel,
                 targetEntityName: entityName,
                 metadataProvider: metadataProvider);

            // Add all the columns whose value(s) will be derived from insertion in parent entity to the set of derivable columns (b).
            foreach (string columnDerivedFromSourceEntity in columnsDerivedFromSourceEntity)
            {
                derivableColumnsFromRequestBody.TryAdd(columnDerivedFromSourceEntity, parentEntityName);
            }

            // For the relationships with the parent entity, where the current entity is a referenced entity,
            // we need to make sure that we have non-null values for all the referenced columns - since the values for all those
            // columns will be used for insertion in the parent entity. We can get the referenced column value:
            // Case 1. Via an absolute value
            // Case 2. Via another relationship where a referenced column by the parent of the current entity is a referencing column for a child of the current entity.

            // Case 1: Remove from columnsToBeDerivedFromEntity, the columns which are autogenerated or
            // have been provided a non-null value in the input for the current entity.
            foreach (string columnToBeDerivedFromEntity in columnsToBeDerivedFromThisEntity)
            {
                if (sourceDefinition.Columns[columnToBeDerivedFromEntity].IsAutoGenerated)
                {
                    // The value for an autogenerated column is derivable.
                    columnsToBeDerivedFromThisEntity.Remove(columnToBeDerivedFromEntity);
                }
                else if (backingColumnData.TryGetValue(columnToBeDerivedFromEntity, out IValueNode? value))
                {
                    if (value is null)
                    {
                        throw new DataApiBuilderException(
                            message: $"Value cannot be null for referenced column.",
                            statusCode: HttpStatusCode.BadRequest,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    columnsToBeDerivedFromThisEntity.Remove(columnToBeDerivedFromEntity);
                }
            }

            // Dictionary storing the mapping from relationship name to the set of referencing fields for all
            // the relationships for which the current entity is the referenced entity.
            Dictionary<string, HashSet<string>> columnsDerivedForRelationships = new();

            // Dictionary storing the mapping from relationship name to the set of referenced fields for all
            // the relationships for which the current entity is the referencing entity.
            Dictionary<string, HashSet<string>> columnsToBeDerivedFromRelationships = new();

            // Loop over all the relationship fields input provided for the current entity.
            foreach (ObjectFieldNode fieldNode in objectFieldNodes)
            {
                (IValueNode? fieldValue, SyntaxKind fieldKind) = GraphQLUtils.GetFieldDetails(fieldNode.Value, context.Variables);
                if (fieldKind is SyntaxKind.NullValue || GraphQLUtils.IsScalarField(fieldKind))
                {
                    // If the current field is a column/scalar field or has a null value, continue.
                    continue;
                }

                string relationshipName = fieldNode.Name.Value;
                string targetEntityName = runtimeConfig.Entities![entityName].Relationships![relationshipName].TargetEntity;
                string? linkingObject = runtimeConfig.Entities![entityName].Relationships![relationshipName].LinkingObject;
                if (!string.IsNullOrWhiteSpace(linkingObject))
                {
                    // When a linking object is present, it indicates an M:N relationship between current entity and the target entity.
                    // For such a case, the linking table act as a referencing table for both the source/target entities, both of which act as
                    // referenced entities. In such a case, no column values for the child entity can be derived from insertion in the current entity,
                    // and no column values for the current entity can be derived from the insertion in the child entity.
                    continue;
                }

                // Determine the referencing entity for the current relationship field input.
                string referencingEntityName = MultipleCreateOrderHelper.GetReferencingEntityName(
                    context: context,
                    sourceEntityName: entityName,
                    targetEntityName: targetEntityName,
                    metadataProvider: metadataProvider,
                    columnDataInSourceBody: backingColumnData,
                    targetNodeValue: fieldValue);

                // Determine the referenced entity.
                string referencedEntityName = referencingEntityName.Equals(entityName) ? targetEntityName : entityName;

                // Get the required foreign key definition with the above inferred referencing and referenced entities.
                ForeignKeyDefinition fkDefinition = metadataProvider.GetFKDefinition(
                    sourceEntityName: entityName,
                    targetEntityName: targetEntityName,
                    referencedEntityName: referencedEntityName,
                    referencingEntityName: referencingEntityName);

                // The current entity is the referencing entity.
                if (referencingEntityName.Equals(entityName))
                {
                    for (int idx = 0; idx < fkDefinition.ReferencingColumns.Count; idx++)
                    {
                        string referencingColumn = fkDefinition.ReferencingColumns[idx];
                        string referencedColumn = fkDefinition.ReferencedColumns[idx];

                        // The input data for current entity should not specify a value for a referencing column -
                        // as it's value will be derived from the insertion in the referenced (child) entity.
                        if (derivableColumnsFromRequestBody.TryGetValue(referencingColumn, out string? referencingColumnSource))
                        {
                            string conflictingSource = referencingColumnSource.Equals(parentEntityName) ?
                                $"Parent entity: {referencingColumnSource}" : $"entity: {entityName}";
                            metadataProvider.TryGetExposedColumnName(entityName, referencingColumn, out string? exposedColumnName);
                            throw new DataApiBuilderException(
                                message: $"Found conflicting sources of values for the field: {exposedColumnName} for entity: {entityName} at level: {nestingLevel}." +
                                $"Source 1: Relationship: {relationshipName}, Source 2: {conflictingSource}",
                                statusCode: HttpStatusCode.BadRequest,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                        }

                        // Case 2: When a column whose value is to be derived from the insertion in current entity
                        // (happens when the parent entity is a referencing entity in a relationship with current entity),
                        // is a referencing column in the current relationship, we pass on the responsibility of getting the value
                        // of such a column to the child entity in the current relationship.
                        if (columnsToBeDerivedFromThisEntity.Contains(referencingColumn))
                        {
                            // We optimistically assume that we will get the value of the referencing column
                            // from the insertion in the child entity.
                            columnsToBeDerivedFromThisEntity.Remove(referencingColumn);

                            // Populate the set of fields for which the child entity for the current relationship needs to provide a value.
                            columnsToBeDerivedFromRelationships.TryAdd(relationshipName, new());
                            columnsToBeDerivedFromRelationships[relationshipName].Add(referencedColumn);
                        }

                        // All the referencing columns in the current entity can get a value via insertion in the child entity (c).
                        derivableColumnsFromRequestBody.TryAdd(referencingColumn, relationshipName);
                    }
                }
                else
                {
                    columnsDerivedForRelationships.Add(relationshipName, new(fkDefinition.ReferencingColumns));
                }
            }

            // After determining all the fields that can be derived for the current entity either:
            // 1. Via absolute value,
            // 2. Via an autogenerated value from the database,
            // 3. Via Insertion in a referenced child entity in a relationship,
            // if there are still columns which are yet to be derived, this means we don't have sufficient data to perform insertion.
            if (columnsToBeDerivedFromThisEntity.Count > 0)
            {
                throw new DataApiBuilderException(
                    message: $"Insufficient data provided for insertion in the entity: {entityName} at level: {nestingLevel}.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            // For multiple-create, we generate the schema such that the foreign key referencing columns become optional i.e.,
            // 1. Either the client provide the values (when it is a point create), or
            // 2. We derive the values via insertion in the referenced entity.
            // But we need to ensure that we either have a source (either via 1 or 2) for all the required columns required to do a successful insertion.
            ValidatePresenceOfRequiredColumnsForInsertion(derivableColumnsFromRequestBody.Keys.ToHashSet(), entityName, metadataProvider, nestingLevel);

            // Recurse to validate input data for the relationship fields.
            ValidateRelationshipFields(
                schemaObject: schemaObject,
                entityName: entityName,
                context: context,
                objectFieldNodes: objectFieldNodes,
                runtimeConfig: runtimeConfig,
                columnsDerivedForRelationships: columnsDerivedForRelationships,
                columnsToBeDerivedFromRelationships: columnsToBeDerivedFromRelationships,
                nestingLevel: nestingLevel,
                sqlMetadataProviderFactory: sqlMetadataProviderFactory);
        }

        /// <summary>
        /// Helper method to validate that we have non-null values for all the fields which are non-nullable or do not have a
        /// default value. With multiple-create, the fields which hold FK reference to other fields in same/other referenced table become optional,
        /// because the value for them may come from insertion in the aforementioned referenced table. However, providing data for
        /// insertion in the referenced table is again optional. Hence, it might be the case that the request input didn't contain
        /// data for any of the referencing field nor the referenced table. We need to invalidate such request.
        /// </summary>
        /// <param name="derivableBackingColumns">Set of backing columns in the current entity whose values can be derived.</param>
        /// <param name="entityName">Name of the entity</param>
        /// <param name="metadataProvider">Metadata provider.</param>
        /// <param name="nestingLevel">Current depth of nesting in the multiple-create request.</param>
        private static void ValidatePresenceOfRequiredColumnsForInsertion(
            HashSet<string> derivableBackingColumns,
            string entityName,
            ISqlMetadataProvider metadataProvider,
            int nestingLevel)
        {
            SourceDefinition sourceDefinition = metadataProvider.GetSourceDefinition(entityName);
            Dictionary<string, ColumnDefinition> columns = sourceDefinition.Columns;
            foreach((string columnName, ColumnDefinition columnDefinition) in columns)
            {
                // Must specify a value for a non-nullable column which does not have a default value.
                if (!columnDefinition.IsNullable && !columnDefinition.HasDefault && !columnDefinition.IsAutoGenerated && !derivableBackingColumns.Contains(columnName))
                {
                    metadataProvider.TryGetExposedColumnName(entityName, columnName, out string? exposedColumnName);
                    throw new DataApiBuilderException(
                        message: $"No value found for non-null/non-default column: {exposedColumnName} for entity: {entityName} at level: {nestingLevel}.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }
        }

        /// <summary>
        /// Helper method to validate input data for the relationship fields present in the input for the current entity.
        /// </summary>
        /// <param name="schemaObject">Input object type for the field.</param>
        /// <param name="entityName">Current entity's name.</param>
        /// <param name="context">Middleware context.</param>
        /// <param name="objectFieldNodes">List of ObjectFieldNodes for the the input field.</param>
        /// <param name="runtimeConfig">Runtime config</param>
        /// <param name="columnsDerivedForRelationships">Dictionary storing the mapping from relationship name to the set of
        /// referencing fields for all the relationships for which the current entity is the referenced entity.</param>
        /// <param name="columnsToBeDerivedFromRelationships">Dictionary storing the mapping from relationship name
        /// to the set of referenced fields for all the relationships for which the current entity is the referencing entity.</param>
        /// <param name="nestingLevel">Current depth of nesting in the multiple-create request.</param>
        /// <param name="sqlMetadataProviderFactory">Metadata provider factory.</param>
        private static void ValidateRelationshipFields(
            InputObjectType schemaObject,
            string entityName,
            IMiddlewareContext context,           
            IReadOnlyList<ObjectFieldNode> objectFieldNodes,
            RuntimeConfig runtimeConfig,
            Dictionary<string, HashSet<string>> columnsDerivedForRelationships,
            Dictionary<string, HashSet<string>> columnsToBeDerivedFromRelationships,
            int nestingLevel,
            IMetadataProviderFactory sqlMetadataProviderFactory)
        {
            foreach (ObjectFieldNode field in objectFieldNodes)
            {
                Tuple<IValueNode?, SyntaxKind> fieldDetails = GraphQLUtils.GetFieldDetails(field.Value, context.Variables);
                SyntaxKind fieldKind = fieldDetails.Item2;

                // For non-scalar fields, i.e. relationship fields, we have to recurse to process fields in the relationship field -
                // which represents input data for a related entity.
                if (fieldKind is not SyntaxKind.NullValue && !GraphQLUtils.IsScalarField(fieldKind))
                {
                    string relationshipName = field.Name.Value;
                    string targetEntityName = runtimeConfig.Entities![entityName].Relationships![relationshipName].TargetEntity;
                    HashSet<string>? derivedColumnsForEntity, columnsToBeDerivedFromEntity;

                    // When the current entity is a referenced entity, there will be no corresponding entry for the relationship
                    // in the derivedColumnsForRelationships dictionary.
                    columnsDerivedForRelationships.TryGetValue(relationshipName, out derivedColumnsForEntity);

                    // When the current entity is a referencing entity, there will be no corresponding entry for the relationship
                    // in the columnsToBeDerivedFromRelationships dictionary.
                    columnsToBeDerivedFromRelationships.TryGetValue(relationshipName, out columnsToBeDerivedFromEntity);
                    ValidateGraphQLValueNode(
                        schema: schemaObject.Fields[relationshipName],
                        entityName: targetEntityName,
                        context: context,
                        parameters: fieldDetails.Item1,
                        runtimeConfig: runtimeConfig,
                        columnsDerivedFromParentEntity: derivedColumnsForEntity ?? new(),
                        columnsToBeDerivedFromEntity: columnsToBeDerivedFromEntity ?? new(),
                        nestingLevel: nestingLevel,
                        parentEntityName: entityName,
                        sqlMetadataProviderFactory: sqlMetadataProviderFactory);
                }
            }
        }

        /// <summary>
        /// Helper method to validate that the referencing columns are not included in the input data for the target (referencing) entity -
        /// because the value for such referencing columns is derived from the insertion in the source (referenced) entity.
        /// In case when a value for referencing column is also specified in the referencing entity, there can be two conflicting sources of truth,
        /// which we don't want to allow. In such a case, we throw an appropriate exception.
        /// </summary>
        /// <param name="columnsInTargetEntity">Columns in the target (referencing) entity.</param>
        /// <param name="derivedColumnsFromSourceEntity">Foreign key columns to be derived from the source (referenced) entity.</param>
        /// <param name="nestingLevel">Current depth of nesting in the multiple-create GraphQL request.</param>
        /// <param name="targetEntityName">Name of the target entity.</param>
        /// <param name="metadataProvider">Metadata provider.</param>
        private static void ValidateAbsenceOfReferencingColumnsInTargetEntity(
            Dictionary<string, string> columnsInTargetEntity,
            HashSet<string> derivedColumnsFromSourceEntity,
            int nestingLevel,
            string targetEntityName,
            ISqlMetadataProvider metadataProvider)
        {
            foreach (string derivedColumnFromParentEntity in derivedColumnsFromSourceEntity)
            {
                if (columnsInTargetEntity.ContainsKey(derivedColumnFromParentEntity))
                {
                    metadataProvider.TryGetExposedColumnName(targetEntityName, derivedColumnFromParentEntity, out string? exposedColumnName);
                    throw new DataApiBuilderException(
                        message: $"The field: {exposedColumnName} cannot be present for entity: {targetEntityName} at level: {nestingLevel}.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }
        }
    }
}
