using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
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
        private ISchema _schema;
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
        public SqlConfigValidator(IMetadataStoreProvider metadataStoreProvider, GraphQLService graphQLService)
            : this()
        {
            _config = metadataStoreProvider.GetResolvedConfig();
            _schema = graphQLService.Schema;

            foreach (IDefinitionNode node in _schema.ToDocument().Definitions)
            {
                if (node is ObjectTypeDefinitionNode)
                {
                    ObjectTypeDefinitionNode objectTypeDef = (ObjectTypeDefinitionNode)node;

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

        /// <summary>
        /// Private constructor used by public constructor to initialize all the members
        /// </summary>
        private SqlConfigValidator()
        {
            _configValidationStack = MakeConfigPosition();
            _schemaValidationStack = MakeSchemaPosition();
            _types = new();
            _mutations = new();
            _queries = new();
            _dbSchemaIsValidated = false;
            _graphQLTypesAreValidated = false;
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
            if (_config.GraphqlTypes == null)
            {
                throw new ConfigValidationException(
                    $"Config must have a \"GraphQLTypes\" element.",
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
            if (_config.MutationResolvers == null && _mutations.Count > 0)
            {
                throw new ConfigValidationException(
                    $"Config must have a \"MutationResolvers\" element",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate database has tables
        /// </summary>
        private void ValidateDatabaseHasTables()
        {
            if (_config.DatabaseSchema.Tables == null)
            {
                throw new ConfigValidationException(
                    "Database schema must have a \"Tables\" element",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate table has columns
        /// </summary>
        private void ValidateTableHasColumns(TableDefinition table)
        {
            if (table.Columns == null)
            {
                throw new ConfigValidationException(
                    "Table must have a \"Columns\" element.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate table has primary key
        /// </summary>
        private void ValidateTableHasPrimaryKey(TableDefinition table)
        {
            if (table.PrimaryKey == null)
            {
                throw new ConfigValidationException(
                    "Table must have a \"PrimaryKey\" element.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate table has primary key
        /// </summary>
        private void ValidateTablePrimaryKey(TableDefinition table)
        {
            foreach (string primaryKey in table.PrimaryKey)
            {
                if (!table.Columns.ContainsKey(primaryKey))
                {
                    throw new ConfigValidationException(
                        $"No column found in table corresponding to primary key column \"{primaryKey}\".",
                        _configValidationStack);
                }
            }
        }

        /// <summary>
        /// Validate foreign key has referenced table
        /// </summary>
        public void ValidateForeignKeyHasRefTable(ForeignKeyDefinition foreignKey)
        {
            if (foreignKey.ReferencedTable == null)
            {
                throw new ConfigValidationException(
                    "Foreign key must have a \"ReferencedTable\" element.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate foreign key referenced table
        /// </summary>
        private void ValidateForeignKeyRefTableExists(string refTableName)
        {
            if (!ExistsTableWithName(refTableName))
            {
                throw new ConfigValidationException(
                    $"Referenced table \"{refTableName}\" does not exit in the database schema.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate that the referenced table of the foreign key has a primary key
        /// </summary>
        private void ValidateFKRefTabHasPk(string refTableName)
        {
            TableDefinition refTable = GetTableWithName(refTableName);
            if (refTable.PrimaryKey == null)
            {
                throw new ConfigValidationException(
                    $"Referenced table \"{refTableName}\" must have a primary key.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate foreign key has columns
        /// </summary>
        private void ValidateForeignKeyHasColumns(ForeignKeyDefinition foreignKey)
        {
            if (foreignKey.Columns == null)
            {
                throw new ConfigValidationException("Foreign key must have columns", _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that the count of foreign key columns matches the number of columns of the primary
        /// key referecenced by that foreign key
        /// </summary>
        private void ValidateFKColCountMatchesRefTablePKColCount(ForeignKeyDefinition foreignKey)
        {
            TableDefinition referencedTable = GetTableWithName(foreignKey.ReferencedTable);
            if (foreignKey.Columns.Count != referencedTable.PrimaryKey.Count)
            {
                throw new ConfigValidationException(
                    $"Mismatch between foreign key column count and referenced table \"{foreignKey.ReferencedTable}\"" +
                    "'s primary key column count.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that the foreign key column has a matching column in the table the foreign key
        /// belongs to
        /// </summary>
        private void ValidateFKColumnHasMatchingTableColumn(string fkColumnName, TableDefinition table)
        {
            if (!table.Columns.ContainsKey(fkColumnName))
            {
                throw new ConfigValidationException(
                    $"Table does not contain column for foreign key column \"{fkColumnName}\".",
                    _configValidationStack);
            }
        }

        private void ValidateRefTableHasColumns(string refTableName)
        {
            if (GetTableWithName(refTableName).Columns == null)
            {
                throw new ConfigValidationException(
                    $"Referenced table \"{refTableName}\" does not have \"Columns\" element.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that the type of the foreign key column matches its equivalent column primary
        /// key column in the referenced table
        /// </summary>
        private void ValidateFKColTypeMatchesRefTabPKColType(ForeignKeyDefinition foreignKey, int columnIndex, TableDefinition foreignKeyTable)
        {
            string columnName = foreignKey.Columns[columnIndex];
            TableDefinition referencedTable = GetTableWithName(foreignKey.ReferencedTable);
            ColumnType columnType = foreignKeyTable.Columns[columnName].Type;
            string referencedPrimaryKeyColumnName = referencedTable.PrimaryKey[columnIndex];
            ColumnDefinition referencedPrimaryKeyColumn = referencedTable.Columns[referencedPrimaryKeyColumnName];

            if (!ColumnDefinition.TypesAreEqual(columnType, referencedPrimaryKeyColumn.Type))
            {
                throw new ConfigValidationException(
                    $"Type mismatch between foreign key column \"{columnName}\" with type \"{columnType}\" and " +
                    $"primary key column \"{foreignKey.ReferencedTable}\".\"{referencedPrimaryKeyColumnName}\" " +
                    $"with type \"{referencedPrimaryKeyColumn.Type}\". Look into Models.ColumnDefinition.TypesAreEqual " +
                    "to learn about how type equality is determined.",
                    _configValidationStack);
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
        private void ValidateTypesMatchSchemaTypes(Dictionary<string, GraphqlType> types)
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
        /// there is no unmatched non scalar schema field unmatched to a config field
        /// </summary>
        private void ValidateConfigFieldsMatchSchemaFields(
            Dictionary<string, GraphqlField> configFields,
            Dictionary<string, FieldDefinitionNode> schemaFields)
        {
            IEnumerable<string> unmatchedConfigFields = configFields.Keys.Except(schemaFields.Keys);

            // note that scalar fields can be matched to table columns so they don't
            // need to match a config field
            Dictionary<string, FieldDefinitionNode> nonScalarFields = GetNonScalarFields(schemaFields);
            IEnumerable<string> unmatchedSchemaNonScalarFields = nonScalarFields.Keys.Except(configFields.Keys);

            if (unmatchedConfigFields.Any() || unmatchedSchemaNonScalarFields.Any())
            {
                string unmatchedConFieldsMessage =
                    unmatchedConfigFields.Any() ?
                    $"[{string.Join(", ", unmatchedConfigFields)}] fields don't match any field in the schema. "
                    : string.Empty;
                string unmatchedSchFieldsMessage =
                    unmatchedSchemaNonScalarFields.Any() ?
                    $"[{string.Join(", ", unmatchedSchemaNonScalarFields)}] schema fields are not matched by any config fields."
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
            List<string> paginationields)
        {
            List<string> fieldsWithArguments = new();
            foreach (string fieldName in paginationields)
            {
                if (GetArgumentFromField(typeFields[fieldName]).Count > 0)
                {
                    fieldsWithArguments.Add(fieldName);
                }
            }

            if (fieldsWithArguments.Any())
            {
                throw new ConfigValidationException(
                    $"[{string.Join(", ", fieldsWithArguments)}] field of a pagination type must not have arguments",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate the type of "items" field in a Pagination type
        /// </summary>
        private void ValidateItemsFieldType(FieldDefinitionNode itemsField)
        {
            ITypeNode itemsType = itemsField.Type;
            if (!IsListType(itemsType) || !IsCustomType(itemsType))
            {
                throw new ConfigValidationException(
                    "\"items\" must return a list type of non scalars",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate the type of "endCursor" field in a Pagination type
        /// </summary>
        private void ValidateEndCursorFieldType(FieldDefinitionNode endCursorField)
        {
            ITypeNode endCursorFieldType = endCursorField.Type;
            if (IsListType(endCursorFieldType) || InnerType(endCursorFieldType) != "String")
            {
                throw new ConfigValidationException(
                    "\"endCursor\" must return a \"String\" type",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate the type of "hasNextPage" field in a Pagination type
        /// </summary>
        private void ValidateHasNextPageFieldType(FieldDefinitionNode hasNextPageField)
        {
            ITypeNode hasNextPageFieldType = hasNextPageField.Type;
            if (IsListType(hasNextPageFieldType) || InnerType(hasNextPageFieldType) != "Boolean")
            {
                throw new ConfigValidationException(
                    "\"hasNextPage\" must return a \"Boolean\" type",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate pagination type has correct name
        /// </summary>
        private void ValidatePaginationTypeName(string paginationTypeName)
        {
            FieldDefinitionNode itemsField = GetTypeFields(paginationTypeName)["items"];
            string paginationUnderlyingType = InnerType(itemsField.Type);
            string expectedTypeName = $"{paginationUnderlyingType}Connection";
            if (paginationTypeName != expectedTypeName)
            {
                throw new ConfigValidationException(
                    $"Pagination type on \"{paginationUnderlyingType}\" must be called \"{expectedTypeName}\"",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate graphQLType has table
        /// </summary>
        private void ValidateGraphQLTypeHasTable(GraphqlType type)
        {
            if (type.Table == null)
            {
                throw new ConfigValidationException(
                    "This type must contain a \"Table\" element",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validate that type does not share an underlying table with any other type
        /// </summary>
        private void ValidateGQLTypeTableIsUnique(GraphqlType type, Dictionary<string, string> tableToType)
        {
            if (tableToType.ContainsKey(type.Table))
            {
                throw new ConfigValidationException(
                    $"Type shares underlying table \"{type.Table}\" with other type " +
                    $"\"{tableToType[type.Table]}\". All underlying type tables must be unique.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate the GraphQLType has a "fields" element
        /// </summary>
        private void ValidateGraphQLTypeHasFields(GraphqlType type)
        {
            if (type.Fields == null)
            {
                throw new ConfigValidationException(
                    "Type must have \"Fields\" element.",
                    _configValidationStack);
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
        /// <item> have structural significance (pk, fk) </item>
        /// </list>
        /// Each scalar field should either:
        /// <list type="bullet">
        /// <item> match a table column in name and type </item>
        /// <item> match a GraphqlType.Field </item>
        /// </list>
        /// </remarks>
        private void ValidateTableColumnsMatchScalarFields(string tableName, string typeName, Stack<string> tableColumnPosition)
        {
            TableDefinition table = GetTableWithName(tableName);
            Dictionary<string, ColumnDefinition> tableColumns = table.Columns;
            Dictionary<string, FieldDefinitionNode> scalarFields = GetScalarFields(GetTypeFields(typeName));

            IEnumerable<string> unmatchedTableColumns = tableColumns.Keys.Except(scalarFields.Keys);
            unmatchedTableColumns = unmatchedTableColumns.Except(GetExpectedUnMatchedColumns(table));

            IEnumerable<string> unmatchedScalarFields = scalarFields.Keys.Except(tableColumns.Keys);
            unmatchedScalarFields = unmatchedScalarFields.Except(GetExcpectedUnMatchedScalarFields(_types[typeName]));

            if (unmatchedTableColumns.Any() || unmatchedScalarFields.Any())
            {
                string unmatchedFieldsMessage =
                    unmatchedScalarFields.Any() ?
                    $"Fields [{string.Join(", ", unmatchedScalarFields)}] are not matched. " :
                    string.Empty;

                string unmatchedColumnsMessage =
                    unmatchedTableColumns.Any() ?
                    $"Columns [{string.Join(", ", unmatchedTableColumns)}] are not matched." :
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

            foreach (String matchedName in matchedColumnAndFieldNames)
            {
                ColumnType columnType = tableColumns[matchedName].Type;
                ITypeNode fieldType = typeFields[matchedName].Type;

                if (!GraphQLTypesEqualsColumnType(fieldType, columnType))
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
        /// Validate that field does not have a pagination list type e.g. [BookConnection]
        /// </summary>
        private void ValidateFieldTypeIsNotPaginationListType(FieldDefinitionNode field)
        {
            if (IsPaginatedType(InnerType(field.Type)) && field.Type.IsListType())
            {
                throw new ConfigValidationException(
                    $"Field cannot return a list of pagination type \"{InnerType(field.Type)}\".",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate if argument names match required arguments
        /// </summary>
        private void ValidateFieldHasRequiredArguments(IEnumerable<string> fieldArgumentNames, IEnumerable<string> requiredArguments)
        {
            IEnumerable<string> missingArguments = requiredArguments.Except(fieldArgumentNames);
            IEnumerable<string> extraArguments = fieldArgumentNames.Except(requiredArguments);

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
                    $"Field does not have the required arguments [{string.Join(", ", requiredArguments)}]. " +
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
        /// Validate field has no unexpected arguments
        /// </summary>
        private void ValidateFieldHasNoUnexpectedArguments(IEnumerable<string> fieldArgNames, IEnumerable<string> expectedArgNames)
        {
            IEnumerable<string> unexpectedArguments = fieldArgNames.Except(expectedArgNames);
            if (unexpectedArguments.Any())
            {
                throw new ConfigValidationException(
                    $"Field has unexpected arguments [{string.Join(", ", unexpectedArguments)}]",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Make sure the field has no association table
        /// </summary>
        private void ValidateNoAssociationTable(GraphqlField field)
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
        private void ValidateHasAssociationTable(GraphqlField field)
        {
            if (string.IsNullOrEmpty(field.AssociativeTable))
            {
                throw new ConfigValidationException(
                    $"Must have Associative Table in {field.RelationshipType} field.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates if field has either one left foreign key or only right foreign key
        /// Both and neither result in an exception
        /// </summary>
        private void ValidateLeftXOrRightForeignKey(GraphqlField field)
        {
            if (HasLeftForeignKey(field) ^ HasRightForeignKey(field))
            {
                throw new ConfigValidationException(
                    $"{field.RelationshipType} field must have either only one left foreign key or " +
                    "only one right foreign key.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that field has only left foreign key
        /// </summary>
        private void ValidateHasOnlyLeftForeignKey(GraphqlField field)
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
        private void ValidateHasOnlyRightForeignKey(GraphqlField field)
        {
            if (HasLeftForeignKey(field) || !HasRightForeignKey(field))
            {
                throw new ConfigValidationException(
                    $"{field.RelationshipType} field must have only right foreign key.",
                    _configValidationStack);
            }
        }

        /// <summary>
        /// Validates that the field has both left and right foreign keys
        /// </summary>
        private void ValidateHasBothLeftAndRightFK(GraphqlField field)
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
        private void ValidateLeftForeignKey(GraphqlField field, string type)
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
        private void ValidateRightForeignKey(GraphqlField field, string returnedType)
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
        /// Validate that the field's associative table exists
        /// </summary>
        private void ValidateAssociativeTableExists(GraphqlField field)
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
        private void ValidateLeftAndRightFkForM2MField(GraphqlField field)
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
        /// Validate that Config.GraphqlTypes has already been validated
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
            int nullCount = ids.Count(id => id == null);

            if (nullCount > 0)
            {
                throw new ConfigValidationException(
                    $"{nullCount} mutation ids missing. All mutation resolvers must have an \"Id\" element.",
                    _configValidationStack
                );
            }
        }

        /// <summary>
        /// Validate that all mutation resolver ids are unique
        /// </summary>
        private void ValidateNoDuplicateIds(IEnumerable<string> ids)
        {
            Func<string, int> idCount = id => ids.Count(_id => _id == id);
            IEnumerable<string> duplicateIds = ids.Where(id => idCount(id) > 1).Distinct();

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
                    "Mutation resolver must have a \"Table\" element.",
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
        private void ValidateMutResolverOperation(MutationOperation op, List<MutationOperation> supportedOperations)
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
        private void ValidateMutHasNotListType(FieldDefinitionNode mutation)
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
            if (IsCustomType(mutation.Type) && resolverTable != GetTypeTable(InnerType(mutation.Type)))
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

                if (!GraphQLTypesEqualsColumnType(argument.Type, matchedCol.Type))
                {
                    typeMismatchMessages.Add(
                        $"Argument \"{argName}\" with type \"{InnerType(argument.Type)}\" does not match " +
                        $"the type of \"{argName}\" in table \"{tableName}\" with type \"{matchedCol.Type}\"");
                }
            }

            if (typeMismatchMessages.Any())
            {
                throw new ConfigValidationException(
                    $"Type mismatch between mutation arguments and columns of mutation table. " +
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
            Dictionary<string, InputValueDefinitionNode> mutArguments)
        {
            List<string> cannotBeNullableArgs = new();
            foreach (KeyValuePair<string, InputValueDefinitionNode> nameArgPair in mutArguments)
            {
                string argName = nameArgPair.Key;
                InputValueDefinitionNode argument = nameArgPair.Value;

                if (!argument.Type.IsNonNullType())
                {
                    cannotBeNullableArgs.Add(argName);
                }
            }

            if (cannotBeNullableArgs.Any())
            {
                throw new ConfigValidationException(
                    $"The arguments [{string.Join(", ", cannotBeNullableArgs)}] cannot be null in an " +
                    "insert mutation. All arguments must be non nullable.",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate there is no missing arguments in insert mutation
        /// </summary>
        /// <remarks>
        /// In the current implemetation,
        /// all but autogenerated columns can must be added as arguments
        /// </remarks>
        private void ValidateNoMissingArgsInInsertMut(
            TableDefinition table,
            Dictionary<string, InputValueDefinitionNode> mutArguments)
        {
            List<string> requiredNonArgColumns = new();
            foreach (string column in table.Columns.Keys)
            {
                if (!mutArguments.ContainsKey(column) && !table.Columns[column].hasDefault)
                {
                    requiredNonArgColumns.Add(column);
                }
            }

            if (requiredNonArgColumns.Any())
            {
                throw new ConfigValidationException(
                    $"Insert mutation missing argument/s [{string.Join(", ", requiredNonArgColumns)}] required to perform insertion.",
                    _schemaValidationStack);
            }
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
            List<string> cannotBeNullableArgs = new();
            foreach (KeyValuePair<string, InputValueDefinitionNode> nameArgPair in mutArguments)
            {
                string argName = nameArgPair.Key;
                InputValueDefinitionNode argument = nameArgPair.Value;

                if (!argument.Type.IsNonNullType() && table.PrimaryKey.Contains(argName))
                {
                    cannotBeNullableArgs.Add(argName);
                }
            }

            if (cannotBeNullableArgs.Any())
            {
                throw new ConfigValidationException(
                    $"The arguments [{string.Join(", ", cannotBeNullableArgs)}] cannot be null in an " +
                    "update mutation. All primary key arguments must be non nullable.",
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate there is no missing arguments in update mutation
        /// </summary>
        /// <remarks>
        /// In the current implemetation,
        /// all primary key columns are required
        /// and at least one non primary key column
        /// </remarks>
        private void ValidateNoMissingArgsInUpdateMut(
            string tableName,
            TableDefinition table,
            Dictionary<string, InputValueDefinitionNode> mutArguments)
        {
            IEnumerable<string> missingPkArgs = table.PrimaryKey.Except(mutArguments.Keys);
            IEnumerable<string> nonPkColumns = table.Columns.Keys.Except(table.PrimaryKey);
            IEnumerable<string> nonPkColArgs = mutArguments.Keys.Intersect(nonPkColumns);

            if (missingPkArgs.Any() || !nonPkColArgs.Any())
            {
                string missingPkArgsMessage =
                    missingPkArgs.Any() ?
                    $"Missing [{string.Join(", ", missingPkArgs)}] arguments representing the " +
                    $"primary key columns of the table \"{tableName}\" associated with the mutation. "
                    : string.Empty;
                string noNonPkArgsMessage =
                    !nonPkColArgs.Any() ?
                    $"Mutation has no non primary key arguments. At least one non primary key argument " +
                    $"[{string.Join(", ", nonPkColumns)}] must be present."
                    : string.Empty;

                throw new ConfigValidationException(
                    $"Missing arguments from update mutation. " +
                    missingPkArgsMessage +
                    noNonPkArgsMessage,
                    _schemaValidationStack
                );
            }
        }

        /// <summary>
        /// Validate there are no queries which return a scalar type
        /// </summary>
        private void ValidateNoScalarTypeQueries(Dictionary<string, FieldDefinitionNode> queries)
        {
            IEnumerable<string> queryNames = queries.Keys;
            IEnumerable<string> scalarTypeQueries = queryNames.Where(name => IsScalarType(queries[name].Type));

            if (scalarTypeQueries.Any())
            {
                throw new ConfigValidationException(
                    $"Query fields [{string.Join(", ", scalarTypeQueries)}] have invalid return types. " +
                    "There is no support for queries returning scalar types.",
                    _schemaValidationStack
                );
            }
        }
    }
}
