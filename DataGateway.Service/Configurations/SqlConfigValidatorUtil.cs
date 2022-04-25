using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Config;
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
        private static Stack<string> MakeConfigPosition(IEnumerable<string> path)
        {
            Stack<string> configStack = new();
            configStack.Push("Config");

            foreach (string pathElement in path)
            {
                configStack.Push(pathElement);
            }

            return configStack;
        }

        /// <summary>
        /// Make a stack for the a position in the config
        /// If no path is passed, make starting stack,
        /// add the path to the stack otherwise
        /// </summary>
        private static Stack<string> MakeSchemaPosition(IEnumerable<string> path)
        {
            Stack<string> schemaStack = new();
            schemaStack.Push("GQL Schema");

            foreach (string pathElement in path)
            {
                schemaStack.Push(pathElement);
            }

            return schemaStack;
        }

        /// <summary>
        /// Sets the validation status of all the sql entities.
        /// </summary>
        private void SetSqlEntitiesValidated(bool flag)
        {
            _sqlEntitiesAreValidated = flag;
        }

        /// <summary>
        /// Gets the validation status of all the sql entities.
        /// </summary>
        private bool AreSqlEntitiesValidated()
        {
            return _sqlEntitiesAreValidated;
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

            return _runtimeConfig.Entities.;
        }

        /// <summary>
        /// Gets graphql types from config
        /// </summmary>
        private Dictionary<string, GraphQLType> GetGraphQLTypes()
        {
            return _resolverConfig.GraphQLTypes;
        }

        /// <summary>
        /// Checks if table exists in database schema with that name
        /// </summary>
        private bool ExistsTableWithName(string tableName)
        {
            return _resolverConfig.DatabaseSchema!.Tables.ContainsKey(tableName);
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
                throw new ArgumentException("Invalid table name was provided.");
            }

            return _resolverConfig.DatabaseSchema!.Tables[tableName];
        }

        /// <summary>
        /// Checks if table has foreign key
        /// </summary>
        private static bool TableHasForeignKey(TableDefinition table)
        {
            return table.ForeignKeys != null;
        }

        /// <summary>
        /// Checks if foreign key has explicitly defined columns
        /// </summary>
        private static bool HasExplicitColumns(ForeignKeyDefinition fk)
        {
            return fk.ReferencingColumns.Count > 0;
        }

        /// <summary>
        /// Checks if foreign key has explicitly defined referenced columns
        /// </summary>
        private static bool HasExplicitReferencedColumns(ForeignKeyDefinition fk)
        {
            return fk.ReferencedColumns.Count > 0;
        }

        /// <summary>
        /// Check if the list has duplicates
        /// </summary>
        private static IEnumerable<string> GetDuplicates(IEnumerable<string> enumerable)
        {
            HashSet<string> distinct = new(enumerable);
            List<string> duplicates = new();

            foreach (string elem in enumerable)
            {
                if (distinct.Contains(elem))
                {
                    distinct.Remove(elem);
                }
                else
                {
                    duplicates.Add(elem);
                }
            }

            return duplicates.Distinct();
        }

        /// <summary>
        /// Checks if config type has fields
        /// </summary>
        private static bool TypeHasFields(GraphQLType type)
        {
            return type.Fields != null;
        }

        /// <summary>
        /// A more readable version of !type.IsNonNullType
        /// </summary>
        private static bool IsNullableType(ITypeNode type)
        {
            return !type.IsNonNullType();
        }

        /// <summary>
        /// Checks if the type is a nested list type
        /// e.g. [[Book]], [[[Book!]!]!]!
        /// </summary>
        private static bool IsNestedListType(ITypeNode type)
        {
            return IsListType(InnerType(type));
        }

        /// <summary>
        /// Checks if the type is a list of pagination type
        /// e.g.
        /// [BookConnection] -> true
        /// [[BookConnection]] -> false (list of lists, not list of pagination type)
        /// </summary>
        private bool IsListOfPaginationType(ITypeNode type)
        {
            return IsListType(type) && IsPaginationType(InnerType(type));
        }

        /// <summary>
        /// Checks if the given type name is the name of a pagination type
        /// </summary>
        private bool IsPaginationTypeName(string typeName)
        {
            if (_resolverConfig.GraphQLTypes.TryGetValue(typeName, out GraphQLType? type))
            {
                return type.IsPaginationType;
            }

            return false;
        }

        /// <summary>
        /// Returns if type is a pagination type or not
        /// </summary>
        private bool IsPaginationType(ITypeNode type)
        {
            return IsPaginationTypeName(type.NullableType().ToString());
        }

        /// <summary>
        /// Gets inner type from ITypeNode in string format
        /// </summary>
        private static string InnerTypeStr(ITypeNode type)
        {
            return InnerType(type).ToString();
        }

        /// <summary>
        /// Gets inner type from ITypeNode
        /// </summary>
        /// <remarks>
        /// Go one level deep (if possible) while ignoring non nullability (!)
        /// e.g.
        /// Book, Book!, [Book], [Book!], [Book]!, [Book!]! -> Book
        /// [[Book]!]!, [[Book]]
        /// </remarks>
        private static ITypeNode InnerType(ITypeNode type)
        {
            // ITypeNode.InnerType returns the same type if no inner type
            return type.NullableType().InnerType().NullableType();
        }

        /// <summary>
        /// Checks if ITypeNode is list type
        /// </summary>
        /// <remarks>
        /// The build in IsListType function of ITypeNode will
        /// return false for [Book]! so that needs to be addressed
        /// with this custom function
        /// </remarks>
        private static bool IsListType(ITypeNode type)
        {
            return type.NullableType().IsListType();
        }

        /// <summary>
        /// Checks if a ITypeNode is a custom type
        /// Checks if the nullable type is declared in the GQL Schema
        /// </summary>
        private bool IsCustomType(ITypeNode type)
        {
            return _types.ContainsKey(type.NullableType().ToString());
        }

        /// <summary>
        /// Checks if the ITypeNode is a list of custom types
        /// e.g.
        /// [Book!] -> true
        /// [BookConnection] -> true    (pagination types also qualify as custom)
        /// </summary>
        public bool IsListOfCustomType(ITypeNode type)
        {
            return IsListType(type) && IsCustomType(InnerType(type));
        }

        /// <summary>
        /// Checks if the inner type of a given type is a custom type
        /// </summary>
        private bool IsInnerTypeCustom(ITypeNode type)
        {
            return IsCustomType(InnerType(type));
        }

        /// <summary>
        /// Check if list the elements of a list type are nullable
        /// e.g.
        /// Book    ->   false (not list)
        /// [Book]  ->   true
        /// [Book!] ->   false
        /// </summary>
        private static bool AreListElementsNullable(ITypeNode type)
        {
            if (IsListType(type))
            {
                return IsNullableType(type.NullableType().InnerType());
            }

            return false;
        }

        /// <summary>
        /// Get arguments from field and return a dictionary in [argName, argument] format
        /// </summary>
        private static Dictionary<string, InputValueDefinitionNode> GetArgumentsFromField(FieldDefinitionNode field)
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
        /// it is not a custom type nor a list type
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
        /// <remarks>
        /// Note that [String] is also considered non
        /// </remarks>
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
        private static bool GraphQLTypeEqualsColumnType(ITypeNode gqlType, Type columnType)
        {
            return GetGraphQLTypeForColumnType(columnType) == gqlType.NullableType().ToString();
        }

        /// <summary>
        /// Get the GraphQL type equivalent from ColumnType
        /// </summary>
        private static string GetGraphQLTypeForColumnType(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String:
                    return "String";
                case TypeCode.Int64:
                    return "Int";
                default:
                    throw new ArgumentException($"ColumnType {type} not handled by case. Please add a case resolving " +
                                                $"{type} to the appropriate GraphQL type");
            }
        }

        /// <summary>
        /// Get columns used in the primary key and foreign keys of the table
        /// </summary>
        private static IEnumerable<string> GetPkAndFkColumns(TableDefinition table)
        {
            List<string> columns = new();

            columns.AddRange(table.PrimaryKey);

            foreach (KeyValuePair<string, ForeignKeyDefinition> nameFKPair in table.ForeignKeys)
            {
                ForeignKeyDefinition foreignKey = nameFKPair.Value;
                columns.AddRange(foreignKey.ReferencingColumns);
            }

            return columns;
        }

        /// <summary>
        /// Get the config GraphQLTypes.Fields for a graphql schema type
        /// </summary>
        private IEnumerable<string> GetConfigFieldsForGqlType(ObjectTypeDefinitionNode type)
        {
            return _resolverConfig.GraphQLTypes[type.Name.Value].Fields.Keys;
        }

        /// <summary>
        /// Check that GraphQLType.Field has only a left foreign key
        /// </summary>
        private static bool HasLeftForeignKey(GraphQLField field)
        {
            return !string.IsNullOrEmpty(field.LeftForeignKey);
        }

        /// <summary>
        /// Check that GraphQLType.Field has only a right foreign key
        /// </summary>
        private static bool HasRightForeignKey(GraphQLField field)
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
        /// Gets a foreign key by name from the table
        /// </summary>
        /// <exception cref="KeyNotFoundException" />
        private ForeignKeyDefinition GetFkFromTable(string tableName, string fkName)
        {
            return _resolverConfig.DatabaseSchema!.Tables[tableName].ForeignKeys[fkName];
        }

        /// <summary>
        /// Gets mutation resolvers from config
        /// </summary>
        private List<MutationResolver> GetMutationResolvers()
        {
            return _resolverConfig.MutationResolvers;
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

        /// <summary>
        /// Check if GraphQL schema has mutations
        /// </summary>
        private bool SchemaHasMutations()
        {
            return _mutations.Count > 0;
        }

        /// <summary>
        /// Merges two dictionaries and returns the result
        /// </summary>
        /// <exception cref="ArgumentException"> If the dictionaries have overlapping keys </exception>
        private static Dictionary<K, V> MergeDictionaries<K, V>(IDictionary<K, V> d1, IDictionary<K, V> d2) where K : notnull
        {
            Dictionary<K, V> result = new();

            foreach (KeyValuePair<K, V> pair in d1)
            {
                result.Add(pair.Key, pair.Value);
            }

            foreach (KeyValuePair<K, V> pair in d2)
            {
                result.Add(pair.Key, pair.Value);
            }

            return result;
        }
    }
}
