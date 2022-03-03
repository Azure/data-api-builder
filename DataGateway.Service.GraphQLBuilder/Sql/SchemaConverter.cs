using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.GraphQLBuilder.Sql
{
    internal record SqlColumnInfo(string Name, string DataType, bool Nullable);

    public static class SchemaConverter
    {
        public static async Task<DocumentNode> FromSchema(DbConnection connection)
        {
            await connection.OpenAsync();

            DataTable schema = await connection.GetSchemaAsync("Columns", new[] { null, "dbo" });

            Dictionary<string, List<SqlColumnInfo>> dict = new();

            foreach (DataRow row in schema.Rows)
            {
                string tableName = (string)row[2];
                if (!dict.ContainsKey(tableName))
                {
                    dict[tableName] = new List<SqlColumnInfo>();
                }

                dict[tableName].Add(new((string)row[3], (string)row[7], ((string)row[6]) != "NO"));
            }

            List<ObjectTypeDefinitionNode> typeDefs = new();

            foreach ((string tableName, List<SqlColumnInfo> columns) in dict)
            {
                typeDefs.Add(new(
                    null,
                    new(tableName),
                    null,
                    new List<DirectiveNode>(new[] { new DirectiveNode("model") }),
                    new List<NamedTypeNode>(),
                    columns.Select(col =>
                    new FieldDefinitionNode(
                        null,
                        new(col.Name),
                        null,
                        new List<InputValueDefinitionNode>(),
                        ColumnTypeToGraphQLType(col),
                        new List<DirectiveNode>()))
                    .ToList()));
            }

            return new(typeDefs);
        }

        private static ITypeNode ColumnTypeToGraphQLType(SqlColumnInfo col)
        {
            ITypeNode node = col.DataType switch
            {
                "bigint" => new IntType().ToTypeNode(),
                "varchar" => new StringType().ToTypeNode(),
                "bit" => new BooleanType().ToTypeNode(),
                _ => throw new NotSupportedException($"Unable to parse type {col.DataType} to GraphQL"),
            };

            if (!col.Nullable)
            {
                node = new NonNullTypeNode((INullableTypeNode)node);
            }

            return node;
        }
    }
}
