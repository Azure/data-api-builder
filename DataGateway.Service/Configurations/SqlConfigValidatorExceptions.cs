using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Configurations
{
    /// This portion of the class
    /// holds the members of the SqlConfigValidator and the functions
    /// which run its validation logic.
    /// All config/schema related exceptions are thrown here
    /// Each function checks for only one thing and throws only one exception.
    public partial class SqlConfigValidator : IConfigValidator
    {
        private ResolverConfig _config;
        private ISchema? _schema;
        private Stack<string> _configValidationStack;
        private Stack<string> _schemaValidationStack;
        private Dictionary<string, FieldDefinitionNode> _queries;
        private Dictionary<string, FieldDefinitionNode> _mutations;
        private Dictionary<string, ObjectTypeDefinitionNode> _types;
        private bool _dbSchemaIsValidated;
        private bool _graphQLTypesAreValidated;

        /// <summary>
        /// Sets the config and schema for the validator
        /// </summary>
        public SqlConfigValidator(IGraphQLMetadataProvider metadataStoreProvider, GraphQLService graphQLService)
        {
            _configValidationStack = MakeConfigPosition(Enumerable.Empty<string>());
            _schemaValidationStack = MakeSchemaPosition(Enumerable.Empty<string>());
            _types = new();
            _mutations = new();
            _queries = new();
            _dbSchemaIsValidated = false;
            _graphQLTypesAreValidated = false;

            _config = metadataStoreProvider.GetResolvedConfig();
            _schema = graphQLService.Schema;

            if (_schema != null)
            {
                foreach (IDefinitionNode node in _schema.ToDocument().Definitions)
                {
                    if (node is ObjectTypeDefinitionNode objectTypeDef)
                    {
                        if (objectTypeDef.Name.ToString() == "Mutation")
                        {
                            _mutations = GetObjTypeDefFields(objectTypeDef);
                        }
                        else if (objectTypeDef.Name.Value == "Query")
                        {
                            _queries = GetObjTypeDefFields(objectTypeDef);
                        }
                        else
                        {
                            _types.Add(objectTypeDef.Name.ToString(), objectTypeDef);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validate that config has a DatabaseSchema element
        /// </summary>
        private void ValidateConfigHasDatabaseSchema()
        {
            if (_config.DatabaseSchema == null)
            {
                throw new ConfigValidationException(
                    $"Config must have a \"DatabaseSchema\" element.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that config has a GraphQLTypes element
        /// </summary>
        private void ValidateConfigHasGraphQLTypes()
        {
            if (_config.GraphQLTypes == null || _config.GraphQLTypes.Count == 0)
            {
                throw new ConfigValidationException(
                    $"Config must have a non empty \"GraphQLTypes\" element.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that config has a MutationResolvers element
        /// if the GraphQL schema has a mutations
        /// </summary>
        private void ValidateConfigHasMutationResolvers()
        {
            if (_config.MutationResolvers == null || _config.MutationResolvers.Count == 0)
            {
                throw new ConfigValidationException(
                    $"Config must have a non empty \"MutationResolvers\" element to resolve " +
                    "GraphQL mutations.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the config has "MutationResolvers" element
        /// Called when there are no mutations in the schema
        /// </summary>
        private void ValidateNoMutationResolvers()
        {
            if (_config.MutationResolvers != null)
            {
                throw new ConfigValidationException(
                    "Config doesn't need a \"MutationResolvers\" element. No mutations in the schema.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate database has tables
        /// </summary>
        private void ValidateDatabaseHasTables()
        {
            if (_config.DatabaseSchema!.Tables == null || _config.DatabaseSchema!.Tables.Count == 0)
            {
                throw new ConfigValidationException(
                    "Database schema must have a non empty \"Tables\" element.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate table has columns
        /// </summary>
        private void ValidateTableHasColumns(TableDefinition table)
        {
            if (table.Columns == null || table.Columns.Count == 0)
            {
                throw new ConfigValidationException(
                    "Table must have a non \"Columns\" element.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate table has primary key
        /// </summary>
        private void ValidateTableHasPrimaryKey(TableDefinition table)
        {
            if (table.PrimaryKey == null || table.PrimaryKey.Count == 0)
            {
                throw new ConfigValidationException(
                    "Table must have a non empty \"PrimaryKey\" element.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate that all primary key columns are unique
        /// </summary>
        private void ValidateNoDuplicatePkColumns(TableDefinition table)
        {
            IEnumerable<string> duplicatePkCols = GetDuplicates(table.PrimaryKey);

            if (duplicatePkCols.Any())
            {
                throw new ConfigValidationException(
                    "All primary key columns must be unique. Found duplicate columns " +
                    $"[{string.Join(", ", duplicatePkCols)}].",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the primary key columns match columns of the table
        /// </summary>
        private void ValidatePkColsMatchTableCols(TableDefinition table)
        {
            IEnumerable<string> unmatchedPks = table.PrimaryKey.Except(table.Columns.Keys);

            if (unmatchedPks.Any())
            {
                throw new ConfigValidationException(
                    $"Primary Key columns [{string.Join(", ", unmatchedPks)}] do not have equivalent columns " +
                    "in the table.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that primary columns do not have "hasDefault" column property set to true
        /// Primary Key columns can only be autogenerated ("IsAutoGenerated": true)
        /// </summart>
        private void ValidateNoPkColsWithDefaultValue(TableDefinition table)
        {
            IEnumerable<string> pkColsWithDefaultValue =
                table.PrimaryKey.Where(pkCol => table.Columns[pkCol].HasDefault);

            if (pkColsWithDefaultValue.Any())
            {
                throw new ConfigValidationException(
                    $"Primary Key columns [{string.Join(", ", pkColsWithDefaultValue)}] must not " +
                    "have \"hasDefault\" column property set to true.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that both IsAutoGenerated and HasDefault are not set to true for a colum
        /// since IsAutoGenerated implies HasDefault so no need to increase verbosity in the
        /// config by specifying both each time a column in IsAutoGenerated
        /// </summary>
        private void ValidateNoAutoGeneratedAndHasDefault(ColumnDefinition column)
        {
            if (column.IsAutoGenerated == true && column.HasDefault == true)
            {
                throw new ConfigValidationException(
                    "No need to specify both \"IsAutoGenerated\" and \"HasDefault\". Auto generated implies has default.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate foreign key has referenced table
        /// </summary>
        public void ValidateForeignKeyHasRefTable(ForeignKeyDefinition foreignKey)
        {
            if (string.IsNullOrEmpty(foreignKey.ReferencedTable))
            {
                throw new ConfigValidationException(
                    "Foreign key must have a non empty string \"ReferencedTable\" element.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate foreign key referenced table
        /// </summary>
        private void ValidateForeignKeyRefTableExists(ForeignKeyDefinition foreignKey)
        {
            if (!ExistsTableWithName(foreignKey.ReferencedTable))
            {
                throw new ConfigValidationException(
                    $"Referenced table \"{foreignKey.ReferencedTable}\" does not exit in the database schema.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate that the foreign key columns have unique names amongst themselves
        /// </summary>
        private void ValidateNoDuplicateFkColumns(List<string> columns, bool refColumns)
        {
            IEnumerable<string> duplicateCols = GetDuplicates(columns);

            if (duplicateCols.Any())
            {
                throw new ConfigValidationException(
                    $"Foreign key {(refColumns ? "referenced" : string.Empty)} columns must be unique amongst " +
                    $"themselves. Duplicate columns [{string.Join(", ", duplicateCols)}] found.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that the count of foreign key columns matches the number of  referenced columns of the
        /// referenced table
        /// </summary>
        private void ValidateColCountMatchesRefColCount(List<string> columns, List<string> refColumns, string refTableName)
        {
            if (columns.Count != refColumns.Count)
            {
                throw new ConfigValidationException(
                    $"Mismatch between foreign key column count and referenced table \"{refTableName}\"" +
                    "'s referenced columns count.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that the referenced columns of the foreign key exist in the referenced table
        /// </summary>
        private void ValidateRefColumnsExistInRefTable(List<string> referencedColumns, string referencedTable)
        {
            _ = GetTableWithName(referencedTable).Columns.Keys.ToList();
            IEnumerable<string> unmatchedColumns = referencedColumns.Except(referencedColumns);

            if (unmatchedColumns.Any())
            {
                throw new ConfigValidationException(
                    $"Referenced columns [{string.Join(", ", unmatchedColumns)}] do not exist in " +
                    $"referenced table \"{referencedTable}\".",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that the foreign key columns have matching columns in the table the foreign key
        /// belongs to
        /// </summary>
        private void ValidateFKColumnsHaveMatchingTableColumns(ForeignKeyDefinition foreignKey, TableDefinition table)
        {
            IEnumerable<string> unmatchedFkCols = foreignKey.Columns.Except(table.Columns.Keys);

            if (unmatchedFkCols.Any())
            {
                throw new ConfigValidationException(
                    $"Table does not contain columns for foreign key columns [{string.Join(", ", unmatchedFkCols)}].",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the type of the foreign key columns match their equivalent referenced
        /// columns in the referenced table
        /// </summary>
        private void ValidateFKColTypesMatchRefTabPKColTypes(
            List<string> columns,
            TableDefinition table,
            List<string> refColumns,
            string refTableName,
            TableDefinition refTable
        )
        {
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                string columnName = columns[columnIndex];
                Type columnType = table.Columns[columnName].SystemType;
                string refColumnName = refColumns[columnIndex];
                ColumnDefinition refColumn = refTable.Columns[refColumnName];

                if (!ReferenceEquals(columnType, refColumn.SystemType))
                {
                    throw new ConfigValidationException(
                        $"Type mismatch between foreign key column \"{columnName}\" with type \"{columnType}\" and " +
                        $"referenced column \"{refTableName}\".\"{refColumnName}\" " +
                        $"with type \"{refColumn.SystemType}\". Look into Models.ColumnDefinition.TypesAreEqual " +
                        "to learn about how type equality is determined.",
                        _configValidationStack);
                }
            }
        }

        /// <summary>
        /// Validate that database schema has already been validated
        /// </summary>
        private void ValidateDatabaseSchemaIsValidated()
        {
            if (!IsDatabaseSchemaValidated())
            {
                throw new NotSupportedException(
                    "Current validation functions requires that the database schema is validated first.");
            }
        }

        /// <summary>
        /// Validate that the GraphQLType in the config match the types in the schema
        /// </summary>
        private void ValidateTypesMatchSchemaTypes(Dictionary<string, GraphQLType> types)
        {
            IEnumerable<string> unmatchedConfigTypes = types.Keys.Except(_types.Keys);
            IEnumerable<string> unmatchedSchemaTypes = _types.Keys.Except(types.Keys);

            if (unmatchedConfigTypes.Any() || unmatchedSchemaTypes.Any())
            {
                string unmatchedConfigTypesMessage =
                    unmatchedConfigTypes.Any() ?
                    $"Types [{string.Join(", ", unmatchedConfigTypes)}] are not matched in the schema. " :
                    string.Empty;

                string unmatchedSchemaTypesMessage =
                    unmatchedSchemaTypes.Any() ?
                    $"Schema types [{string.Join(", ", unmatchedSchemaTypes)}] are not matched in the config." :
                    string.Empty;

                throw new ConfigValidationException(
                    $"Mismatch between types in the config and in the schema. " +
                    unmatchedConfigTypesMessage +
                    unmatchedSchemaTypesMessage,
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that config fields are matched to a schema field and that
        /// there is no non scalar schema field not matched to a config field
        /// </summary>
        private void ValidateConfigFieldsMatchSchemaFields(
            Dictionary<string, GraphQLField> configFields,
            Dictionary<string, FieldDefinitionNode> schemaFields)
        {
            IEnumerable<string> unmatchedConfigFields = configFields.Keys.Except(schemaFields.Keys);

            // note that scalar fields can be matched to table columns so they don't
            // need to match a config field
            Dictionary<string, FieldDefinitionNode> nonScalarFields = GetNonScalarFields(schemaFields);
            IEnumerable<string> unmatchedNonScalarSchemaFields = nonScalarFields.Keys.Except(configFields.Keys);

            if (unmatchedConfigFields.Any() || unmatchedNonScalarSchemaFields.Any())
            {
                string unmatchedConFieldsMessage =
                    unmatchedConfigFields.Any() ?
                    $"[{string.Join(", ", unmatchedConfigFields)}] fields don't match any field in the schema. "
                    : string.Empty;
                string unmatchedSchFieldsMessage =
                    unmatchedNonScalarSchemaFields.Any() ?
                    $"[{string.Join(", ", unmatchedNonScalarSchemaFields)}] schema fields are not matched by any config fields."
                    : string.Empty;

                throw new ConfigValidationException(
                    "Mismatch between fields and the schema fields in " +
                    PrettyPrintValidationStack(_schemaValidationStack) + ". " +
                    unmatchedConFieldsMessage +
                    unmatchedSchFieldsMessage,
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the fields of a schema type no have invalid return types
        /// </summary>
        /// <remarks>
        /// Nested list types and lists of *Connection types are considered invalid
        /// </remarks>
        private void ValidateSchemaFieldsReturnTypes(Dictionary<string, FieldDefinitionNode> fieldDefinitions)
        {
            List<string> nestedListFields = new();
            List<string> listOfPgTypeFields = new();

            foreach (KeyValuePair<string, FieldDefinitionNode> nameFieldPair in fieldDefinitions)
            {
                string fieldName = nameFieldPair.Key;
                FieldDefinitionNode field = nameFieldPair.Value;

                if (IsNestedListType(field.Type))
                {
                    nestedListFields.Add(fieldName);
                }
                else if (IsListOfPaginationType(field.Type))
                {
                    listOfPgTypeFields.Add(fieldName);
                }
            }

            if (nestedListFields.Any() || listOfPgTypeFields.Any())
            {
                string nestedListMessage =
                    nestedListFields.Any() ?
                    $"Fields [{string.Join(", ", nestedListFields)}] must not have a nested " +
                    "list as a return type. "
                    : string.Empty;

                string listOfPgTypeMessage =
                    listOfPgTypeFields.Any() ?
                    $"Fields [{string.Join(", ", listOfPgTypeFields)}] must have a list of " +
                    "*Connection types as a return type."
                    : string.Empty;

                throw new ConfigValidationException(
                    "Found fields with invalid return types. " +
                    nestedListMessage +
                    listOfPgTypeMessage,
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate pagination type has required fields
        /// </summary>
        private void ValidatePaginationTypeHasRequiredFields(
            Dictionary<string, FieldDefinitionNode> typeFields,
            List<string> requiredFields)
        {
            IEnumerable<string> missingRequiredFields = requiredFields.Except(typeFields.Keys);
            IEnumerable<string> extraFields = typeFields.Keys.Except(requiredFields);
            if (missingRequiredFields.Any() || extraFields.Any())
            {
                throw new ConfigValidationException(
                    $"Pagination type must have only [{string.Join(", ", requiredFields)}] fields.",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the pagination required fields have no arguments
        /// </summary>
        private void ValidatePaginationFieldsHaveNoArguments(
            Dictionary<string, FieldDefinitionNode> typeFields,
            List<string> paginationFieldNames)
        {
            List<string> fieldsWithArguments = new();
            foreach (string fieldName in paginationFieldNames)
            {
                if (GetArgumentsFromField(typeFields[fieldName]).Count > 0)
                {
                    fieldsWithArguments.Add(fieldName);
                }
            }

            if (fieldsWithArguments.Any())
            {
                throw new ConfigValidationException(
                    $"[{string.Join(", ", fieldsWithArguments)}] field of a pagination type must not have arguments.",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate the type of "items" field in a Pagination type
        /// </summary>
        private void ValidateItemsFieldType(FieldDefinitionNode itemsField)
        {
            ITypeNode itemsType = itemsField.Type;
            if (!IsListType(itemsType) ||
                !IsInnerTypeCustom(itemsType) ||
                !itemsType.IsNonNullType() ||
                AreListElementsNullable(itemsType) ||
                IsPaginationType(InnerType(itemsType)))
            {
                throw new ConfigValidationException(
                    "\"items\" must return a non nullable list type of non nullable custom type " +
                    "\"[CustomType!]!\" where CustomType is not a pagination type.",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate the type of "endCursor" field in a Pagination type
        /// </summary>
        private void ValidateEndCursorFieldType(FieldDefinitionNode endCursorField)
        {
            ITypeNode endCursorFieldType = endCursorField.Type;
            if (IsListType(endCursorFieldType) ||
                InnerTypeStr(endCursorFieldType) != "String" ||
                endCursorFieldType.IsNonNullType())
            {
                throw new ConfigValidationException(
                    "\"endCursor\" must return a nullable \"String\" type.",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate the type of "hasNextPage" field in a Pagination type
        /// </summary>
        private void ValidateHasNextPageFieldType(FieldDefinitionNode hasNextPageField)
        {
            ITypeNode hasNextPageFieldType = hasNextPageField.Type;
            if (IsListType(hasNextPageFieldType) ||
                InnerTypeStr(hasNextPageFieldType) != "Boolean" ||
                !hasNextPageFieldType.IsNonNullType())
            {
                throw new ConfigValidationException(
                    "\"hasNextPage\" must return a non nullable \"Boolean!\" type.",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate pagination type has correct name
        /// </summary>
        private void ValidatePaginationTypeName(string paginationTypeName)
        {
            FieldDefinitionNode itemsField = GetTypeFields(paginationTypeName)["items"];
            string paginationUnderlyingType = InnerTypeStr(itemsField.Type);
            string expectedTypeName = $"{paginationUnderlyingType}Connection";
            if (paginationTypeName != expectedTypeName)
            {
                throw new ConfigValidationException(
                    $"Pagination type on \"{paginationUnderlyingType}\" must be called \"{expectedTypeName}\".",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate graphQLType has table
        /// </summary>
        private void ValidateGraphQLTypeHasTable(GraphQLType type)
        {
            if (string.IsNullOrEmpty(type.Table))
            {
                throw new ConfigValidationException(
                    "This type must contain a non empty string \"Table\" element.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate that type does not share an underlying table with any other type
        /// </summary>
        private void ValidateGQLTypeTableIsUnique(GraphQLType type, Dictionary<string, string> tableToType)
        {
            if (tableToType.ContainsKey(type.Table))
            {
                throw new ConfigValidationException(
                    $"SystemType shares underlying table \"{type.Table}\" with other type " +
                    $"\"{tableToType[type.Table]}\". All underlying type tables must be unique.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate the scalar fields and table columns match one to one
        /// </summary>
        /// <remarks>
        /// Each table column and scalar field should serve a purpouse
        /// So each table column should either:
        /// <list type="bullet">
        /// <item> match in name and type to a field </item>
        /// <item> be part of the primary or foreign key </item>
        /// </list>
        /// Each scalar field should either:
        /// <list type="bullet">
        /// <item> match a table column in name and type </item>
        /// <item> match a GraphQLType.Field </item>
        /// </list>
        /// </remarks>
        private void ValidateTableColumnsMatchScalarFields(string tableName, string typeName, Stack<string> tableColumnPosition)
        {
            TableDefinition table = GetTableWithName(tableName);
            Dictionary<string, ColumnDefinition> tableColumns = table.Columns;
            Dictionary<string, FieldDefinitionNode> scalarFields = GetScalarFields(GetTypeFields(typeName));

            IEnumerable<string> unmatchedTableColumns = tableColumns.Keys
                                                            .Except(scalarFields.Keys)
                                                            .Except(GetPkAndFkColumns(table));

            IEnumerable<string> unmatchedScalarFields = scalarFields.Keys
                                                            .Except(tableColumns.Keys)
                                                            .Except(GetConfigFieldsForGqlType(_types[typeName]));

            if (unmatchedTableColumns.Any() || unmatchedScalarFields.Any())
            {
                string unmatchedFieldsMessage =
                    unmatchedScalarFields.Any() ?
                    $"Fields [{string.Join(", ", unmatchedScalarFields)}] are neither matched to columns nor " +
                    $"match to type fields in the config. " :
                    string.Empty;

                string unmatchedColumnsMessage =
                    unmatchedTableColumns.Any() ?
                    $"Columns [{string.Join(", ", unmatchedTableColumns)}] are neither matched to fields nor " +
                    $"serve as primary key or foreign key columns in table \"{tableName}\"." :
                    string.Empty;

                throw new ConfigValidationException(
                    "Mismatch between scalar fields and table columns in " +
                    $"{PrettyPrintValidationStack(tableColumnPosition)}. " +
                    unmatchedColumnsMessage +
                    unmatchedFieldsMessage,
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate the scalar fields and table columns that match in name match in type
        /// </summary>
        private void ValidateTableColumnTypesMatchScalarFieldTypes(string tableName, string typeName, Stack<string> tableColumnPosition)
        {
            TableDefinition table = GetTableWithName(tableName);
            Dictionary<string, ColumnDefinition> tableColumns = table.Columns;
            Dictionary<string, FieldDefinitionNode> typeFields = GetTypeFields(typeName);

            IEnumerable<string> matchedColumnAndFieldNames = tableColumns.Keys.Intersect(typeFields.Keys);

            List<string> mismatchedFieldColumnTypeMessages = new();

            foreach (string matchedName in matchedColumnAndFieldNames)
            {
                Type columnType = tableColumns[matchedName].SystemType;
                ITypeNode fieldType = typeFields[matchedName].Type;

                if (!GraphQLTypeEqualsColumnType(fieldType, columnType))
                {
                    mismatchedFieldColumnTypeMessages.Add(
                        $"Column \"{matchedName}\" with type \"{columnType}\" doesn't match field " +
                        $"\"{matchedName}\" with type \"{fieldType.ToString()}\".");
                }
            }

            if (mismatchedFieldColumnTypeMessages.Any())
            {
                throw new ConfigValidationException(
                    "There are mismatched types between some type fields and some columns of the types's underlying table in " +
                    $"{PrettyPrintValidationStack(tableColumnPosition)}. {string.Join(" ", mismatchedFieldColumnTypeMessages)}",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the scalar fields that match table columns do
        /// not have arguments
        /// </summary>
        private void ValidateScalarFieldsMatchingTableColumnsHaveNoArgs(
            string typeName,
            string typeTable,
            Stack<string> tableColumnsPosition
        )
        {
            Dictionary<string, FieldDefinitionNode> scalarFields = GetScalarFields(GetTypeFields(typeName));
            IEnumerable<String> fieldsWithArgs =
                scalarFields.Keys.Where(fieldName => GetArgumentsFromField(scalarFields[fieldName]).Count > 0)
                                    .Intersect(GetTableWithName(typeTable).Columns.Keys);

            if (fieldsWithArgs.Any())
            {
                throw new ConfigValidationException(
                    $"Fields [{string.Join(", ", fieldsWithArgs)}] which match with table columns " +
                    $"in {PrettyPrintValidationStack(tableColumnsPosition)} should not have any arguments.",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate the nullability of scalar type fields which match table columns
        /// </summary>
        /// <remarks>
        /// Currently it validates that all scalar type fields are non nullable.
        /// This will have to change when nullable database types are supported
        /// </remarks>
        private void ValidateScalarFieldsMatchingTableColumnsNullability(
            string typeName,
            string typeTable,
            Stack<string> tableColumnsPosition)
        {
            Dictionary<string, FieldDefinitionNode> scalarFields = GetScalarFields(GetTypeFields(typeName));
            IEnumerable<string> nullableScalarFields =
                scalarFields.Keys.Where(fieldName => !scalarFields[fieldName].Type.IsNonNullType())
                                    .Intersect(GetTableWithName(typeTable).Columns.Keys);

            if (nullableScalarFields.Any())
            {
                throw new ConfigValidationException(
                    $"Fields [{string.Join(", ", nullableScalarFields)}] which match with table columns " +
                    $"in {PrettyPrintValidationStack(tableColumnsPosition)} should return a non nullable type.",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate that type has no fields which return a custom type
        /// </summary>
        /// <remarks>
        /// Called if config type has no fields
        /// </remarks>
        private void ValidateNoFieldsWithInnerCustomType(string typeName, Dictionary<string, FieldDefinitionNode> fields)
        {
            IEnumerable<String> fieldsWithCustomTypes = fields.Keys.Where(fieldName => IsInnerTypeCustom(fields[fieldName].Type));

            if (fieldsWithCustomTypes.Any())
            {
                throw new ConfigValidationException(
                    $"SystemType \"{typeName}\" has no fields to resolve schema fields which return custom types [" +
                    string.Join(", ", fieldsWithCustomTypes) + "].",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate if argument names match required arguments
        /// </summary>
        private void ValidateFieldArguments(
            IEnumerable<string> fieldArgumentNames,
            IEnumerable<string>? requiredArguments = null,
            IEnumerable<string>? optionalArguments = null)
        {
            IEnumerable<string> empty = Enumerable.Empty<string>();
            IEnumerable<string> missingArguments = requiredArguments?.Except(fieldArgumentNames) ?? empty;
            IEnumerable<string> extraArguments = fieldArgumentNames.Except(requiredArguments ?? empty)
                                                                    .Except(optionalArguments ?? empty);

            if (missingArguments.Any() || extraArguments.Any())
            {
                string missingArgsMessage =
                    missingArguments.Any() ?
                    $"Missing [{string.Join(", ", missingArguments)}] arguments. "
                    : string.Empty;
                string extraArgsMessage =
                    extraArguments.Any() ?
                    $"Arguments [{string.Join(", ", extraArguments)}] are not appropriate for this field."
                    : string.Empty;

                throw new ConfigValidationException(
                    $"Field has invalid arguments." +
                    missingArgsMessage +
                    extraArgsMessage,
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the argument type of the fields matches what what is expected
        /// </summary>
        private void ValidateFieldArgumentTypes(
            Dictionary<string, InputValueDefinitionNode> fieldArguments,
            Dictionary<string, IEnumerable<string>> expectedArguments)
        {
            List<string> mismatchMessages = new();
            foreach (KeyValuePair<string, InputValueDefinitionNode> nameArgumentPair in fieldArguments)
            {
                string argName = nameArgumentPair.Key;
                InputValueDefinitionNode argument = nameArgumentPair.Value;

                if (!expectedArguments[argName].Contains(argument.Type.ToString()))
                {
                    mismatchMessages.Add(
                        $"Argument \"{argName}\" has unexpected type \"{argument.Type.ToString()}\". " +
                        $"It's type can only be one of [{string.Join(", ", expectedArguments[argName])}].");
                }
            }

            if (mismatchMessages.Any())
            {
                throw new ConfigValidationException(
                    "Unexpected arguments types found. " + string.Join(" ", mismatchMessages),
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the field has a valid relationship type
        /// </summary>
        private void ValidateRelationshipType(GraphQLField field, List<GraphQLRelationshipType> validRelationshipTypes)
        {
            if (!validRelationshipTypes.Contains(field.RelationshipType))
            {
                throw new ConfigValidationException(
                    $"{field.RelationshipType} is not a valid/supported relationship type.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate the nullability of the return type of the field
        /// </summary>
        private void ValidateReturnTypeNullability(FieldDefinitionNode field, bool returnsNullable)
        {
            if (field.Type.IsNonNullType() == returnsNullable)
            {
                string label = returnsNullable ? "nullable" : "non nullable";
                throw new ConfigValidationException(
                    $"The type returned from this field must be {label}.",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that field does return a pagination type
        /// </summary>
        private void ValidateReturnTypeNotPagination(GraphQLField field, FieldDefinitionNode fieldDefinition)
        {
            if (IsPaginationType(fieldDefinition.Type))
            {
                throw new ConfigValidationException(
                    $"{field.RelationshipType} field must not return a pagination " +
                    $"type \"{fieldDefinition.Type.ToString()}\".",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the field returns a list of custom type
        /// </summary>
        private void ValidateFieldReturnsListOfCustomType(
            FieldDefinitionNode fieldDefinition,
            bool listNullabe = true,
            bool listElemsNullable = true)
        {
            ITypeNode type = fieldDefinition.Type;
            if (!IsListOfCustomType(type) ||
                listNullabe == type.IsNonNullType() ||
                listElemsNullable != AreListElementsNullable(type))
            {
                string listLabel = listNullabe ? "nullable" : "non nullable";
                string elemLabel = listElemsNullable ? "nullable" : "non nullable";

                throw new ConfigValidationException(
                    $"Field must return a {listLabel} list of {elemLabel} custom type.",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate that the field returns a custom type
        /// </summary>
        private void ValidateFieldReturnsCustomType(FieldDefinitionNode fieldDefinition, bool typeNullable = true)
        {
            ITypeNode type = fieldDefinition.Type;
            if (!IsCustomType(type) || typeNullable == type.IsNonNullType())
            {
                string typeLabel = typeNullable ? "nullable" : "non nullable";
                throw new ConfigValidationException(
                    $"Field must return a {typeLabel} custom type.",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Make sure the field has no association table
        /// </summary>
        private void ValidateNoAssociationTable(GraphQLField field)
        {
            if (!string.IsNullOrEmpty(field.AssociativeTable))
            {
                throw new ConfigValidationException(
                    $"Cannot have Associative Table in {field.RelationshipType} field.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Make sure the field has an association table
        /// </summary>
        private void ValidateHasAssociationTable(GraphQLField field)
        {
            if (string.IsNullOrEmpty(field.AssociativeTable))
            {
                throw new ConfigValidationException(
                    $"Must have a non empty string Associative Table in {field.RelationshipType} field.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that field has only left foreign key
        /// </summary>
        private void ValidateHasOnlyLeftForeignKey(GraphQLField field)
        {
            if (!HasLeftForeignKey(field) || HasRightForeignKey(field))
            {
                throw new ConfigValidationException(
                    $"{field.RelationshipType} field must have only left foreign key.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that field has only right foreign key
        /// </summary>
        private void ValidateHasOnlyRightForeignKey(GraphQLField field)
        {
            if (HasLeftForeignKey(field) || !HasRightForeignKey(field))
            {
                throw new ConfigValidationException(
                    $"{field.RelationshipType} field must have only right foreign key.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that field has left foreign key or right foreign key
        /// </summary>
        private void ValidateHasLeftOrRightForeignKey(GraphQLField field)
        {
            if (!(HasLeftForeignKey(field) || HasRightForeignKey(field)))
            {
                throw new ConfigValidationException(
                    $"{field.RelationshipType} field must have a left foreign key or right foreign key.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that the field has both left and right foreign keys
        /// </summary>
        private void ValidateHasBothLeftAndRightFK(GraphQLField field)
        {
            if (!HasLeftForeignKey(field) || !HasRightForeignKey(field))
            {
                throw new ConfigValidationException(
                    $"{field.RelationshipType} field must have both left and right foreign keys.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the left foreign key of the field is a foreign key of the
        /// table of the type that this field belongs to
        /// </summary>
        private void ValidateLeftForeignKey(GraphQLField field, string type)
        {
            string typeTable = GetTypeTable(type);
            if (!TableContainsForeignKey(typeTable, field.LeftForeignKey))
            {
                throw new ConfigValidationException(
                    $"Left foreign key in {field.RelationshipType} field, must be a foreign key " +
                    $"of the table \"{typeTable}\", which is the underlying table of the type \"{type}\" " +
                    "that contains this field.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the right foreign key of the field is a foreign key of the
        /// table of the type that this field returns
        /// </summary>
        private void ValidateRightForeignKey(GraphQLField field, string returnedType)
        {
            string returnedTypeTable = GetTypeTable(returnedType);
            if (!TableContainsForeignKey(returnedTypeTable, field.RightForeignKey))
            {
                throw new ConfigValidationException(
                    $"Right foreign key in {field.RelationshipType} field, must be a foreign key " +
                    $"of the table \"{returnedTypeTable}\", which is the underlying table of the type " +
                    $"\"{returnedType}\" that this field returns.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the reference table of the right foreign key refers to type table
        /// </summary>
        private void ValidateRightFkRefTableIsTypeTable(ForeignKeyDefinition rightFk, string type)
        {
            string typeTable = GetTypeTable(type);
            if (rightFk.ReferencedTable != typeTable)
            {
                throw new ConfigValidationException(
                    $"Right foreign key's referenced table \"{rightFk.ReferencedTable}\" does not " +
                    $"refer to the type table \"{typeTable}\" of type \"{typeTable}\".",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the reference table of the left foreign key refers to the returned type's table
        /// </summary>
        private void ValidateLeftFkRefTableIsReturnedTypeTable(ForeignKeyDefinition rightFk, string returnedType)
        {
            string returnedTypeTable = GetTypeTable(returnedType);
            if (rightFk.ReferencedTable != returnedTypeTable)
            {
                throw new ConfigValidationException(
                    $"Left foreign key's referenced table \"{rightFk.ReferencedTable}\" does not refer " +
                    $"to the type table \"{returnedTypeTable}\" of the returned type \"{returnedTypeTable}\".",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the field's associative table exists
        /// </summary>
        private void ValidateAssociativeTableExists(GraphQLField field)
        {
            if (!ExistsTableWithName(field.AssociativeTable))
            {
                throw new ConfigValidationException(
                    $"Associative table \"{field.AssociativeTable}\" does not exits in the " +
                    "config database schema.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate the left and right foreign keys for many to many field
        /// </summary>
        private void ValidateLeftAndRightFkForM2MField(GraphQLField field)
        {
            if (!TableContainsForeignKey(field.AssociativeTable, field.LeftForeignKey) ||
                !TableContainsForeignKey(field.AssociativeTable, field.RightForeignKey))
            {
                throw new ConfigValidationException(
                    $"Both the left and right foreign key in {field.RelationshipType} field " +
                    $"must be foreign keys of the field's associative table \"{field.AssociativeTable}\".",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that Config.GraphQLTypes has already been validated
        /// </summary>
        private void ValidateGraphQLTypesIsValidated()
        {
            if (!IsGraphQLTypesValidated())
            {
                throw new NotSupportedException(
                    "Current validation functions requires that the Config > GraphQLTypes is validated first.");
            }
        }

        /// <summary>
        /// Validate that none of the mutation resolver ids are missing
        /// </summary>
        private void ValidateNoMissingIds(IEnumerable<string> ids)
        {
            int missingIdsCount = ids.Count(id => string.IsNullOrEmpty(id));

            if (missingIdsCount > 0)
            {
                throw new ConfigValidationException(
                    $"{missingIdsCount} mutation ids missing. All mutation resolvers must " +
                    "have a non empty string \"Id\" element.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that all mutation resolver ids are unique
        /// </summary>
        private void ValidateNoDuplicateMutIds(IEnumerable<string> ids)
        {
            IEnumerable<string> duplicateIds = GetDuplicates(ids);

            if (duplicateIds.Any())
            {
                throw new ConfigValidationException(
                    "All mutation resolver ids must be unique." +
                    $"[{string.Join(", ", duplicateIds)}] ids appear multiple times.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that mutation resolvers and mutations in the schema are matched one-to-one
        /// </summary>
        private void ValidateMutationResolversMatchSchema(IEnumerable<string> ids)
        {
            IEnumerable<string> unmatchedMutations = _mutations.Keys.Except(ids);
            IEnumerable<string> extraIds = ids.Except(_mutations.Keys);

            if (unmatchedMutations.Any() || extraIds.Any())
            {
                string unmatchedMutationsMessage =
                    unmatchedMutations.Any() ?
                    $"[{string.Join(", ", unmatchedMutations)}] mutations in the GraphQL schema " +
                    "do not have equivalent resolvers. "
                    : string.Empty;
                string extraIdsMessage =
                    extraIds.Any() ?
                    $"Resolvers with ids [{string.Join(", ", extraIds)}] do not resolver any mutation."
                    : string.Empty;

                throw new ConfigValidationException(
                    $"Mismatch between mutation resolvers and GraphQL mutations. " +
                    unmatchedMutationsMessage +
                    extraIdsMessage,
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate mutaiton resolver has a "Table" element
        /// </summary>
        private void ValidateMutResolverHasTable(MutationResolver resolver)
        {
            if (string.IsNullOrEmpty(resolver.Table))
            {
                throw new ConfigValidationException(
                    "Mutation resolver must have a non empty string \"Table\" element.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate that the mutation resolver table exists in the database schema
        /// </summary>
        private void ValidateMutResolverTableExists(string tableName)
        {
            if (!ExistsTableWithName(tableName))
            {
                throw new ConfigValidationException(
                    $"Mutation resolver table \"{tableName}\" does not exist in " +
                    "the database schema.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Check if the mutation resolver operation is a valid/supported for sql (pg and mssql)
        /// </summary>
        private void ValidateMutResolverOperation(Operation op, List<Operation> supportedOperations)
        {
            if (!supportedOperations.Contains(op))
            {
                throw new ConfigValidationException(
                    $"Mutation resolver operation type \"{op}\" is not valid for Sql. " +
                    $"Only supported operations are [{string.Join(", ", supportedOperations)}].",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the mutation does not return a list type
        /// </summary>
        private void ValidateMutReturnTypeIsNotListType(FieldDefinitionNode mutation)
        {
            if (IsListType(mutation.Type))
            {
                throw new ConfigValidationException(
                    "Mutation must not return a list type.",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate the return type of the mutation matches the mutation resolver table
        /// </summary>
        private void ValidateMutReturnTypeMatchesTable(string resolverTable, FieldDefinitionNode mutation)
        {
            if (resolverTable != GetTypeTable(InnerTypeStr(mutation.Type)))
            {
                throw new ConfigValidationException(
                    $"Mutation return type {mutation.Type.ToString()} does not match the type " +
                    $"associated with this mutation's resolver table \"{resolverTable}\".",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate that all parameters of mutation match a colum in the mutation table
        /// </summary>
        private void ValidateMutArgsMatchTableColumns(
            string tableName,
            TableDefinition table,
            Dictionary<string, InputValueDefinitionNode> mutArguments)
        {
            Dictionary<string, InputValueDefinitionNode> arguments = mutArguments;
            IEnumerable<string> nonColumnArgs = arguments.Keys.Except(table.Columns.Keys);
            if (nonColumnArgs.Any())
            {
                throw new ConfigValidationException(
                    $"Arguments [{string.Join(", ", nonColumnArgs)}] are not valid columns of the table " +
                    $"\"{tableName}\" associated with this mutation.",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate mutation argument types match table column types
        /// </summary>
        private void ValidateMutArgTypesMatchTableColTypes(
            string tableName,
            TableDefinition table,
            Dictionary<string, InputValueDefinitionNode> mutArguments)
        {
            List<string> typeMismatchMessages = new();
            foreach (KeyValuePair<string, InputValueDefinitionNode> nameArgPair in mutArguments)
            {
                string argName = nameArgPair.Key;
                InputValueDefinitionNode argument = nameArgPair.Value;

                ColumnDefinition matchedCol = table.Columns[argName];

                if (!GraphQLTypeEqualsColumnType(argument.Type, matchedCol.SystemType))
                {
                    typeMismatchMessages.Add(
                        $"Argument \"{argName}\" with type \"{InnerTypeStr(argument.Type)}\" does not match " +
                        $"the type of \"{argName}\" in table \"{tableName}\" with type \"{matchedCol.SystemType}\"");
                }
            }

            if (typeMismatchMessages.Any())
            {
                throw new ConfigValidationException(
                    $"SystemType mismatch between mutation arguments and columns of mutation table. " +
                    string.Join(" ", typeMismatchMessages),
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the arguments of the insert mutation are properly set to nullable or not
        /// </summary>
        /// <remarks>
        /// In the current implemetation,  none of the arguments in insert mutations
        /// are nullable, this may change when the projects starts to provide nullable
        /// type support in the database.
        /// </remarks>
        private void ValidateArgNullabilityInInsertMut(
            TableDefinition table,
            Dictionary<string, InputValueDefinitionNode> mutArguments)
        {
            List<string> shouldBeNullable = new();
            List<string> shouldBeNonNullable = new();
            foreach (KeyValuePair<string, InputValueDefinitionNode> nameArgPair in mutArguments)
            {
                string argName = nameArgPair.Key;
                InputValueDefinitionNode argument = nameArgPair.Value;

                if (table.Columns[argName].HasDefault && argument.Type.IsNonNullType())
                {
                    shouldBeNullable.Add(argName);
                }
                else if (!argument.Type.IsNonNullType())
                {
                    shouldBeNonNullable.Add(argName);
                }
            }

            if (shouldBeNullable.Any() || shouldBeNonNullable.Any())
            {
                string shouldBeNullableMsg =
                    shouldBeNullable.Any() ?
                    $"Arguments [{string.Join(", ", shouldBeNullable)}] must be nullable. "
                    : string.Empty;

                string shouldBeNonNullableMsg =
                    shouldBeNonNullable.Any() ?
                    $"Arguments [{string.Join(", ", shouldBeNonNullable)}] must not be nullable."
                    : string.Empty;

                throw new ConfigValidationException(
                    $"Insert mutation arguments have incorrent nullability. " +
                    shouldBeNullableMsg +
                    shouldBeNonNullableMsg,
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate there insert mutation has the correct args
        /// </summary>
        /// <remarks>
        /// In the current implemetation,
        /// all but autogenerated columns must be added as arguments
        /// </remarks>
        private void ValidateInsertMutHasCorrectArgs(
            TableDefinition table,
            Dictionary<string, InputValueDefinitionNode> mutArgs)
        {
            List<string> requiredArguments = new();
            foreach (KeyValuePair<string, ColumnDefinition> nameColumnPair in table.Columns)
            {
                string columnName = nameColumnPair.Key;
                ColumnDefinition column = nameColumnPair.Value;

                if (!column.IsAutoGenerated)
                {
                    requiredArguments.Add(columnName);
                }
            }

            ValidateFieldArguments(mutArgs.Keys, requiredArguments: requiredArguments);
        }

        /// <summary>
        /// Validate that the arguments of the update mutation are properly set to nullable or not
        /// </summary>
        /// <remarks>
        /// In the current implemetation, only primary key arguments cannot be nullable
        /// </remarks>
        private void ValidateArgNullabilityInUpdateMut(
            TableDefinition table,
            Dictionary<string, InputValueDefinitionNode> mutArguments)
        {
            List<string> shouldNotBeNullable = new();
            foreach (KeyValuePair<string, InputValueDefinitionNode> nameArgPair in mutArguments)
            {
                string argName = nameArgPair.Key;
                InputValueDefinitionNode argument = nameArgPair.Value;

                if (!argument.Type.IsNonNullType() && table.PrimaryKey.Contains(argName))
                {
                    shouldNotBeNullable.Add(argName);
                }
            }

            if (shouldNotBeNullable.Any())
            {
                throw new ConfigValidationException(
                    $"The arguments [{string.Join(", ", shouldNotBeNullable)}] cannot be null in an " +
                    "update mutation. All primary key arguments must be non nullable.",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that none of the provided field arguments are nullable
        /// </summary>
        private void ValidateFieldArgumentsAreNonNullable(Dictionary<string, InputValueDefinitionNode> arguments)
        {
            IEnumerable<string> nullableArgs = arguments.Keys.Where(argName => !arguments[argName].Type.IsNonNullType());

            if (nullableArgs.Any())
            {
                throw new ConfigValidationException(
                    $"Field arguments [{string.Join(", ", nullableArgs)}] must not be nullable.",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate there are no queries which return a type with a scalar inner type
        /// types with inner type scalar: String, String!, [String!]!
        /// </summary>
        private void ValidateNoScalarInnerTypeQueries(Dictionary<string, FieldDefinitionNode> queries)
        {
            IEnumerable<string> queryNames = queries.Keys;
            IEnumerable<string> scalarTypeQueries = queryNames.Where(name => IsScalarType(InnerType(queries[name].Type)));

            if (scalarTypeQueries.Any())
            {
                throw new ConfigValidationException(
                    $"Query fields [{string.Join(", ", scalarTypeQueries)}] have invalid return types. " +
                    "There is no support for queries returning scalar types or list of scalar types.",
                    _schemaValidationStack
                );
            }
        }
    }
}
