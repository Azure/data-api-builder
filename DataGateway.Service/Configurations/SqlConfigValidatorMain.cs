using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Models;
using HotChocolate.Language;

namespace Azure.DataGateway.Service.Configurations
{

    /// This portion of the class
    /// contains the high level validation workflow.
    /// It doesn't access any of the private members
    /// or throw any exceptions.

    /// <summary>
    /// Validates the sql config and the graphql schema and
    /// and if the two match each other
    /// </summary>
    public partial class SqlConfigValidator : IConfigValidator
    {
        /// <inheritdoc />
        public void ValidateConfig()
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

            ValidateConfigHasGraphQLTypes();
            ValidateGraphQLTypes();

            if (SchemaHasMutations())
            {
                ValidateConfigHasMutationResolvers();
                ValidateMutationResolvers();
            }
            else
            {
                ValidateNoMutationResolvers();
            }

            ValidateQuerySchema();

            timer.Stop();
            Console.WriteLine($"Done validating GQL schema in {timer.ElapsedMilliseconds}ms.");
        }

        /// <summary>
        /// Validate GraphQL type fields
        /// </summary>
        private void ValidateGraphQLTypes()
        {
            ConfigStepInto("GraphQLTypes");

            Dictionary<string, GraphQLType> types = GetGraphQLTypes();
            Dictionary<string, string> tableToType = new();

            ValidateTypesMatchSchemaTypes(types);

            // Field validation relies on valid pagination types so
            // this must be validated first
            ValidatePaginationTypes(types);

            foreach (KeyValuePair<string, GraphQLType> nameTypePair in types)
            {
                string typeName = nameTypePair.Key;
                GraphQLType type = nameTypePair.Value;

                ConfigStepInto(typeName);
                SchemaStepInto(typeName);

                if (!IsPaginationTypeName(typeName))
                {
                    ValidateGraphQLTypeHasTable(type);

                    ValidateGQLTypeTableIsUnique(type, tableToType);
                    tableToType.Add(type.Table, typeName);

                    ValidateGraphQLTypeTableColumnsMatchSchema(typeName, type.Table);

                    Dictionary<string, FieldDefinitionNode> fieldDefinitions = GetTypeFields(typeName);
                    ValidateSchemaFieldsReturnTypes(fieldDefinitions);

                    if (!TypeHasFields(type))
                    {
                        ValidateNoFieldsWithInnerCustomType(typeName, fieldDefinitions);
                    }
                    else
                    {
                        ValidateGraphQLTypeFields(typeName, type);
                    }
                }

                ConfigStepOutOf(typeName);
                SchemaStepOutOf(typeName);
            }

            ConfigStepOutOf("GraphQLTypes");

            SetGraphQLTypesValidated(true);
        }

        /// <summary>
        /// Validate pagination types
        /// </summary>
        private void ValidatePaginationTypes(Dictionary<string, GraphQLType> types)
        {
            foreach (string typeName in types.Keys)
            {
                ConfigStepInto(typeName);
                SchemaStepInto(typeName);

                if (IsPaginationTypeName(typeName))
                {
                    ValidatePaginationTypeSchema(typeName);
                }

                ConfigStepOutOf(typeName);
                SchemaStepOutOf(typeName);
            }
        }

        ///<summary>
        /// Validate that pagination type has the right GraphQL schema
        ///</summary>
        private void ValidatePaginationTypeSchema(string typeName)
        {
            Dictionary<string, FieldDefinitionNode> fields = GetTypeFields(typeName);

            List<string> paginationTypeRequiredFields = new()
            {
                GraphQLBuilder.Queries.QueryBuilder.PAGINATION_FIELD_NAME,
                GraphQLBuilder.Queries.QueryBuilder.PAGINATION_TOKEN_FIELD_NAME,
                GraphQLBuilder.Queries.QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME
            };

            ValidatePaginationTypeHasRequiredFields(fields, paginationTypeRequiredFields);
            ValidatePaginationFieldsHaveNoArguments(fields, paginationTypeRequiredFields);

            ValidateItemsFieldType(fields[GraphQLBuilder.Queries.QueryBuilder.PAGINATION_FIELD_NAME]);
            ValidateAfterFieldType(fields[GraphQLBuilder.Queries.QueryBuilder.PAGINATION_TOKEN_FIELD_NAME]);
            ValidateHasNextPageFieldType(fields[GraphQLBuilder.Queries.QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME]);

            ValidatePaginationTypeName(typeName);
        }

