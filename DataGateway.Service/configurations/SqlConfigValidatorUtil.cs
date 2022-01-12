using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Models;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Configurations
{

    /// This portion of the class
    /// hold all function which do not directly do validation
    public partial class SqlConfigValidator : IConfigValidator
    {
        /// <summary>
        /// Make a stack for the a position in the config
        /// If no path is passed, make starting stack,
        /// add the path to the stack otherwise
        /// </summary>
        private static Stack<string> MakeConfigPosition(IEnumerable<string> path = null)
        {
            Stack<string> configStack = new();
            configStack.Push("Config");

            if (path != null)
            {
                foreach (string pathElement in path)
                {
                    configStack.Push(pathElement);
                }
            }

            return configStack;
        }

        /// <summary>
        /// Make a stack for the a position in the config
        /// If no path is passed, make starting stack,
        /// add the path to the stack otherwise
        /// </summary>
        private static Stack<string> MakeSchemaPosition(IEnumerable<string> path = null)
        {
            Stack<string> schemaStack = new();
            schemaStack.Push("GQL Schema");

            if (path != null)
            {
                foreach (string pathElement in path)
                {
                    schemaStack.Push(pathElement);
                }
            }

            return schemaStack;
        }

        /// <summary>
        /// Sets the validation status of the database schema
        /// </summary>
        private void SetDatabaseSchemaValidated(bool flag)
        {
            _dbSchemaIsValidated = flag;
        }

        /// <summary>
        /// Gets the validation status of the database schema
        /// </summary>
        private bool IsDatabaseSchemaValidated()
        {
            return _dbSchemaIsValidated;
        }

        /// <summary>
        /// Sets the validation status of the GraphQLTypes
        /// </summary>
        private void SetGraphQLTypesValidated(bool flag)
        {
            _graphQLTypesAreValidated = flag;
        }

        /// <summary>
        /// Gets the validation status of the GraphQLTypes
        /// </summary>
        private bool IsGraphQLTypesValidated()
        {
            return _graphQLTypesAreValidated;
        }

        /// <summary>
        /// Print the reversed validation stack since the validation stack
        /// contains the smallest context at the top and the largest at the bottom
        /// </summary>
        private static string PrettyPrintValidationStack(Stack<string> validationStack)
        {
            string[] stackArray = validationStack.ToArray();
            Array.Reverse(stackArray);
            return string.Join(" > ", stackArray);
        }

        /// <summary>
        /// Move into <c>path</c> from the current position in config
        /// </summary>
        private void ConfigStepInto(string path)
        {
            _configValidationStack.Push(path);
        }

        /// <summary>
        /// Move out of <c>path</c> from the current postion in config
        /// </summary>
        /// <exception cref="ArgumentException">
        /// If the last element in the config path is not equal to the
        /// the parameter path
        /// </exception>
        private void ConfigStepOutOf(string path)
        {
            string lastdPath = _configValidationStack.Peek();
            if (lastdPath != path)
            {
                throw new ArgumentException(
                    $"Cannot step out of {path} because config is currently " +
                    $"being validated at {PrettyPrintValidationStack(_configValidationStack)}");
            }
            else
            {
                _configValidationStack.Pop();
            }
        }

        /// <summary>
        /// Move into <c>path</c> from the current position in the GraphQL schema
        /// </summary>
        private void SchemaStepInto(string path)
        {
            _schemaValidationStack.Push(path);
        }

        /// <summary>
        /// Move out of <c>path</c> from the current postion in the GraphQL schema
        /// </summary>
        /// <exception cref="ArgumentException">
        /// If the last element in the schema path is not equal to the
        /// the parameter path
        /// </exception>
        private void SchemaStepOutOf(string path)
        {
            string lastdPath = _schemaValidationStack.Peek();
            if (lastdPath != path)
            {
                throw new ArgumentException(
                    $"Cannot step out of {path} because schema is currently " +
                    $"being validated at {PrettyPrintValidationStack(_schemaValidationStack)}");
            }
            else
            {
                _schemaValidationStack.Pop();
            }
        }

        /// <summary>
        /// Gets fields from GraphQL type
        /// </summary>
        private Dictionary<string, FieldDefinitionNode> GetTypeFields(string typeName)
        {
            return GetObjTypeDefFields(_types[typeName]);
        }

        /// <summary>
        /// Get fields from a HotCholate ObjectTypeDefinitionNode
        /// </summary>
        private static Dictionary<string, FieldDefinitionNode> GetObjTypeDefFields(ObjectTypeDefinitionNode objectTypeDef)
        {
            Dictionary<string, FieldDefinitionNode> fields = new();
            foreach (FieldDefinitionNode field in objectTypeDef.Fields)
            {
                fields.Add(field.Name.Value, field);
            }

            return fields;
        }

        /// <summary>
        /// Gets database tables from config
        /// </summary>
        private Dictionary<string, TableDefinition> GetDatabaseTables()
        {
            return _config.DatabaseSchema.Tables;
        }

        /// <summary>
        /// Gets graphql types from config
        /// </summmary>
        private Dictionary<string, GraphqlType> GetGraphQLTypes()
        {
            return _config.GraphqlTypes;
        }

        /// <summary>
        /// Checks if table exists in database schema with that name
        /// </summary>
        private bool ExistsTableWithName(string tableName)
        {
            return _config.DatabaseSchema.Tables.ContainsKey(tableName);
        }

        /// <summary>
        /// Get table definition from name
        /// Expects valid tableName
        /// </summary>
        /// <exception cref="ArgumentException">
        /// If the given table name does not exist in the schema
        /// </exception>
        private TableDefinition GetTableWithName(string tableName)
        {
            if (!ExistsTableWithName(tableName))
            {
                // note that this is not an issue with the config, this is an issue with the
                // logic of how the config is being validate, this function should be called
                // with valid table names
                throw new ArgumentException("Invalid table name was provided");
            }

            return _config.DatabaseSchema.Tables[tableName];
        }

        /// <summary>
        ///  Checks if table has foreign key
        /// </summary>
        private static bool TableHasForeignKey(TableDefinition table)
        {
            return table.ForeignKeys != null;
        }

        /// <summary>
        /// Returns if type is paginated or not
        /// </summary>
        private bool IsPaginatedType(string typeName)
        {
            return _config.GraphqlTypes[typeName].IsPaginationType;
        }

        /// <summary>
        /// Gets innermost type of Type
        /// </summary>
        private static string InnerType(ITypeNode type)
        {
            while (type.ToString() != type.InnerType().ToString())
            {
                type = type.InnerType();
            }

            return type.ToString();
        }

        /// <summary>
        /// Checks if ITypeNode is list type
        /// </summary>
        /// <remarks>
        /// The build in IsListType function of ITypeNode will
        /// return false for [Book]! so that needs to be addressed
        /// with this custom function
        /// </remakrs>
        private static bool IsListType(ITypeNode type)
        {
            return type.NullableType().IsListType();
        }

        /// <summary>
        /// Checks if a ITypeNode is a custom type
        /// Does it by checking if its inner types corresponds to any of the
        /// types extracted from the schema
        /// </summary>
        private bool IsCustomType(ITypeNode type)
        {
            return _types.ContainsKey(InnerType(type));
        }

        /// <summary>
        /// Get arguments from field and return a dictionary in [argName, argument] format
        /// </summary>
        private static Dictionary<string, InputValueDefinitionNode> GetArgumentFromField(FieldDefinitionNode field)
        {
            Dictionary<string, InputValueDefinitionNode> arguments = new();

            foreach (InputValueDefinitionNode node in field.Arguments)
            {
                arguments.Add(node.Name.ToString(), node);
            }

            return arguments;
        }

        /// <summary>
        /// Checks if ITypeNode is scalar type which means
        /// it is not a custom type or a list type
        /// </summary>
        private bool IsScalarType(ITypeNode type)
        {
            return !IsCustomType(type) && !IsListType(type);
        }

        /// <summary>
        /// Returns the scalar fields from a dictionary of fields
        /// </summary>
        private Dictionary<string, FieldDefinitionNode> GetScalarFields(Dictionary<string, FieldDefinitionNode> fields)
        {
            Dictionary<string, FieldDefinitionNode> scalarFields = new();
            foreach (KeyValuePair<string, FieldDefinitionNode> nameFieldPair in fields)
            {
                string fieldName = nameFieldPair.Key;
                FieldDefinitionNode field = nameFieldPair.Value;
                if (IsScalarType(field.Type))
                {
                    scalarFields.Add(fieldName, field);
                }
            }

            return scalarFields;
        }

        /// <summary>
        /// Returns the non scalar fields from a dictionary of fields
        /// </summary>
        private Dictionary<string, FieldDefinitionNode> GetNonScalarFields(Dictionary<string, FieldDefinitionNode> fields)
        {
            Dictionary<string, FieldDefinitionNode> nonScalarFields = new();
            foreach (KeyValuePair<string, FieldDefinitionNode> nameFieldPair in fields)
            {
                string fieldName = nameFieldPair.Key;
                FieldDefinitionNode field = nameFieldPair.Value;
                if (!IsScalarType(field.Type))
                {
                    nonScalarFields.Add(fieldName, field);
                }
            }

            return nonScalarFields;
        }

        /// <summary>
        /// Checks if a GraphQL type is equal to a ColumnType
        /// </summary>
        private static bool GraphQLTypesEqualsColumnType(ITypeNode gqlType, ColumnType columnType)
        {
            // column types cannot be list types so the only expected difference between
            // the gqlType.ToString() and GetGraphQLTypeForColumn(columnType) is that the
            // string version of the gqlType might include ! so InnerType is applied.
            return GetGraphQLTypeForColumnType(columnType) == InnerType(gqlType) && !IsListType(gqlType);
        }

        /// <summary>
        /// Get the GraphQL type equivalent from ColumnType
        /// </summary>
        private static string GetGraphQLTypeForColumnType(ColumnType type)
        {
            switch (type)
            {
                case ColumnType.Text:
                case ColumnType.Varchar:
                    return "String";
                case ColumnType.Bigint:
                case ColumnType.Int:
                case ColumnType.Smallint:
                    return "Int";
                default:
                    throw new ArgumentException($"ColumnType {type} not handled by case. Please add a case resolving " +
                                                $"{type} to the appropriate GraphQL type");
            }
        }

        /// <summary>
        /// Get the columns which are expected to be unmatched with the type schema fields from table columns
        /// That will include columns which have other purposes like being part of the foreign key or the primary key
        /// </summary>
        private static IEnumerable<string> GetExpectedUnMatchedColumns(TableDefinition table)
        {
            List<string> excpectedUnmatchedColumns = new();

            excpectedUnmatchedColumns.AddRange(table.PrimaryKey);

            foreach (KeyValuePair<string, ForeignKeyDefinition> nameFKPair in table.ForeignKeys)
            {
                ForeignKeyDefinition foreignKey = nameFKPair.Value;
                excpectedUnmatchedColumns.AddRange(foreignKey.Columns);
            }

            return excpectedUnmatchedColumns;
        }

        /// <summary>
        /// Get the scalar fields which are excpected to unmatched with the table columns of the underlying
        /// table of the type
        /// </summary>
        private IEnumerable<string> GetExcpectedUnMatchedScalarFields(ObjectTypeDefinitionNode type)
        {
            // if there are scalar fields which have an equivalent GraphqlType.Field, they don't need
            // to match a table column
            return _config.GraphqlTypes[type.Name.Value].Fields.Keys;
        }

        /// <summary>
        /// Check that GraphQLType.Field has only a left foreign key
        /// </summary>
        private static bool HasLeftForeignKey(GraphqlField field)
        {
            return !string.IsNullOrEmpty(field.LeftForeignKey);
        }

        /// <summary>
        /// Check that GraphQLType.Field has only a right foreign key
        /// </summary>
        private static bool HasRightForeignKey(GraphqlField field)
        {
            return !string.IsNullOrEmpty(field.RightForeignKey);
        }

        /// <summary>
        /// Get the db table underlying the GraphQL type
        /// Assumes type is valid throws KeyNotFoundException otherwise
        /// </summary>
        private string GetTypeTable(string type)
        {
            return GetGraphQLTypes()[type].Table;
        }

        /// <summary>
        /// Whether a table contains a foreign key by the given name
        /// ArgumentException on invalid tableName
        /// </summary>
        private bool TableContainsForeignKey(string tableName, string foreignKeyName)
        {
            TableDefinition table = GetTableWithName(tableName);

            if (table.ForeignKeys == null)
            {
                return false;
            }

            return table.ForeignKeys.ContainsKey(foreignKeyName);
        }

        /// <summary>
        /// Gets mutation resolvers from config
        /// </summary>
        private List<MutationResolver> GetMutationResolvers()
        {
            return _config.MutationResolvers;
        }

        /// <summary>
        /// Get mutation resolver ids
        /// May contain null for resolvers without ids
        /// </summary>
        private IEnumerable<string> GetMutationResolverIds()
        {
            return GetMutationResolvers().Select(resolver => resolver.Id);
        }

        /// <summary>
        /// Get mutation by name
        /// </summary>
        private FieldDefinitionNode GetMutation(string mutationName)
        {
            return _mutations[mutationName];
        }

        /// <summary>
        /// Get GraphQL schema queries
        /// </summary>
        private Dictionary<string, FieldDefinitionNode> GetQueries()
        {
            return _queries;
        }
    }
}
