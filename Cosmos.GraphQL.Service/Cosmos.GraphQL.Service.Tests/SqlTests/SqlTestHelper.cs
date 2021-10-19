using Cosmos.GraphQL.Service.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Tests.Sql
{
    class SqlTestHelper
    {
        public static readonly string GraphQLSchema = @"
                type Query {
                    characterList: [Character]
                    characterById (id : ID!): Character
                }
                type Character {
                    id : ID,
                    name : String,
                    type: String,
                    homePlanet: Int,
                    primaryFunction: String
                }
                ";

        public static readonly string CharacterListResolver = "{\r\n \"id\": \"characterList\",\r\n \"parametrizedQuery\": \"SELECT id, name, type, homePlanet, primaryFunction FROM character\"\r\n }";
        public static readonly string CharacterByIdResolver = "{\r\n \"id\": \"characterById\",\r\n \"parametrizedQuery\": \"SELECT id, name, type, homePlanet, primaryFunction FROM character WHERE id = @id\"\r\n}";

        public static GraphQLQueryResolver GetQueryResolverJson(string rawResolverText)
        {
            return JsonConvert.DeserializeObject<GraphQLQueryResolver>(rawResolverText);
        }
    }
}