        /// <summary>
        /// Validate that the scalar fields of the type match the table columns associated with the type
        /// </summary>
        /// <remarks>
        /// Ignore scalar fields which match config type fields
        /// </remarks>
        private void ValidateGraphQLTypeTableColumnsMatchSchema(
            string typeName,
            string typeTable)
        {
            string[] tableColumnsPath = new[] { "DatabaseSchema", "Tables", typeTable, "Columns" };
            ValidateTableColumnsMatchScalarFields(typeTable, typeName, MakeConfigPosition(tableColumnsPath));
            ValidateTableColumnTypesMatchScalarFieldTypes(typeTable, typeName, MakeConfigPosition(tableColumnsPath));
            ValidateScalarFieldsMatchingTableColumnsHaveNoArgs(typeName, typeTable, MakeConfigPosition(tableColumnsPath));
            ValidateScalarFieldsMatchingTableColumnsNullability(typeName, typeTable, MakeConfigPosition(tableColumnsPath));
        }

        /// <summary>
        /// Validate GraphQLType fields
        /// </summary>
        private void ValidateGraphQLTypeFields(string typeName, GraphQLType type)
        {
            ConfigStepInto("Fields");

            Dictionary<string, FieldDefinitionNode> fieldDefinitions = GetTypeFields(typeName);

            ValidateConfigFieldsMatchSchemaFields(type.Fields, fieldDefinitions);

            foreach (KeyValuePair<string, GraphQLField> nameFieldPair in type.Fields)
            {
                string fieldName = nameFieldPair.Key;
                GraphQLField field = nameFieldPair.Value;

                ConfigStepInto(fieldName);
                SchemaStepInto(fieldName);

                FieldDefinitionNode fieldDefinition = fieldDefinitions[fieldName];
                ITypeNode fieldType = fieldDefinition.Type;
                string returnedType = InnerTypeStr(fieldType);

                List<GraphQLRelationshipType> validRelationshipTypes = new()
                {
                    GraphQLRelationshipType.OneToOne,
                    GraphQLRelationshipType.ManyToMany,
                    GraphQLRelationshipType.OneToMany,
                    GraphQLRelationshipType.ManyToOne
                };

                ValidateRelationshipType(field, validRelationshipTypes);

                switch (field.RelationshipType)
                {
                    case GraphQLRelationshipType.OneToOne:
                        ValidateOneToOneField(field, fieldDefinition, typeName, returnedType);
                        break;
                    case GraphQLRelationshipType.OneToMany:
                        ValidateOneToManyField(field, fieldDefinition, typeName, returnedType);
                        break;
                    case GraphQLRelationshipType.ManyToOne:
                        ValidateManyToOneField(field, fieldDefinition, typeName, returnedType);
                        break;
                    case GraphQLRelationshipType.ManyToMany:
                        ValidateManyToManyField(field, fieldDefinition, typeName, returnedType);
                        break;
                }

                ConfigStepOutOf(fieldName);
                SchemaStepOutOf(fieldName);
            }

            ConfigStepOutOf("Fields");
        }

        /// <summary>
        /// Validate that pagination type field has the required arguments
        /// </summary>
        private void ValidatePaginationTypeFieldArguments(FieldDefinitionNode field)
        {
            Dictionary<string, IEnumerable<string>> requiredArguments = new()
            {
                ["first"] = new[] { "Int", "Int!" },
                ["after"] = new[] { "String" }
            };

            string returnedPaginationType = InnerTypeStr(field.Type);
            string itemsType = InnerTypeStr(GetTypeFields(returnedPaginationType)["items"].Type);
            Dictionary<string, IEnumerable<string>> optionalArguments = new()
            {
                ["_filter"] = new[] { $"{itemsType}FilterInput", $"{itemsType}FilterInput!" },
                ["orderBy"] = new[] { $"{itemsType}OrderByInput", $"{itemsType}OrderByInput!" },
                ["_filterOData"] = new[] { "String", "String!" }
            };

            Dictionary<string, InputValueDefinitionNode> fieldArguments = GetArgumentsFromField(field);

            ValidateFieldArguments(
                fieldArguments.Keys,
                requiredArguments: requiredArguments.Keys,
                optionalArguments: optionalArguments.Keys);
            ValidateFieldArgumentTypes(
                fieldArguments,
                MergeDictionaries<string, IEnumerable<string>>(requiredArguments, optionalArguments));
        }

