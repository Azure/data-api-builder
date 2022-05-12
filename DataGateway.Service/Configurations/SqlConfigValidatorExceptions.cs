using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
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
        private ISchema? _schema;
        private ISqlMetadataProvider _sqlMetadataProvider;
        private Stack<string> _configValidationStack;
        private Stack<string> _schemaValidationStack;
        private Dictionary<string, FieldDefinitionNode> _queries;
        private Dictionary<string, FieldDefinitionNode> _mutations;
        private Dictionary<string, ObjectTypeDefinitionNode> _types;
        private bool _graphQLTypesAreValidated;

        /// <summary>
        /// Sets the config and schema for the validator
        /// </summary>
        public SqlConfigValidator(
            GraphQLService graphQLService,
            ISqlMetadataProvider sqlMetadataProvider)
        {
            _configValidationStack = MakeConfigPosition(Enumerable.Empty<string>());
            _schemaValidationStack = MakeSchemaPosition(Enumerable.Empty<string>());
            _types = new();
            _mutations = new();
            _queries = new();
            _graphQLTypesAreValidated = false;

            _sqlMetadataProvider = sqlMetadataProvider;
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
        /// Validate the type of <see cref="QueryBuilder.PAGINATION_TOKEN_FIELD_NAME"/> field in a Pagination type
        /// </summary>
        private void ValidateAfterFieldType(FieldDefinitionNode afterField)
        {
            ITypeNode afterFieldType = afterField.Type;
            if (IsListType(afterFieldType) ||
                InnerTypeStr(afterFieldType) != "String" ||
                afterFieldType.IsNonNullType())
            {
                throw new ConfigValidationException(
                    $"\"{QueryBuilder.PAGINATION_TOKEN_FIELD_NAME}\" must return a nullable \"String\" type.",
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
                IsNullableType(hasNextPageFieldType))
            {
                throw new ConfigValidationException(
                    $"\"{QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME}\" must return a non nullable \"Boolean!\" type.",
                    _schemaValidationStack);
            }
        }

        /// <summary>
        /// Validate pagination type has correct name
        /// </summary>
        private void ValidatePaginationTypeName(string paginationTypeName)
        {
            FieldDefinitionNode itemsField = GetTypeFields(paginationTypeName)[QueryBuilder.PAGINATION_FIELD_NAME];
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
        private void ValidateScalarFieldsMatchingTableColumnsNullability(
            string typeName,
            string typeTable,
            Stack<string> tableColumnsPosition)
        {
            Dictionary<string, FieldDefinitionNode> scalarFields = GetScalarFields(GetTypeFields(typeName));
            IEnumerable<string> nullableScalarFields =
                scalarFields.Keys.Where(fieldName => IsNullableType(scalarFields[fieldName].Type));
            IEnumerable<string> notNullableScalarFields = scalarFields.Keys.Except(nullableScalarFields);

            TableDefinition table = GetTableWithName(typeTable);
            IEnumerable<string> nullableTableColumns =
                table.Columns.Keys.Where(colName => table.Columns[colName].IsNullable);
            IEnumerable<string> notNullableTableColumns = table.Columns.Keys.Except(nullableTableColumns);

            IEnumerable<string> shouldBeNullable = notNullableScalarFields.Intersect(nullableTableColumns);
            IEnumerable<string> shouldBeNotNullable = nullableScalarFields.Intersect(notNullableTableColumns);

            if (shouldBeNullable.Any() || shouldBeNotNullable.Any())
            {
                string shouldBeNullableMessage = shouldBeNullable.Any() ?
                    $"The fields [{string.Join(", ", shouldBeNullable)}] should be nullable. " :
                    string.Empty;
                string shouldBeNotNullableMessage = shouldBeNotNullable.Any() ?
                    $"The fields [{string.Join(", ", shouldBeNotNullable)}] should be not nullable." :
                    string.Empty;

                throw new ConfigValidationException(
                    $"Mismatch of field nullability with table columns in {PrettyPrintValidationStack(tableColumnsPosition)}." +
                    shouldBeNullableMessage +
                    shouldBeNotNullableMessage,
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

                bool isNullable = table.Columns[argName].IsNullable;
                if (isNullable && argument.Type.IsNonNullType())
                {
                    shouldBeNullable.Add(argName);
                }
                else if (!isNullable && IsNullableType(argument.Type))
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
        /// Validate the update mutation has the correct args
        /// </summary>
        /// <remarks>
        /// All but non pk autogenerated columns must be added as arguments
        /// </remarks>
        private void ValidateUpdateMutHasCorrectArgs(
            TableDefinition table,
            Dictionary<string, InputValueDefinitionNode> mutArgs)
        {
            List<string> requiredArguments = new();
            foreach (KeyValuePair<string, ColumnDefinition> nameColumnPair in table.Columns)
            {
                string columnName = nameColumnPair.Key;
                ColumnDefinition column = nameColumnPair.Value;

                if (table.PrimaryKey.Contains(columnName) || !column.IsAutoGenerated)
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

                if (table.PrimaryKey.Contains(argName))
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
            IEnumerable<string> nullableArgs = arguments.Keys.Where(argName => IsNullableType(arguments[argName].Type));

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
