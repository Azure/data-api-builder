using System;
using System.Collections.Generic;
using System.Linq;
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

            ValidateConfigHasDatabaseSchema();
            ValidateDatabaseSchema();

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
            Console.WriteLine($"Done validating config and GQL schema in {timer.ElapsedMilliseconds}ms.");
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

            ValidateTablesStrucutre(tables);
            ValidateTablesLogic(tables);

            ConfigStepOutOf("Tables");
        }

        /// <summary>
        /// Validate table strucutre
        /// Validate that the table and its subcomponents have the required information
        /// in the config
        /// </summary>
        private void ValidateTablesStrucutre(Dictionary<string, TableDefinition> tables)
        {
            foreach (KeyValuePair<string, TableDefinition> nameTableDefPair in tables)
            {
                string tableName = nameTableDefPair.Key;
                TableDefinition tableDefinition = nameTableDefPair.Value;

                ConfigStepInto(tableName);

                ValidateTableHasColumns(tableDefinition);

                ConfigStepInto("Columns");
                ValidateTableColumnsHaveType(tableDefinition);
                ConfigStepOutOf("Columns");

                ValidateTableHasPrimaryKey(tableDefinition);

                ConfigStepOutOf(tableName);
            }
        }

        /// <summary>
        /// Validate table logic
        /// Validate the information of primary key, foreign keys, and columns
        /// doesn't contradict each other
        /// </summary>
        private void ValidateTablesLogic(Dictionary<string, TableDefinition> tables)
        {
            foreach (KeyValuePair<string, TableDefinition> nameTableDefPair in tables)
            {
                string tableName = nameTableDefPair.Key;
                TableDefinition tableDefinition = nameTableDefPair.Value;

                ConfigStepInto(tableName);

                ValidateNoDuplicatePkColumns(tableDefinition);
                ValidatePkColsMatchTableCols(tableDefinition);

                ValidateTableColumnsLogic(tableDefinition);

                if (TableHasForeignKey(tableDefinition))
                {
                    ValidateTableForeignKeys(tableDefinition);
                }

                ConfigStepOutOf(tableName);
            }
        }

        /// <summary>
        /// Validate the logic related to table columns in the config
        /// </summary>
        private void ValidateTableColumnsLogic(TableDefinition table)
        {
            ConfigStepInto("Columns");

            foreach (KeyValuePair<string, ColumnDefinition> nameColumnPair in table.Columns)
            {
                string columnName = nameColumnPair.Key;
                ColumnDefinition column = nameColumnPair.Value;

                ConfigStepInto(columnName);
                ValidateNoAutoGeneratedAndHasDefault(column);
                ConfigStepOutOf(columnName);
            }

            ConfigStepOutOf("Columns");
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
                ValidateForeignKeyRefTableExists(foreignKey);

                ValidateForeignKeyHasColumns(foreignKey);
                ValidateForeignKeyColumns(foreignKey, table);

                ConfigStepOutOf(foreignKeyName);
            }

            ConfigStepOutOf("ForeignKeys");
        }

        /// <summary>
        /// Validate foreign key columns
        /// </summary>
        private void ValidateForeignKeyColumns(ForeignKeyDefinition foreignKey, TableDefinition table)
        {
            ValidateNoDuplicateFkColumns(foreignKey);
            ValidateFKColCountMatchesRefTablePKColCount(foreignKey);
            ValidateFKColumnsHaveMatchingTableColumns(foreignKey, table);

            for (int columnIndex = 0; columnIndex < foreignKey.Columns.Count; columnIndex++)
            {
                _ = foreignKey.Columns[columnIndex];
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

            // Field validation relies on valid pagination types so
            // this must be validated first
            ValidatePaginationTypes(types);

            foreach (KeyValuePair<string, GraphqlType> nameTypePair in types)
            {
                string typeName = nameTypePair.Key;
                GraphqlType type = nameTypePair.Value;

                ConfigStepInto(typeName);
                SchemaStepInto(typeName);

                if (!IsPaginationTypeName(typeName))
                {
                    ValidateGraphQLTypeHasTable(type);

                    ValidateGQLTypeTableIsUnique(type, tableToType);
                    tableToType.Add(type.Table, typeName);

                    ValidateGraphQLTypeTableMatchesSchema(typeName, type.Table);
                    Dictionary<string, FieldDefinitionNode> fieldDefinitions = GetTypeFields(typeName);
                    ValidateSchemaFieldsReturnTypes(fieldDefinitions);
                    bool hasNonScalarFields = HasAnyCustomFieldInGraphQLType(fieldDefinitions);

                    if (hasNonScalarFields)
                    {
                        ValidateGraphQLTypeHasFields(type);
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
        private void ValidatePaginationTypes(Dictionary<string, GraphqlType> types)
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
        private void ValidateGraphQLTypeTableMatchesSchema(
            string typeName,
            string typeTable)
        {
            string[] tableColumnsPath = new[] { "DatabaseSchema", "Tables", typeTable, "Columns" };
            ValidateTableColumnsMatchScalarFields(typeTable, typeName, MakeConfigPosition(tableColumnsPath));
            ValidateTableColumnTypesMatchScalarFieldTypes(typeTable, typeName, MakeConfigPosition(tableColumnsPath));
            ValidateScalarFieldNullability(typeName);
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
                string returnedType = InnerTypeStr(fieldType);

                List<GraphqlRelationshipType> validRelationshipTypes = new()
                {
                    GraphqlRelationshipType.ManyToMany,
                    GraphqlRelationshipType.OneToMany,
                    GraphqlRelationshipType.ManyToOne
                };

                ValidateRelationshipType(field, validRelationshipTypes);

                switch (field.RelationshipType)
                {
                    case GraphqlRelationshipType.OneToMany:
                        ValidateOneToManyField(field, fieldDefinition, typeName, returnedType);
                        break;
                    case GraphqlRelationshipType.ManyToOne:
                        ValidateManyToOneField(field, fieldDefinition, typeName, returnedType);
                        break;
                    case GraphqlRelationshipType.ManyToMany:
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

            ValidateFieldHasNoUnexpectedArguments(fieldArguments.Keys, expectedArguments.Keys);
            ValidateFieldArgumentTypes(fieldArguments, expectedArguments);
        }

        /// <summary>
        /// Validate that field doesn't have any arguments.
        /// </summary>
        private void ValidateNoFieldArguments(FieldDefinitionNode field)
        {
            Dictionary<string, InputValueDefinitionNode> fieldArguments = GetArgumentFromField(field);
            ValidateFieldHasNoUnexpectedArguments(fieldArguments.Keys, Enumerable.Empty<string>());
        }

        /// <summary>
        /// Validate field with One-To-Many relationship to the type that owns it
        /// </summary>
        private void ValidateOneToManyField(GraphqlField field, FieldDefinitionNode fieldDefinition, string type, string returnedType)
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
        private void ValidateManyToOneField(GraphqlField field, FieldDefinitionNode fieldDefinition, string type, string returnedType)
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
        private void ValidateManyToManyField(GraphqlField field, FieldDefinitionNode fieldDefinition, string type, string returnedType)
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
            ValidateAssociativeTableExists(field);
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
            ValidateDatabaseSchemaIsValidated();
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
                ValidateMutResolverTableExists(resolver.Table);

                // the rest of the mutation operations are only valid for cosmos
                List<Operation> supportedOperations = new()
                {
                    Operation.Insert,
                    Operation.Update,
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
            Dictionary<string, InputValueDefinitionNode> mutArgs = GetArgumentFromField(mutation);
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
            Dictionary<string, InputValueDefinitionNode> mutArgs = GetArgumentFromField(mutation);
            TableDefinition table = GetTableWithName(resolver.Table);

            ValidateMutReturnTypeIsNotListType(mutation);
            if (IsCustomType(mutation.Type))
            {
                ValidateMutReturnTypeMatchesTable(resolver.Table, mutation);
            }

            ValidateMutArgsMatchTableColumns(resolver.Table, table, mutArgs);
            ValidateMutArgTypesMatchTableColTypes(resolver.Table, table, mutArgs);
            ValidateArgNullabilityInUpdateMut(table, mutArgs);
            ValidateReturnTypeNullability(mutation, returnsNullable: true);
        }

        /// <summary>
        /// Validate the schema of a delete mutation
        /// </summary>
        private void ValidateDeleteMutationSchema(MutationResolver resolver)
        {
            FieldDefinitionNode mutation = GetMutation(resolver.Id);
            Dictionary<string, InputValueDefinitionNode> mutArgs = GetArgumentFromField(mutation);
            TableDefinition table = GetTableWithName(resolver.Table);

            ValidateMutReturnTypeIsNotListType(mutation);
            if (IsCustomType(mutation.Type))
            {
                ValidateMutReturnTypeMatchesTable(resolver.Table, mutation);
            }

            ValidateFieldHasRequiredArguments(mutArgs.Keys, table.PrimaryKey);
            ValidateMutArgTypesMatchTableColTypes(resolver.Table, table, mutArgs);
            ValidateFieldArgumentsAreNonNullable(mutArgs);
            ValidateReturnTypeNullability(mutation, returnsNullable: true);
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
            Dictionary<string, InputValueDefinitionNode> arguments = GetArgumentFromField(queryField);

            string returnedTypeTableName = GetTypeTable(InnerTypeStr(queryField.Type));
            TableDefinition returnedTypeTable = GetTableWithName(returnedTypeTableName);

            ValidateFieldHasRequiredArguments(arguments.Keys, returnedTypeTable.PrimaryKey);
            ValidateFieldArgumentsAreNonNullable(arguments);
        }
    }
}