        /// <summary>
        /// Validate that list type field has the expected arguments
        /// </summary>
        private void ValidateListTypeFieldArguments(FieldDefinitionNode field)
        {
            string returnedType = InnerTypeStr(field.Type);
            Dictionary<string, IEnumerable<string>> optionalArguments = new()
            {
                ["first"] = new[] { "Int", "Int!" },
                ["_filter"] = new[] { $"{returnedType}FilterInput", $"{returnedType}FilterInput!" },
                ["orderBy"] = new[] { $"{returnedType}OrderByInput", $"{returnedType}OrderByInput!" },
                ["_filterOData"] = new[] { "String", "String!" }
            };

            Dictionary<string, InputValueDefinitionNode> fieldArguments = GetArgumentsFromField(field);

            ValidateFieldArguments(fieldArguments.Keys, optionalArguments: optionalArguments.Keys);
            ValidateFieldArgumentTypes(fieldArguments, optionalArguments);
        }

        /// <summary>
        /// Validate that field doesn't have any arguments.
        /// </summary>
        private void ValidateNoFieldArguments(FieldDefinitionNode field)
        {
            Dictionary<string, InputValueDefinitionNode> fieldArguments = GetArgumentsFromField(field);
            ValidateFieldArguments(fieldArguments.Keys, requiredArguments: Enumerable.Empty<string>());
        }

        /// <summary>
        /// Validate field with One-To-One relationship to the type that owns it
        /// </summary>
        /// <param name="type">The type which owns the field</param>
        /// <param name="returnedType">The type returned by the field</param>
        private void ValidateOneToOneField(GraphQLField field, FieldDefinitionNode fieldDefinition, string type, string returnedType)
        {
            bool hasLeftFk = HasLeftForeignKey(field);
            bool hasRightFk = HasRightForeignKey(field);

            ValidateReturnTypeNotPagination(field, fieldDefinition);
            ValidateFieldReturnsCustomType(fieldDefinition, typeNullable: !hasLeftFk);
            ValidateNoFieldArguments(fieldDefinition);

            ValidateNoAssociationTable(field);
            ValidateHasLeftOrRightForeignKey(field);

            if (hasLeftFk)
            {
                ValidateLeftForeignKey(field, type);
                ForeignKeyDefinition leftFk = GetFkFromTable(GetTypeTable(type), field.LeftForeignKey);
                ValidateLeftFkRefTableIsReturnedTypeTable(leftFk, returnedType);
            }

            if (hasRightFk)
            {
                ValidateRightForeignKey(field, returnedType);
                ForeignKeyDefinition rightFk = GetFkFromTable(GetTypeTable(returnedType), field.RightForeignKey);
                ValidateRightFkRefTableIsTypeTable(rightFk, type);
            }
        }

        /// <summary>
        /// Validate field with One-To-Many relationship to the type that owns it
        /// </summary>
        /// <param name="type">The type which owns the field</param>
        /// <param name="returnedType">The type returned by the field</param>
        private void ValidateOneToManyField(GraphQLField field, FieldDefinitionNode fieldDefinition, string type, string returnedType)
        {
            if (IsPaginationType(fieldDefinition.Type))
            {
                ValidateReturnTypeNullability(fieldDefinition, returnsNullable: false);
                ValidatePaginationTypeFieldArguments(fieldDefinition);
                returnedType = InnerTypeStr(GetTypeFields(returnedType)["items"].Type);
            }
            else
            {
                ValidateFieldReturnsListOfCustomType(fieldDefinition, listNullabe: false, listElemsNullable: false);
                ValidateListTypeFieldArguments(fieldDefinition);
            }

            ValidateNoAssociationTable(field);
            ValidateHasOnlyRightForeignKey(field);
            ValidateRightForeignKey(field, returnedType);

            ForeignKeyDefinition rightFk = GetFkFromTable(GetTypeTable(returnedType), field.RightForeignKey);
            ValidateRightFkRefTableIsTypeTable(rightFk, type);
        }

