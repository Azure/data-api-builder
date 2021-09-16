using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.ComponentModel;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

using Newtonsoft.Json.Linq;

namespace Demo
{
    public class QueryMetadata
    {
        Dictionary<string, string> columns;
        public List<string> conditions;
        Dictionary<string, QueryMetadata> joinQueries;
        string tableName;
        string tableAlias;
        // public List<string> conditions;
        public QueryMetadata(string tableName, IResolverContext ctx) : this(
            tableName,
            "0",
            ctx.Selection.SyntaxNode.SelectionSet.Selections
        )
        {
            // TODO: hopefully useful for later
            ObjectField schema = ctx.Schema.QueryType.Fields.FirstOrDefault((field) => field.Name == "book");
        }

        public QueryMetadata(string tableName, string tableAlias, IReadOnlyList<ISelectionNode> selections)
        {
            columns = new();
            joinQueries = new();
            conditions = new();

            this.tableName = tableName;
            this.tableAlias = tableAlias;
            AddFields(selections);
        }
        static string Table(string name, string alias)
        {
            return $"{escapeIdentifier(name)} AS {escapeIdentifier(alias)}";
        }

        static string QualifiedColumn(string tableAlias, string columnName)
        {
            return $"{escapeIdentifier(tableAlias)}.{escapeIdentifier(columnName)}";
        }

        void AddFields(IReadOnlyList<ISelectionNode> Selections)
        {
            int subselectionCount = 0;
            foreach (var node in Selections)
            {
                FieldNode field = node as FieldNode;
                string fieldName = field.Name.Value;

                if (field.SelectionSet == null)
                {
                    // TODO: Get column name from JSON config
                    string columnName = field.Name.Value;
                    string column = QualifiedColumn(tableAlias, columnName);
                    columns.Add(fieldName, column);
                }
                else
                {
                    string subtableName = "authors";
                    string subtableAlias = $"{tableAlias}.{subselectionCount}";
                    // TODO: Get find fieldtype in schema and use JSON config to determine join column(s)
                    string leftColumnName = "author_id";
                    string rightColumnName = "id";
                    string leftColumn = QualifiedColumn(tableAlias, leftColumnName);
                    string rightColumn = QualifiedColumn(subtableAlias, rightColumnName);

                    QueryMetadata subquery = new(subtableName, subtableAlias, field.SelectionSet.Selections);
                    subquery.conditions.Add($"{leftColumn} = {rightColumn}");
                    string subqueryAlias = $"q{subtableAlias}";
                    joinQueries.Add(subqueryAlias, subquery);
                    columns.Add(fieldName, $"{escapeIdentifier(subqueryAlias)}.data");
                }
            }
        }

        static string escapeIdentifier(string ident)
        {
            // TODO: Make this safe by using SQL/PG library provided versions
            return $"\"{ident}\"";
        }

        static string escapeString(string str)
        {
            // TODO: Make this safe by using SQL/PG library provided versions
            return $"'{str}'";
        }

        public override string ToString()
        {
            var buildObjectArgs = String.Join(", ", columns.Select(x => $"{escapeString(x.Key)}, ({x.Value})"));
            string fromPart = Table(tableName, tableAlias);
            fromPart += String.Join("", joinQueries.Select(x => $"LEFT OUTER JOIN LATERAL ({x.Value.ToString()}) AS {escapeIdentifier(x.Key)} ON TRUE"));
            string query = $"SELECT json_build_object({buildObjectArgs}) AS data FROM {fromPart}";
            if (conditions.Count() > 0)
            {
                query += $" WHERE {String.Join(" AND ", conditions)}";
            }
            return query;
        }

    }

    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddGraphQLServer()
                .AddDocumentFromString(@"
                    type Query {
                      books: [Book]
                    }

                    type Book {
                      id: ID
                      title: String
                      author: Author
                      author_id: ID
                    }

                    type Author {
                        id: ID
                        name: String
                        birthdate: String
                    }
                ")

                .AddResolver("Query", "books", (ctx) =>
                {
                    // Replace below with query
                    var metadata = new QueryMetadata("books", ctx);
                    JArray result = new();
                    result.Add(JObject.FromObject(new
                    {
                        id = 1,
                        title = metadata.ToString(),
                        author_id = 123,
                        author = new
                        {
                            id = 123,
                            name = "Jelte",
                            birtdate = "2001-01-01"
                        }
                    }));
                    result.Add(JObject.FromObject(new
                    {
                        id = 2,
                        title = metadata.ToString(),
                        author_id = 124,
                        author = new
                        {
                            id = 124,
                            name = "Aniruddh",
                            birtdate = "2002-01-01"
                        }
                    }));

                    return result;
                })
                .AddResolver("Book", "id", (ctx) =>
                {
                    return (string)ctx.Parent<JObject>()["id"];
                })
                .AddResolver("Book", "author", (ctx) =>
                {
                    return ctx.Parent<JObject>()["author"];
                })
                .AddResolver("Book", "author_id", (ctx) =>
                {
                    return (int)ctx.Parent<JObject>()["author_id"];
                })
                .AddResolver("Author", "id", (ctx) =>
                {
                    return (int)ctx.Parent<JObject>()["id"];
                })
                .AddResolver("Author", "name", (ctx) =>
                {
                    return (string)ctx.Parent<JObject>()["name"];
                })
                .AddResolver("Author", "birthdate", (ctx) =>
                {
                    return (string)ctx.Parent<JObject>()["birtdate"];
                })
            ;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app
                .UseRouting()
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapGraphQL();
                });
        }
    }
}
