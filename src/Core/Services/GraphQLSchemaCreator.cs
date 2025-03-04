// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Used to generate a GraphQL schema from the provided database.
    ///
    /// This will take the provided database object model for entities and
    /// combine it with the runtime configuration to apply the auth config.
    ///
    /// It also generates the middleware resolvers used for the queries
    /// and mutations, based off the provided <c>IQueryEngine</c> and
    /// <c>IMutationEngine</c> for the runtime.
    /// </summary>
    public class GraphQLSchemaCreator
    {
        private readonly IQueryEngineFactory _queryEngineFactory;
        private readonly IMutationEngineFactory _mutationEngineFactory;
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private RuntimeEntities _entities;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private bool _isMultipleCreateOperationEnabled;
        private bool _isAggregationEnabled;

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphQLSchemaCreator"/> class.
        /// </summary>
        /// <param name="runtimeConfigProvider">Runtime config provided for the instance.</param>
        /// <param name="queryEngineFactory">QueryEngineFactory to retreive query engine to be used by resolvers.</param>
        /// <param name="mutationEngineFactory">MutationEngineFactory to retreive mutation engine to be used by resolvers.</param>
        /// <param name="metadataProviderFactory">MetadataProviderFactory to get metadata provider used when generating the SQL-based GraphQL schema. Ignored if the runtime is Cosmos.</param>
        /// <param name="authorizationResolver">Authorization information for the runtime, to be applied to the GraphQL schema.</param>
        /// <param name="handler">Optional hot-reload event handler to subscribe to the config change event.</param>
        public GraphQLSchemaCreator(
            RuntimeConfigProvider runtimeConfigProvider,
            IQueryEngineFactory queryEngineFactory,
            IMutationEngineFactory mutationEngineFactory,
            IMetadataProviderFactory metadataProviderFactory,
            IAuthorizationResolver authorizationResolver,
            HotReloadEventHandler<HotReloadEventArgs>? handler = null)
        {
            handler?.Subscribe(DabConfigEvents.GRAPHQL_SCHEMA_CREATOR_ON_CONFIG_CHANGED, OnConfigChanged);
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

            _isMultipleCreateOperationEnabled = runtimeConfig.IsMultipleCreateOperationEnabled();
            _isAggregationEnabled = runtimeConfig.EnableAggregation;

            _entities = runtimeConfig.Entities;
            _queryEngineFactory = queryEngineFactory;
            _mutationEngineFactory = mutationEngineFactory;
            _metadataProviderFactory = metadataProviderFactory;
            _authorizationResolver = authorizationResolver;
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Executed when a hot-reload event occurs. Pulls the latest
        /// runtimeconfig object from the provider and updates the flag indicating
        /// whether multiple create operations are enabled, and the entities based on the new config.
        /// </summary>
        protected void OnConfigChanged(object? sender, HotReloadEventArgs args)
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            _isMultipleCreateOperationEnabled = runtimeConfig.IsMultipleCreateOperationEnabled();
            _entities = runtimeConfig.Entities;
        }

        /// <summary>
        /// Take the raw GraphQL objects and generate the full schema from them.
        /// At this point, we're somewhat agnostic to whether the runtime is Cosmos or SQL
        /// as we're working with GraphQL object types, regardless of where they came from.
        /// </summary>
        /// <param name="sb">Schema builder</param>
        /// <param name="root">Root document containing the GraphQL object and input types.</param>
        /// <param name="inputTypes">Reference table of the input types for query lookup.</param>
        private ISchemaBuilder Parse(
            ISchemaBuilder sb,
            DocumentNode root,
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            // Generate the Query and the Mutation Node.
            (DocumentNode queryNode, DocumentNode mutationNode) = GenerateQueryAndMutationNodes(root, inputTypes);

            return sb
                .AddDocument(root)
                .AddAuthorizeDirectiveType()
                // Add our custom directives
                .AddDirectiveType<ModelDirectiveType>()
                .AddDirectiveType<RelationshipDirectiveType>()
                .AddDirectiveType<PrimaryKeyDirectiveType>()
                .AddDirectiveType<ReferencingFieldDirectiveType>()
                .AddDirectiveType<DefaultValueDirectiveType>()
                .AddDirectiveType<AutoGeneratedDirectiveType>()
                // Add our custom scalar GraphQL types
                .AddType<OrderByType>()
                .AddType<DefaultValueType>()
                // Generate the GraphQL queries from the provided objects
                .AddDocument(queryNode)
                // Generate the GraphQL mutations from the provided objects
                .AddDocument(mutationNode)
                // Enable the OneOf directive (https://github.com/graphql/graphql-spec/pull/825) to support the DefaultValue type
                .ModifyOptions(o => o.EnableOneOf = true)
                // Adds our type interceptor that will create the resolvers.
                .TryAddTypeInterceptor(new ResolverTypeInterceptor(new ExecutionHelper(_queryEngineFactory, _mutationEngineFactory, _runtimeConfigProvider)));
        }

        /// <summary>
        /// Generate the GraphQL schema query and mutation nodes from the provided database.
        /// </summary>
        /// <param name="root">Root document node which contains base entity types.</param>
        /// <param name="inputTypes">Dictionary with key being the object and value the input object type definition node for that object.</param>
        /// <returns>Query and mutation nodes.</returns>
        public (DocumentNode, DocumentNode) GenerateQueryAndMutationNodes(DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            Dictionary<string, DatabaseObject> entityToDbObjects = new();
            Dictionary<string, DatabaseType> entityToDatabaseType = new();

            HashSet<string> dataSourceNames = new();

            // Merge the entityToDBObjects for queryNode generation for all entities.
            foreach ((string entityName, _) in _entities)
            {
                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
                ISqlMetadataProvider metadataprovider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                if (!dataSourceNames.Contains(dataSourceName))
                {
                    entityToDbObjects = entityToDbObjects.Concat(metadataprovider.EntityToDatabaseObject).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    dataSourceNames.Add(dataSourceName);
                }

                entityToDatabaseType.TryAdd(entityName, metadataprovider.GetDatabaseType());
            }
            // Generate the GraphQL queries from the provided objects
            DocumentNode queryNode = QueryBuilder.Build(root, entityToDatabaseType, _entities, inputTypes, _authorizationResolver.EntityPermissionsMap, entityToDbObjects, _isAggregationEnabled);

            // Generate the GraphQL mutations from the provided objects
            DocumentNode mutationNode = MutationBuilder.Build(root, entityToDatabaseType, _entities, _authorizationResolver.EntityPermissionsMap, entityToDbObjects, _isMultipleCreateOperationEnabled);

            return (queryNode, mutationNode);
        }

        /// <summary>
        /// If the metastore provider is able to get the graphql schema,
        /// this function parses it and attaches resolvers to the various query fields.
        /// </summary>
        /// <exception cref="NotImplementedException">Thrown if the database type is not supported</exception>
        /// <returns>The <c>ISchemaBuilder</c> for HotChocolate, with the generated GraphQL schema</returns>
        public ISchemaBuilder InitializeSchemaAndResolvers(ISchemaBuilder schemaBuilder)
        {
            (DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes) = GenerateGraphQLObjects();

            return Parse(schemaBuilder, root, inputTypes);
        }

        /// <summary>
        /// Generates the ObjectTypeDefinitionNodes and InputObjectTypeDefinitionNodes as part of GraphQL Schema generation
        /// with the provided entities listed in the runtime configuration that match the provided database type.
        /// </summary>
        /// <param name="entities">Key/Value Collection {entityName -> Entity object}</param>
        /// <returns>Root GraphQLSchema DocumentNode and inputNodes to be processed by downstream schema generation helpers.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        private DocumentNode GenerateSqlGraphQLObjects(RuntimeEntities entities, Dictionary<string, InputObjectTypeDefinitionNode> inputObjects)
        {
            // Dictionary to store:
            // 1. Object types for every entity exposed for MySql/PgSql/MsSql/DwSql in the config file.
            // 2. Object type for source->target linking object for M:N relationships to support insertion in the target table,
            // followed by an insertion in the linking table. The directional linking object contains all the fields from the target entity
            // (relationship/column) and non-relationship fields from the linking table.
            Dictionary<string, ObjectTypeDefinitionNode> objectTypes = new();

            Dictionary<string, EnumTypeDefinitionNode> enumTypes = new();

            // 1. Build up the object and input types for all the exposed entities in the config.
            foreach ((string entityName, Entity entity) in entities)
            {
                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
                ISqlMetadataProvider sqlMetadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                // Skip creating the GraphQL object for the current entity due to configuration
                // explicitly excluding the entity from the GraphQL endpoint.
                if (!entity.GraphQL.Enabled)
                {
                    continue;
                }

                if (sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(entityName, out DatabaseObject? databaseObject))
                {
                    // Collection of role names allowed to access entity, to be added to the authorize directive
                    // of the objectTypeDefinitionNode. The authorize Directive is one of many directives created.
                    IEnumerable<string> rolesAllowedForEntity = _authorizationResolver.GetRolesForEntity(entityName);
                    Dictionary<string, IEnumerable<string>> rolesAllowedForFields = new();
                    SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);
                    bool isStoredProcedure = entity.Source.Type is EntitySourceType.StoredProcedure;
                    foreach (string column in sourceDefinition.Columns.Keys)
                    {
                        EntityActionOperation operation = isStoredProcedure ? EntityActionOperation.Execute : EntityActionOperation.Read;
                        IEnumerable<string> roles = _authorizationResolver.GetRolesForField(entityName, field: column, operation: operation);
                        if (!rolesAllowedForFields.TryAdd(key: column, value: roles))
                        {
                            throw new DataApiBuilderException(
                                message: "Column already processed for building ObjectTypeDefinition authorization definition.",
                                statusCode: HttpStatusCode.InternalServerError,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization
                                );
                        }
                    }

                    // The roles allowed for Fields are the roles allowed to READ the fields, so any role that has a read definition for the field.
                    // Only add objectTypeDefinition for GraphQL if it has a role definition defined for access.
                    if (rolesAllowedForEntity.Any())
                    {
                        ObjectTypeDefinitionNode node = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                            entityName: entityName,
                            databaseObject: databaseObject,
                            configEntity: entity,
                            entities: entities,
                            rolesAllowedForEntity: rolesAllowedForEntity,
                            rolesAllowedForFields: rolesAllowedForFields);

                        if (databaseObject.SourceType is not EntitySourceType.StoredProcedure)
                        {
                            InputTypeBuilder.GenerateInputTypesForObjectType(node, inputObjects);

                            if (_isAggregationEnabled)
                            {
                                bool isAggregationEnumCreated = EnumTypeBuilder.GenerateAggregationNumericEnumForObjectType(node, enumTypes);
                                bool isGroupByColumnsEnumCreated = EnumTypeBuilder.GenerateScalarFieldsEnumForObjectType(node, enumTypes);
                                ObjectTypeDefinitionNode aggregationType;
                                ObjectTypeDefinitionNode groupByEntityNode;

                                // note: if aggregation enum is created, groupByColumnsEnum is also created as there would be scalar fields to groupby.
                                if (isAggregationEnumCreated)
                                {
                                    // Both aggregation and group by columns enum types are created for the entity. GroupBy should include fields and aggregation subfields.
                                    aggregationType = SchemaConverter.GenerateAggregationTypeForEntity(node.Name.Value, node);
                                    groupByEntityNode = SchemaConverter.GenerateGroupByTypeForEntity(node.Name.Value, node);
                                    IReadOnlyList<FieldDefinitionNode> groupByFields = groupByEntityNode.Fields;
                                    string aggregationsTypeName = SchemaConverter.GenerateObjectAggregationNodeName(node.Name.Value);
                                    FieldDefinitionNode aggregationNode = new(
                                        location: null,
                                        name: new NameNode(QueryBuilder.GROUP_BY_AGGREGATE_FIELD_NAME),
                                        description: new StringValueNode($"Aggregations for {entityName}"),
                                        arguments: new List<InputValueDefinitionNode>(),
                                        type: new NamedTypeNode(new NameNode(aggregationsTypeName)),
                                        directives: new List<DirectiveNode>()
                                    );
                                    List<FieldDefinitionNode> fieldDefinitionNodes = new(groupByFields) { aggregationNode };
                                    groupByEntityNode = groupByEntityNode.WithFields(fieldDefinitionNodes);
                                    objectTypes.Add(SchemaConverter.GenerateObjectAggregationNodeName(entityName), aggregationType);
                                    objectTypes.Add(SchemaConverter.GenerateGroupByTypeName(entityName), groupByEntityNode);
                                }
                                else if (isGroupByColumnsEnumCreated)
                                {
                                    // only groupBy enum is created for the entity. GroupBy should include fields but not aggregations.
                                    groupByEntityNode = SchemaConverter.GenerateGroupByTypeForEntity(entityName, node);
                                    objectTypes.Add(SchemaConverter.GenerateGroupByTypeName(entityName), groupByEntityNode);
                                }
                            }
                        }

                        objectTypes.Add(entityName, node);
                    }
                }
                else
                {
                    throw new DataApiBuilderException(message: $"Database Object definition for {entityName} has not been inferred.",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }
            }

            // ReferencingFieldDirective is added to eventually mark the referencing fields in the input object types as optional. When multiple create operations are disabled
            // the referencing fields should be required fields. Hence, ReferencingFieldDirective is added only when the multiple create operations are enabled.
            if (_isMultipleCreateOperationEnabled)
            {
                // For all the fields in the object which hold a foreign key reference to any referenced entity, add a foreign key directive.
                AddReferencingFieldDirective(entities, objectTypes);
            }

            // Pass two - Add the arguments to the many-to-* relationship fields
            foreach ((string entityName, ObjectTypeDefinitionNode node) in objectTypes)
            {
                objectTypes[entityName] = QueryBuilder.AddQueryArgumentsForRelationships(node, inputObjects);
            }

            // Create ObjectTypeDefinitionNode for linking entities. These object definitions are not exposed in the schema
            // but are used to generate the object definitions of directional linking entities for (source, target) and (target, source) entities.
            // However, ObjectTypeDefinitionNode for linking entities are need only for multiple create operation. So, creating these only when multiple create operations are
            // enabled.
            if (_isMultipleCreateOperationEnabled)
            {
                Dictionary<string, ObjectTypeDefinitionNode> linkingObjectTypes = GenerateObjectDefinitionsForLinkingEntities();
                GenerateSourceTargetLinkingObjectDefinitions(objectTypes, linkingObjectTypes);
            }

            // Return a list of all the object types to be exposed in the schema.
            Dictionary<string, FieldDefinitionNode> fields = new();

            // Add the DBOperationResult type to the schema
            NameNode nameNode = new(value: GraphQLUtils.DB_OPERATION_RESULT_TYPE);
            FieldDefinitionNode field = GetDbOperationResultField();

            fields.TryAdd(GraphQLUtils.DB_OPERATION_RESULT_FIELD_NAME, field);

            objectTypes.Add(GraphQLUtils.DB_OPERATION_RESULT_TYPE, new ObjectTypeDefinitionNode(
                location: null,
                name: nameNode,
                description: null,
                new List<DirectiveNode>(),
                new List<NamedTypeNode>(),
                fields.Values.ToImmutableList()));

            List<IDefinitionNode> nodes = new(objectTypes.Values);
            nodes.AddRange(enumTypes.Values);
            return new DocumentNode(nodes);
        }

        /// <summary>
        /// Helper method to traverse through all the relationships for all the entities exposed in the config.
        /// For all the relationships defined in each entity's configuration, it adds a referencing field directive to all the
        /// referencing fields of the referencing entity in the relationship. For relationships defined in config:
        /// 1. If an FK constraint exists between the entities - the referencing field directive
        /// is added to the referencing fields from the referencing entity.
        /// 2. If no FK constraint exists between the entities - the referencing field directive
        /// is added to the source.fields/target.fields from both the source and target entities.
        ///
        /// The values of such fields holding foreign key references can come via insertions in the related entity.
        /// By adding ForiegnKeyDirective here, we can later ensure that while creating input type for create mutations,
        /// these fields can be marked as nullable/optional.
        /// </summary>
        /// <param name="objectTypes">Collection of object types.</param>
        /// <param name="entities">Entities from runtime config.</param>
        private void AddReferencingFieldDirective(RuntimeEntities entities, Dictionary<string, ObjectTypeDefinitionNode> objectTypes)
        {
            foreach ((string sourceEntityName, ObjectTypeDefinitionNode sourceObjectTypeDefinitionNode) in objectTypes)
            {
                if (!entities.TryGetValue(sourceEntityName, out Entity? entity))
                {
                    continue;
                }

                if (!entity.GraphQL.Enabled || entity.Source.Type is not EntitySourceType.Table || entity.Relationships is null)
                {
                    // Multiple create is only supported on database tables for which GraphQL endpoint is enabled.
                    continue;
                }

                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(sourceEntityName);
                ISqlMetadataProvider sqlMetadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(sourceEntityName);
                Dictionary<string, FieldDefinitionNode> sourceFieldDefinitions = sourceObjectTypeDefinitionNode.Fields.ToDictionary(field => field.Name.Value, field => field);

                // Retrieve all the relationship information for the source entity which is backed by this table definition.
                sourceDefinition.SourceEntityRelationshipMap.TryGetValue(sourceEntityName, out RelationshipMetadata? relationshipInfo);

                // Retrieve the database object definition for the source entity.
                sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(sourceEntityName, out DatabaseObject? sourceDbo);
                foreach ((_, EntityRelationship relationship) in entity.Relationships)
                {
                    string targetEntityName = relationship.TargetEntity;
                    if (!string.IsNullOrEmpty(relationship.LinkingObject))
                    {
                        // The presence of LinkingObject indicates that the relationship is a M:N relationship. For M:N relationships,
                        // the fields in this entity are referenced fields and the fields in the linking table are referencing fields.
                        // Thus, it is not required to add the directive to any field in this entity.
                        continue;
                    }

                    // From the relationship information, obtain the foreign key definition for the given target entity and add the
                    // referencing field directive to the referencing fields from the referencing table (whether it is the source entity or the target entity).
                    if (relationshipInfo is not null &&
                        relationshipInfo.TargetEntityToFkDefinitionMap.TryGetValue(targetEntityName, out List<ForeignKeyDefinition>? listOfForeignKeys))
                    {
                        // Find the foreignkeys in which the source entity is the referencing object.
                        IEnumerable<ForeignKeyDefinition> sourceReferencingForeignKeysInfo =
                            listOfForeignKeys.Where(fk =>
                                fk.ReferencingColumns.Count > 0
                                && fk.ReferencedColumns.Count > 0
                                && fk.Pair.ReferencingDbTable.Equals(sourceDbo));

                        sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(targetEntityName, out DatabaseObject? targetDbo);
                        // Find the foreignkeys in which the target entity is the referencing object, i.e. source entity is the referenced object.
                        IEnumerable<ForeignKeyDefinition> targetReferencingForeignKeysInfo =
                            listOfForeignKeys.Where(fk =>
                                fk.ReferencingColumns.Count > 0
                                && fk.ReferencedColumns.Count > 0
                                && fk.Pair.ReferencingDbTable.Equals(targetDbo));

                        ForeignKeyDefinition? sourceReferencingFKInfo = sourceReferencingForeignKeysInfo.FirstOrDefault();
                        if (sourceReferencingFKInfo is not null)
                        {
                            // When source entity is the referencing entity, referencing field directive is to be added to relationship fields
                            // in the source entity.
                            AddReferencingFieldDirectiveToReferencingFields(sourceFieldDefinitions, sourceReferencingFKInfo.ReferencingColumns, sqlMetadataProvider, sourceEntityName);
                        }

                        ForeignKeyDefinition? targetReferencingFKInfo = targetReferencingForeignKeysInfo.FirstOrDefault();
                        if (targetReferencingFKInfo is not null &&
                            objectTypes.TryGetValue(targetEntityName, out ObjectTypeDefinitionNode? targetObjectTypeDefinitionNode))
                        {
                            Dictionary<string, FieldDefinitionNode> targetFieldDefinitions = targetObjectTypeDefinitionNode.Fields.ToDictionary(field => field.Name.Value, field => field);
                            // When target entity is the referencing entity, referencing field directive is to be added to relationship fields
                            // in the target entity.
                            AddReferencingFieldDirectiveToReferencingFields(targetFieldDefinitions, targetReferencingFKInfo.ReferencingColumns, sqlMetadataProvider, targetEntityName);

                            // Update the target object definition with the new set of fields having referencing field directive.
                            objectTypes[targetEntityName] = targetObjectTypeDefinitionNode.WithFields(new List<FieldDefinitionNode>(targetFieldDefinitions.Values));
                        }
                    }
                }

                // Update the source object definition with the new set of fields having referencing field directive.
                objectTypes[sourceEntityName] = sourceObjectTypeDefinitionNode.WithFields(new List<FieldDefinitionNode>(sourceFieldDefinitions.Values));
            }
        }

        /// <summary>
        /// Helper method to add referencing field directive type to all the fields in the entity which
        /// hold a foreign key reference to another entity exposed in the config, related via a relationship.
        /// </summary>
        /// <param name="referencingEntityFieldDefinitions">Field definitions of the referencing entity.</param>
        /// <param name="referencingColumns">Referencing columns in the relationship.</param>
        private static void AddReferencingFieldDirectiveToReferencingFields(
            Dictionary<string, FieldDefinitionNode> referencingEntityFieldDefinitions,
            List<string> referencingColumns,
            ISqlMetadataProvider metadataProvider,
            string entityName)
        {
            foreach (string referencingColumn in referencingColumns)
            {
                if (metadataProvider.TryGetExposedColumnName(entityName, referencingColumn, out string? exposedReferencingColumnName) &&
                    referencingEntityFieldDefinitions.TryGetValue(exposedReferencingColumnName, out FieldDefinitionNode? referencingFieldDefinition))
                {
                    if (!referencingFieldDefinition.Directives.Any(directive => directive.Name.Value == ReferencingFieldDirectiveType.DirectiveName))
                    {
                        List<DirectiveNode> directiveNodes = referencingFieldDefinition.Directives.ToList();
                        directiveNodes.Add(new DirectiveNode(ReferencingFieldDirectiveType.DirectiveName));
                        referencingEntityFieldDefinitions[exposedReferencingColumnName] = referencingFieldDefinition.WithDirectives(directiveNodes);
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to generate object definitions for linking entities. These object definitions are used later
        /// to generate the object definitions for directional linking entities for (source, target) and (target, source).
        /// </summary>
        /// <returns>Object definitions for linking entities.</returns>
        private Dictionary<string, ObjectTypeDefinitionNode> GenerateObjectDefinitionsForLinkingEntities()
        {
            IEnumerable<ISqlMetadataProvider> sqlMetadataProviders = _metadataProviderFactory.ListMetadataProviders();
            Dictionary<string, ObjectTypeDefinitionNode> linkingObjectTypes = new();
            foreach (ISqlMetadataProvider sqlMetadataProvider in sqlMetadataProviders)
            {
                foreach ((string linkingEntityName, Entity linkingEntity) in sqlMetadataProvider.GetLinkingEntities())
                {
                    if (sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(linkingEntityName, out DatabaseObject? linkingDbObject))
                    {
                        ObjectTypeDefinitionNode node = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                                entityName: linkingEntityName,
                                databaseObject: linkingDbObject,
                                configEntity: linkingEntity,
                                entities: new(new Dictionary<string, Entity>()),
                                rolesAllowedForEntity: new List<string>(),
                                rolesAllowedForFields: new Dictionary<string, IEnumerable<string>>()
                            );

                        linkingObjectTypes.Add(linkingEntityName, node);
                    }
                }
            }

            return linkingObjectTypes;
        }

        /// <summary>
        /// Helper method to generate object types for linking nodes from (source, target) using
        /// simple linking nodes which represent a linking table linking the source and target tables which have an M:N relationship between them.
        /// A 'sourceTargetLinkingNode' will contain:
        /// 1. All the fields (column/relationship) from the target node,
        /// 2. Column fields from the linking node which are not part of the Foreign key constraint (or relationship fields when the relationship
        /// is defined in the config).
        /// </summary>
        /// <example>
        /// Target node definition contains fields: TField1, TField2, TField3
        /// Linking node definition contains fields:  LField1, LField2, LField3
        /// Relationship : linkingTable(Lfield3) -> targetTable(TField3)
        ///
        /// Result:
        /// SourceTargetLinkingNodeDefinition contains fields:
        /// 1. TField1, TField2, TField3 (All the fields from the target node.)
        /// 2. LField1, LField2 (Non-relationship fields from linking table.)
        /// </example>
        /// <param name="objectTypes">Collection of object types.</param>
        /// <param name="linkingObjectTypes">Collection of object types for linking entities.</param>
        private void GenerateSourceTargetLinkingObjectDefinitions(
            Dictionary<string, ObjectTypeDefinitionNode> objectTypes,
            Dictionary<string, ObjectTypeDefinitionNode> linkingObjectTypes)
        {
            foreach ((string linkingEntityName, ObjectTypeDefinitionNode linkingObjectDefinition) in linkingObjectTypes)
            {
                (string sourceEntityName, string targetEntityName) = GraphQLUtils.GetSourceAndTargetEntityNameFromLinkingEntityName(linkingEntityName);
                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(targetEntityName);
                ISqlMetadataProvider sqlMetadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                if (sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(sourceEntityName, out DatabaseObject? sourceDbo))
                {
                    IEnumerable<ForeignKeyDefinition> foreignKeyDefinitionsFromSourceToTarget = sourceDbo.SourceDefinition.SourceEntityRelationshipMap[sourceEntityName].TargetEntityToFkDefinitionMap[targetEntityName];

                    // Get list of all referencing columns from the foreign key definition. For an M:N relationship,
                    // all the referencing columns belong to the linking entity.
                    HashSet<string> referencingColumnNamesInLinkingEntity = new(foreignKeyDefinitionsFromSourceToTarget.SelectMany(foreignKeyDefinition => foreignKeyDefinition.ReferencingColumns).ToList());

                    // Store the names of relationship/column fields in the target entity to prevent conflicting names
                    // with the linking table's column fields.
                    ObjectTypeDefinitionNode targetNode = objectTypes[targetEntityName];
                    HashSet<string> fieldNamesInTarget = targetNode.Fields.Select(field => field.Name.Value).ToHashSet();

                    // Initialize list of fields in the sourceTargetLinkingNode with the set of fields present in the target node.
                    List<FieldDefinitionNode> fieldsInSourceTargetLinkingNode = targetNode.Fields.ToList();

                    // Get list of fields in the linking node (which represents columns present in the linking table).
                    List<FieldDefinitionNode> fieldsInLinkingNode = linkingObjectDefinition.Fields.ToList();

                    // The sourceTargetLinkingNode will contain:
                    // 1. All the fields from the target node to perform insertion on the target entity,
                    // 2. Fields from the linking node which are not a foreign key reference to source or target node. This is needed to perform
                    // an insertion in the linking table. For the foreign key columns in linking table, the values are derived from the insertions in the
                    // source and the target table. For the rest of the columns, the value will be provided via a field exposed in the sourceTargetLinkingNode.
                    foreach (FieldDefinitionNode fieldInLinkingNode in fieldsInLinkingNode)
                    {
                        string fieldName = fieldInLinkingNode.Name.Value;
                        if (!referencingColumnNamesInLinkingEntity.Contains(fieldName))
                        {
                            if (fieldNamesInTarget.Contains(fieldName))
                            {
                                // The fieldName can represent a column in the targetEntity or a relationship.
                                // The fieldName in the linking node cannot conflict with any of the
                                // existing field names (either column name or relationship name) in the target node.
                                bool doesFieldRepresentAColumn = sqlMetadataProvider.TryGetBackingColumn(targetEntityName, fieldName, out string? _);
                                string infoMsg = $"Cannot use field name '{fieldName}' as it conflicts with another field's name in the entity: {targetEntityName}. ";
                                string actionableMsg = doesFieldRepresentAColumn ?
                                    $"Consider using the 'mappings' section of the {targetEntityName} entity configuration to provide some other name for the field: '{fieldName}'." :
                                    $"Consider using the 'relationships' section of the {targetEntityName} entity configuration to provide some other name for the relationship: '{fieldName}'.";
                                throw new DataApiBuilderException(
                                    message: infoMsg + actionableMsg,
                                    statusCode: HttpStatusCode.ServiceUnavailable,
                                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                            }
                            else
                            {
                                fieldsInSourceTargetLinkingNode.Add(fieldInLinkingNode);
                            }
                        }
                    }

                    // Store object type of the linking node for (sourceEntityName, targetEntityName).
                    NameNode sourceTargetLinkingNodeName = new(GenerateLinkingNodeName(
                        objectTypes[sourceEntityName].Name.Value,
                        targetNode.Name.Value));
                    objectTypes.TryAdd(sourceTargetLinkingNodeName.Value,
                        new(
                            location: null,
                            name: sourceTargetLinkingNodeName,
                            description: null,
                            new List<DirectiveNode>() { },
                            new List<NamedTypeNode>(),
                            fieldsInSourceTargetLinkingNode));
                }
            }
        }

        /// <summary>
        /// Generates the ObjectTypeDefinitionNodes and InputObjectTypeDefinitionNodes as part of GraphQL Schema generation for cosmos db.
        /// Each datasource in cosmos has a root file provided which is used to generate the schema.
        /// NOTE: DataSourceNames must be preFiltered to be cosmos datasources.
        /// </summary>
        /// <param name="dataSourceNames">Hashset of datasourceNames to generate cosmos objects.</param>
        private DocumentNode GenerateCosmosGraphQLObjects(HashSet<string> dataSourceNames, Dictionary<string, InputObjectTypeDefinitionNode> inputObjects)
        {
            DocumentNode? root = null;

            if (dataSourceNames.Count() == 0)
            {
                return new DocumentNode(new List<IDefinitionNode>());
            }

            foreach (string dataSourceName in dataSourceNames)
            {
                ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                DocumentNode currentNode = ((CosmosSqlMetadataProvider)metadataProvider).GraphQLSchemaRoot;
                root = root is null ? currentNode : root.WithDefinitions(root.Definitions.Concat(currentNode.Definitions).ToImmutableList());
            }

            IEnumerable<ObjectTypeDefinitionNode> objectNodes = root!.Definitions.Where(d => d is ObjectTypeDefinitionNode).Cast<ObjectTypeDefinitionNode>();
            foreach (ObjectTypeDefinitionNode node in objectNodes)
            {
                InputTypeBuilder.GenerateInputTypesForObjectType(node, inputObjects);
            }

            return root;
        }

        /// <summary>
        /// Create and return a default GraphQL result field for a mutation which doesn't
        /// define a result set and doesn't return any rows.
        /// </summary>
        private static FieldDefinitionNode GetDbOperationResultField()
        {
            return new(
                location: null,
                name: new(GraphQLUtils.DB_OPERATION_RESULT_FIELD_NAME),
                description: new StringValueNode("Contains result for mutation execution"),
                arguments: new List<InputValueDefinitionNode>(),
                type: new StringType().ToTypeNode(),
                directives: new List<DirectiveNode>());
        }

        public (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateGraphQLObjects()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            HashSet<string> cosmosDataSourceNames = new();
            IDictionary<string, Entity> sqlEntities = new Dictionary<string, Entity>();
            Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new();

            foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
            {
                DataSource ds = runtimeConfig.GetDataSourceFromEntityName(entityName);

                switch (ds.DatabaseType)
                {
                    case DatabaseType.CosmosDB_NoSQL:
                        cosmosDataSourceNames.Add(_runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName));
                        break;
                    case DatabaseType.MSSQL or DatabaseType.MySQL or DatabaseType.PostgreSQL or DatabaseType.DWSQL:
                        sqlEntities.TryAdd(entityName, entity);
                        break;
                    default:
                        throw new NotImplementedException($"This database type {ds.DatabaseType} is not yet implemented.");
                }
            }

            RuntimeEntities sql = new(new ReadOnlyDictionary<string, Entity>(sqlEntities));

            DocumentNode cosmosResult = GenerateCosmosGraphQLObjects(cosmosDataSourceNames, inputObjects);
            DocumentNode sqlResult = GenerateSqlGraphQLObjects(sql, inputObjects);
            // Create Root node with definitions from both cosmos and sql.
            DocumentNode root = new(cosmosResult.Definitions.Concat(sqlResult.Definitions).ToImmutableList());

            // Merge the inputobjectType definitions from cosmos and sql onto the root.
            return (root.WithDefinitions(root.Definitions.Concat(inputObjects.Values).ToImmutableList()), inputObjects);
        }
    }
}