        /// <summary>
        /// Validate field with Many-To-One relationship to the type that owns it
        /// </summary>
        /// <param name="type">The type which owns the field</param>
        /// <param name="returnedType">The type returned by the field</param>
        private void ValidateManyToOneField(GraphQLField field, FieldDefinitionNode fieldDefinition, string type, string returnedType)
        {
            ValidateReturnTypeNotPagination(field, fieldDefinition);
            ValidateFieldReturnsCustomType(fieldDefinition, typeNullable: false);
            ValidateNoFieldArguments(fieldDefinition);

            ValidateNoAssociationTable(field);
            ValidateHasOnlyLeftForeignKey(field);
            ValidateLeftForeignKey(field, type);

            ForeignKeyDefinition leftFk = GetFkFromTable(GetTypeTable(type), field.LeftForeignKey);
            ValidateLeftFkRefTableIsReturnedTypeTable(leftFk, returnedType);
        }

        /// <summary>
        /// Validate field with Many-To-Many relationship to the type that owns it
        /// </summary>
        /// <param name="type">The type which owns the field</param>
        /// <param name="returnedType">The type returned by the field</param>
        private void ValidateManyToManyField(GraphQLField field, FieldDefinitionNode fieldDefinition, string type, string returnedType)
        {
            if (IsPaginationType(fieldDefinition.Type))
            {
                ValidateReturnTypeNullability(fieldDefinition, returnsNullable: false);
                ValidatePaginationTypeFieldArguments(fieldDefinition);
                returnedType = InnerTypeStr(GetTypeFields(returnedType)["items"].Type);
            }
            else
            {
                ValidateFieldReturnsListOfCustomType(fieldDefinition, listNullabe: false, listElemsNullable: false);
                ValidateListTypeFieldArguments(fieldDefinition);
            }

            ValidateHasAssociationTable(field);
            ValidateHasBothLeftAndRightFK(field);
            ValidateLeftAndRightFkForM2MField(field);

            ForeignKeyDefinition rightFk = GetFkFromTable(field.AssociativeTable, field.RightForeignKey);
            ValidateRightFkRefTableIsTypeTable(rightFk, returnedType);
            ForeignKeyDefinition leftFk = GetFkFromTable(field.AssociativeTable, field.LeftForeignKey);
            ValidateLeftFkRefTableIsReturnedTypeTable(leftFk, type);
        }

        /// <summary>
        /// Validate mutation resolvers
        /// </summary>
        private void ValidateMutationResolvers()
        {
            ValidateGraphQLTypesIsValidated();

            ConfigStepInto("MutationResolvers");
            SchemaStepInto("Mutation");

            IEnumerable<string> mutationResolverIds = GetMutationResolverIds();

            ValidateNoMissingIds(mutationResolverIds);
            ValidateNoDuplicateMutIds(mutationResolverIds);
            ValidateMutationResolversMatchSchema(mutationResolverIds);

            foreach (MutationResolver resolver in GetMutationResolvers())
            {
                ConfigStepInto($"Id = {resolver.Id}");
                SchemaStepInto(resolver.Id);

                ValidateMutResolverHasTable(resolver);

                // the rest of the mutation operations are only valid for cosmos
                List<Operation> supportedOperations = new()
                {
                    Operation.Insert,
                    Operation.UpdateIncremental,
                    Operation.Delete
                };

                ValidateMutResolverOperation(resolver.OperationType, supportedOperations);

                switch (resolver.OperationType)
                {
                    case Operation.Insert:
                        ValidateInsertMutationSchema(resolver);
                        break;
                    case Operation.Update:
                        ValidateUpdateMutationSchema(resolver);
                        break;
                    case Operation.Delete:
                        ValidateDeleteMutationSchema(resolver);
                        break;
                }

                ConfigStepOutOf($"Id = {resolver.Id}");
                SchemaStepOutOf(resolver.Id);
            }

            ConfigStepOutOf("MutationResolvers");
            SchemaStepOutOf("Mutation");
        }

        /// <summary>
        /// Validate the schema of an insert mutation
        /// </summary>
        private void ValidateInsertMutationSchema(MutationResolver resolver)
        {
            FieldDefinitionNode mutation = GetMutation(resolver.Id);
            Dictionary<string, InputValueDefinitionNode> mutArgs = GetArgumentsFromField(mutation);
            TableDefinition table = GetTableWithName(resolver.Table);

            ValidateMutReturnTypeIsNotListType(mutation);
            if (IsCustomType(mutation.Type))
            {
                ValidateMutReturnTypeMatchesTable(resolver.Table, mutation);
            }

            ValidateMutArgsMatchTableColumns(resolver.Table, table, mutArgs);
            ValidateMutArgTypesMatchTableColTypes(resolver.Table, table, mutArgs);

            ValidateInsertMutHasCorrectArgs(table, mutArgs);
            ValidateArgNullabilityInInsertMut(table, mutArgs);
            ValidateReturnTypeNullability(mutation, returnsNullable: true);
        }

