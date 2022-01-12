using System.Collections.Generic;
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
            ValidateConfigHasDatabaseSchema();
            ValidateDatabaseSchema();

            ValidateConfigHasGraphQLTypes();
            ValidateGraphQLTypes();

            ValidateConfigHasMutationResolvers();
            ValidateMutationResolvers();

            ValidateQuerySchema();
        }

        /// <summary>
        /// Validate database schema
        /// </summary>
        private void ValidateDatabaseSchema()
        {
            ConfigStepInto("DatabaseSchema");

            ValidateDatabaseHasTables();
            ValidateDatabaseTables(GetDatabaseTables());

            ConfigStepOutOf("DatabaseSchema");

            SetDatabaseSchemaValidated(true);
        }

        /// <summary>
        /// Validate database tables
        /// </summary>
        private void ValidateDatabaseTables(Dictionary<string, TableDefinition> tables)
        {
            ConfigStepInto("Tables");

            // ValidateNoUnusedTables();

            foreach (KeyValuePair<string, TableDefinition> nameTableDefPair in tables)
            {
                string tableName = nameTableDefPair.Key;
                TableDefinition tableDefinition = nameTableDefPair.Value;

                ConfigStepInto(tableName);

                ValidateTableHasColumns(tableDefinition);
                // nothing else to validate for columns other than their existance

                ValidateTableHasPrimaryKey(tableDefinition);
                ValidateTablePrimaryKey(tableDefinition);

                if (TableHasForeignKey(tableDefinition))
                {
                    ValidateTableForeignKeys(tableDefinition);
                }

                ConfigStepOutOf(tableName);
            }

            ConfigStepOutOf("Tables");
        }

        /// <summary>
        /// Validate table foreign keys
        /// </summary>
        private void ValidateTableForeignKeys(TableDefinition table)
        {
            ConfigStepInto("ForeignKeys");

            foreach (KeyValuePair<string, ForeignKeyDefinition> nameForKeyDef in table.ForeignKeys)
            {
                string foreignKeyName = nameForKeyDef.Key;
                ForeignKeyDefinition foreignKey = nameForKeyDef.Value;

                ConfigStepInto(foreignKeyName);

                ValidateForeignKeyHasRefTable(foreignKey);
                ValidateForeignKeyReferencedTable(foreignKey);

                ValidateForeignKeyHasColumns(foreignKey);
                ValidateForeignKeyColumns(foreignKey, table);

                ConfigStepOutOf(foreignKeyName);
            }

            ConfigStepOutOf("ForeignKeys");
        }

        /// <summary>
        /// Validates the referenced table of the foreign key
        /// </summary>
        private void ValidateForeignKeyReferencedTable(ForeignKeyDefinition foreignKey)
        {
            ValidateForeignKeyRefTableExists(foreignKey.ReferencedTable);
            ValidateFKRefTabHasPk(foreignKey.ReferencedTable);
            ValidateRefTableHasColumns(foreignKey.ReferencedTable);
        }

        /// <summary>
        /// Validate foreign key columns
        /// </summary>
        private void ValidateForeignKeyColumns(ForeignKeyDefinition foreignKey, TableDefinition table)
        {
            ValidateFKColCountMatchesRefTablePKColCount(foreignKey);

            for (int columnIndex = 0; columnIndex < foreignKey.Columns.Count; columnIndex++)
            {
                string columnName = foreignKey.Columns[columnIndex];

                ValidateFKColumnHasMatchingTableColumn(columnName, table);
                ValidateFKColTypeMatchesRefTabPKColType(foreignKey, columnIndex, table);
            }
        }

        /// <summary>
        /// Validate GraphQL type fields
        /// </summary>
        private void ValidateGraphQLTypes()
        {
            ValidateDatabaseSchemaIsValidated();

            ConfigStepInto("GraphQLTypes");

            Dictionary<string, GraphqlType> types = GetGraphQLTypes();
            Dictionary<string, string> tableToType = new();

            ValidateTypesMatchSchemaTypes(types);

            foreach (KeyValuePair<string, GraphqlType> nameTypePair in types)
            {
                string typeName = nameTypePair.Key;
                GraphqlType type = nameTypePair.Value;

                ConfigStepInto(typeName);
                SchemaStepInto(typeName);

                if (IsPaginatedType(typeName))
                {
                    ValidatePaginationTypeSchema(typeName);
                }
                else
                {
                    ValidateGraphQLTypeHasTable(type);

                    ValidateGQLTypeTableIsUnique(type, tableToType);
                    tableToType.Add(type.Table, typeName);

                    ValidateGraphQLTypeTableMatchesSchema(typeName, type.Table);

                    ValidateGraphQLTypeHasFields(type);
                    ValidateGraphQLTypeFields(typeName, type);
                }

                ConfigStepOutOf(typeName);
                SchemaStepOutOf(typeName);
            }

            ConfigStepOutOf("GraphQLTypes");

            SetGraphQLTypesValidated(true);
        }

        ///<summary>
        /// Validate that pagination type has the right GraphQL schema
        ///</summary>
        private void ValidatePaginationTypeSchema(string typeName)
        {
            Dictionary<string, FieldDefinitionNode> fields = GetTypeFields(typeName);

            List<string> paginationTypeRequiredFields = new() { "items", "endCursor", "hasNextPage" };

            ValidatePaginationTypeHasRequiredFields(fields, paginationTypeRequiredFields);
            ValidatePaginationFieldsHaveNoArguments(fields, paginationTypeRequiredFields);

            ValidateItemsFieldType(fields["items"]);
            ValidateEndCursorFieldType(fields["endCursor"]);
            ValidateHasNextPageFieldType(fields["hasNextPage"]);

            ValidatePaginationTypeName(typeName);
        }

        /// <summary>
        /// Validate that the scalar fields of the type match the table associated with the type
        /// </summary>
        private void ValidateGraphQLTypeTableMatchesSchema(string typeName, string typeTable)
        {
            // TODO fix this, not accurate
            string[] tableColumnsPath = new[] { "DatabaseSchema", "Tables", typeTable, "Columns" };
            ValidateTableColumnsMatchScalarFields(typeTable, typeName, MakeConfigPosition(tableColumnsPath));
            ValidateTableColumnTypesMatchScalarFieldTypes(typeTable, typeName, MakeConfigPosition(tableColumnsPath));
        }

        /// <summary>
        /// Validate GraphQLType fields
        /// </summary>
        private void ValidateGraphQLTypeFields(string typeName, GraphqlType type)
        {
            ConfigStepInto("Fields");

            Dictionary<string, FieldDefinitionNode> fieldDefinitions = GetTypeFields(typeName);

            ValidateConfigFieldsMatchSchemaFields(type.Fields, fieldDefinitions);

            foreach (KeyValuePair<string, GraphqlField> nameFieldPair in type.Fields)
            {
                string fieldName = nameFieldPair.Key;
                GraphqlField field = nameFieldPair.Value;

                ConfigStepInto(fieldName);
                SchemaStepInto(fieldName);

                FieldDefinitionNode fieldDefinition = fieldDefinitions[fieldName];
                ITypeNode fieldType = fieldDefinition.Type;
                string returnedType = InnerType(fieldType);

                if (IsPaginatedType(returnedType))
                {
                    ValidateFieldTypeIsNotPaginationListType(fieldDefinition);
                    ValidatePaginationTypeFieldArguments(fieldDefinition);
                    returnedType = InnerType(GetTypeFields(returnedType)["items"].Type);
                }
                else if (IsListType(fieldType))
                {
                    ValidateListTypeFieldArguments(fieldDefinition);
                }
                else if (IsCustomType(fieldType))
                {
                    ValidateNonListCustomTypeField(fieldDefinition);
                }
                else
                {
                    ValidateScalarTypeFieldArguments(fieldDefinition);
                }

                switch (field.RelationshipType)
                {
                    case GraphqlRelationshipType.OneToOne:
                        ValidateOneToOneField(field, typeName, returnedType);
                        break;
                    case GraphqlRelationshipType.OneToMany:
                        ValidateOneToManyField(field, returnedType);
                        break;
                    case GraphqlRelationshipType.ManyToOne:
                        ValidateManyToOneField(field, typeName);
                        break;
                    case GraphqlRelationshipType.ManyToMany:
                        ValidateManyToManyField(field);
                        break;
                    case GraphqlRelationshipType.None:
                        // nothing to check for None types
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

            Dictionary<string, InputValueDefinitionNode> fieldArguments = GetArgumentFromField(field);

            ValidateFieldHasRequiredArguments(fieldArguments.Keys, requiredArguments.Keys);
            ValidateFieldArgumentTypes(fieldArguments, requiredArguments);
        }

        /// <summary>
        /// Validate that list type field has the expected arguments
        /// </summary>
        private void ValidateListTypeFieldArguments(FieldDefinitionNode field)
        {
            Dictionary<string, IEnumerable<string>> expectedArguments = new()
            {
                ["first"] = new[] { "Int", "Int!" },
            };

            Dictionary<string, InputValueDefinitionNode> fieldArguments = GetArgumentFromField(field);

            ValidateFieldHasNoUnexpectedArguments(expectedArguments.Keys, fieldArguments.Keys);
            ValidateFieldArgumentTypes(fieldArguments, expectedArguments);
        }

        /// <summary>
        /// Validate that non list custom type fields have the expected arguments
        /// </summary>
        private void ValidateNonListCustomTypeField(FieldDefinitionNode field)
        {
            Dictionary<string, IEnumerable<string>> expectedArguments = new() { };

            Dictionary<string, InputValueDefinitionNode> fieldArguments = GetArgumentFromField(field);

            ValidateFieldHasNoUnexpectedArguments(fieldArguments.Keys, expectedArguments.Keys);
            ValidateFieldArgumentTypes(fieldArguments, expectedArguments);
        }

        /// <summary>
        /// Validate that scalar type field has the expected arguments
        /// </summary>
        private void ValidateScalarTypeFieldArguments(FieldDefinitionNode field)
        {
            Dictionary<string, IEnumerable<string>> expectedArguments = new() { };

            Dictionary<string, InputValueDefinitionNode> fieldArguments = GetArgumentFromField(field);

            ValidateFieldHasNoUnexpectedArguments(fieldArguments.Keys, expectedArguments.Keys);
            ValidateFieldArgumentTypes(fieldArguments, expectedArguments);
        }

        /// <summary>
        /// Validate field with One-To-One relationship to the type that owns it
        /// </summary>
        private void ValidateOneToOneField(GraphqlField field, string type, string returnedType)
        {
            ValidateNoAssociationTable(field);
            ValidateLeftXOrRightForeignKey(field);

            if (HasLeftForeignKey(field))
            {
                ValidateLeftForeignKey(field, type);
            }

            if (HasRightForeignKey(field))
            {
                ValidateRightForeignKey(field, returnedType);
            }
        }

        /// <summary>
        /// Validate field with One-To-Many relationship to the type that owns it
        /// </summary>
        private void ValidateOneToManyField(GraphqlField field, string returnedType)
        {
            ValidateNoAssociationTable(field);
            ValidateHasOnlyRightForeignKey(field);
            ValidateRightForeignKey(field, returnedType);
        }

        /// <summary>
        /// Validate field with Many-To-One relationship to the type that owns it
        /// </summary>
        private void ValidateManyToOneField(GraphqlField field, string type)
        {
            ValidateNoAssociationTable(field);
            ValidateHasOnlyLeftForeignKey(field);
            ValidateLeftForeignKey(field, type);
        }

        /// <summary>
        /// Validate field with Many-To-Many relationship to the type that owns it
        /// </summary>
        private void ValidateManyToManyField(GraphqlField field)
        {
            ValidateHasAssociationTable(field);
            ValidateAssociativeTableExists(field);
            ValidateHasBothLeftAndRightFK(field);
            ValidateLeftAndRightFkForM2MField(field);
        }

        /// <summary>
        /// Validate mutation resolvers
        /// </summary>
        private void ValidateMutationResolvers()
        {
            ValidateDatabaseSchemaIsValidated();
            ValidateGraphQLTypesIsValidated();

            ConfigStepInto("MutationResolvers");
            SchemaStepInto("Mutation");

            IEnumerable<string> mutationResolverIds = GetMutationResolverIds();

            ValidateNoMissingIds(mutationResolverIds);
            ValidateNoDuplicateIds(mutationResolverIds);
            ValidateMutationResolversMatchSchema(mutationResolverIds);

            foreach (MutationResolver resolver in GetMutationResolvers())
            {
                ConfigStepInto($"Id = {resolver.Id}");
                SchemaStepInto(resolver.Id);

                ValidateMutResolverHasTable(resolver);
                ValidateMutResolverTableExists(resolver.Table);

                // the rest of the mutation operations are only valid for cosmos
                List<MutationOperation> supportedOperations = new()
                {
                    MutationOperation.Insert,
                    MutationOperation.Update
                };

                ValidateMutResolverOperation(resolver.OperationType, supportedOperations);

                switch (resolver.OperationType)
                {
                    case MutationOperation.Insert:
                        ValidateInsertMutationSchema(resolver);
                        break;
                    case MutationOperation.Update:
                        ValidateUpdateMutationSchema(resolver);
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
            Dictionary<string, InputValueDefinitionNode> mutArgs = GetArgumentFromField(mutation);
            TableDefinition table = GetTableWithName(resolver.Table);

            ValidateMutHasNotListType(mutation);
            ValidateMutReturnTypeMatchesTable(resolver.Table, mutation);

            ValidateMutArgsMatchTableColumns(resolver.Table, table, mutArgs);
            ValidateMutArgTypesMatchTableColTypes(resolver.Table, table, mutArgs);

            ValidateArgNullabilityInInsertMut(mutArgs);
            ValidateNoMissingArgsInInsertMut(table, mutArgs);
        }

        /// <summary>
        /// Validate the schema of an update mutation
        /// </summary>
        private void ValidateUpdateMutationSchema(MutationResolver resolver)
        {
            FieldDefinitionNode mutation = GetMutation(resolver.Id);
            Dictionary<string, InputValueDefinitionNode> mutArgs = GetArgumentFromField(mutation);
            TableDefinition table = GetTableWithName(resolver.Table);

            ValidateMutHasNotListType(mutation);
            ValidateMutReturnTypeMatchesTable(resolver.Table, mutation);

            ValidateMutArgsMatchTableColumns(resolver.Table, table, mutArgs);
            ValidateMutArgTypesMatchTableColTypes(resolver.Table, table, mutArgs);

            ValidateArgNullabilityInUpdateMut(table, mutArgs);
            ValidateNoMissingArgsInUpdateMut(resolver.Table, table, mutArgs);
        }

        /// <summary>
        /// Validate query schema
        /// </summary>
        private void ValidateQuerySchema()
        {
            ValidateDatabaseSchemaIsValidated();
            ValidateGraphQLTypesIsValidated();

            SchemaStepInto("Query");

            Dictionary<string, FieldDefinitionNode> queries = GetQueries();

            ValidateNoScalarTypeQueries(queries);

            foreach (KeyValuePair<string, FieldDefinitionNode> nameQueryPair in queries)
            {
                string queryName = nameQueryPair.Key;
                FieldDefinitionNode queryField = nameQueryPair.Value;

                SchemaStepInto(queryName);

                if (IsPaginatedType(InnerType(queryField.Type)))
                {
                    ValidateFieldTypeIsNotPaginationListType(queryField);
                    ValidatePaginationTypeFieldArguments(queryField);
                }
                else if (IsListType(queryField.Type))
                {
                    ValidateListTypeFieldArguments(queryField);
                }
                else if (IsCustomType(queryField.Type))
                {
                    ValidateNonListCustomQueryFieldArgs(queryField);
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
        private void ValidateNonListCustomQueryFieldArgs(FieldDefinitionNode queryField)
        {
            Dictionary<string, IEnumerable<string>> requiredArgs = new();
            Dictionary<string, InputValueDefinitionNode> arguments = GetArgumentFromField(queryField);

            string returnTypeTableName = GetTypeTable(InnerType(queryField.Type));
            TableDefinition returnTypeTable = GetTableWithName(returnTypeTableName);

            foreach (string pkCol in returnTypeTable.PrimaryKey)
            {
                string gqlType = GetGraphQLTypeForColumnType(returnTypeTable.Columns[pkCol].Type);
                requiredArgs.Add(pkCol, new[] { gqlType, $"{gqlType}!" });
            }

            ValidateFieldHasRequiredArguments(arguments.Keys, requiredArgs.Keys);
            ValidateFieldArgumentTypes(arguments, requiredArgs);
        }
    }
}