        /// <summary>
        /// Validate the schema of an update mutation
        /// </summary>
        private void ValidateUpdateMutationSchema(MutationResolver resolver)
        {
            FieldDefinitionNode mutation = GetMutation(resolver.Id);
            Dictionary<string, InputValueDefinitionNode> mutArgs = GetArgumentsFromField(mutation);
            TableDefinition table = GetTableWithName(resolver.Table);

            ValidateMutReturnTypeIsNotListType(mutation);
            if (IsCustomType(mutation.Type))
            {
                ValidateMutReturnTypeMatchesTable(resolver.Table, mutation);
            }

            ValidateMutArgsMatchTableColumns(resolver.Table, table, mutArgs);
            ValidateMutArgTypesMatchTableColTypes(resolver.Table, table, mutArgs);

            ValidateUpdateMutHasCorrectArgs(table, mutArgs);
            ValidateArgNullabilityInUpdateMut(table, mutArgs);
            ValidateReturnTypeNullability(mutation, returnsNullable: true);
        }

        /// <summary>
        /// Validate the schema of a delete mutation
        /// </summary>
        private void ValidateDeleteMutationSchema(MutationResolver resolver)
        {
            FieldDefinitionNode mutation = GetMutation(resolver.Id);
            Dictionary<string, InputValueDefinitionNode> mutArgs = GetArgumentsFromField(mutation);
            TableDefinition table = GetTableWithName(resolver.Table);

            ValidateMutReturnTypeIsNotListType(mutation);
            if (IsCustomType(mutation.Type))
            {
                ValidateMutReturnTypeMatchesTable(resolver.Table, mutation);
            }

            ValidateFieldArguments(mutArgs.Keys, requiredArguments: table.PrimaryKey);
            ValidateMutArgTypesMatchTableColTypes(resolver.Table, table, mutArgs);
            ValidateFieldArgumentsAreNonNullable(mutArgs);
            ValidateReturnTypeNullability(mutation, returnsNullable: true);
        }

        /// <summary>
        /// Validate query schema
        /// </summary>
        private void ValidateQuerySchema()
        {
            ValidateGraphQLTypesIsValidated();

            SchemaStepInto("Query");

            Dictionary<string, FieldDefinitionNode> queries = GetQueries();

            ValidateSchemaFieldsReturnTypes(queries);
            ValidateNoScalarInnerTypeQueries(queries);

            foreach (KeyValuePair<string, FieldDefinitionNode> nameQueryPair in queries)
            {
                string queryName = nameQueryPair.Key;
                FieldDefinitionNode queryField = nameQueryPair.Value;

                SchemaStepInto(queryName);

                if (IsPaginationType(queryField.Type))
                {
                    ValidateReturnTypeNullability(queryField, returnsNullable: false);
                    ValidatePaginationTypeFieldArguments(queryField);
                }
                else if (IsListType(queryField.Type))
                {
                    ValidateFieldReturnsListOfCustomType(queryField, listNullabe: false, listElemsNullable: false);
                    ValidateListTypeFieldArguments(queryField);
                }
                else if (IsCustomType(queryField.Type))
                {
                    ValidateReturnTypeNullability(queryField, returnsNullable: true);
                    ValidateNonListCustomTypeQueryFieldArgs(queryField);
                }

                SchemaStepOutOf(queryName);
            }

            SchemaStepOutOf("Query");
        }

        /// <summary>
        /// Validate non list custom query field arguments
        /// </summary>
        /// <remarks>
        /// This is a search by primary key query so the arguments should match
        /// the return type table primary key
        /// </remarks>
        private void ValidateNonListCustomTypeQueryFieldArgs(FieldDefinitionNode queryField)
        {
            Dictionary<string, InputValueDefinitionNode> arguments = GetArgumentsFromField(queryField);

            string returnedTypeTableName = GetTypeTable(InnerTypeStr(queryField.Type));
            TableDefinition returnedTypeTable = GetTableWithName(returnedTypeTableName);

            ValidateFieldArguments(arguments.Keys, requiredArguments: returnedTypeTable.PrimaryKey);
            ValidateFieldArgumentsAreNonNullable(arguments);
        }
    }
}
