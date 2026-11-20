// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class CosmosQueryStructureHelperTests
    {
        [TestMethod]
        public void GetTableAlias_IncrementsCounter()
        {
            CosmosQueryStructure structure = CreateStructure();

            Assert.AreEqual("table0", structure.GetTableAlias());
            Assert.AreEqual("table1", structure.GetTableAlias());
        }

        [TestMethod]
        public void GenerateQueryColumns_ExpandsFragmentSpreadsAndInlineFragments()
        {
            DocumentNode document = Utf8GraphQLParser.Parse(@"
                query {
                    book {
                        id
                        ...BookFields
                        ... on Book { title }
                    }
                }
                fragment BookFields on Book { name }");
            OperationDefinitionNode operation = document.Definitions.OfType<OperationDefinitionNode>().Single();
            FieldNode book = operation.SelectionSet.Selections.OfType<FieldNode>().Single();
            MethodInfo method = typeof(CosmosQueryStructure).GetMethod(
                "GenerateQueryColumns",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            IEnumerable<LabelledColumn> columns = (IEnumerable<LabelledColumn>)method.Invoke(
                null,
                new object[] { book.SelectionSet!, document, "c" })!;

            CollectionAssert.AreEqual(new[] { "id", "name", "title" }, columns.Select(column => column.Label).ToArray());
        }

        [TestMethod]
        public void ProcessGraphQLOrderByArg_SkipsNullAndMapsDescending()
        {
            CosmosQueryStructure structure = CreateStructure();
            List<ObjectFieldNode> orderBy = new()
            {
                new("ignored", NullValueNode.Default),
                new("title", new EnumValueNode("DESC")),
                new("id", new EnumValueNode("ASC"))
            };
            MethodInfo method = typeof(CosmosQueryStructure).GetMethod(
                "ProcessGraphQLOrderByArg",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            List<OrderByColumn> columns = (List<OrderByColumn>)method.Invoke(structure, new object[] { orderBy })!;

            Assert.AreEqual(2, columns.Count);
            Assert.AreEqual(OrderBy.DESC, columns[0].Direction);
            Assert.AreEqual(OrderBy.ASC, columns[1].Direction);
        }

        private static CosmosQueryStructure CreateStructure()
        {
            CosmosQueryStructure structure =
                (CosmosQueryStructure)RuntimeHelpers.GetUninitializedObject(typeof(CosmosQueryStructure));
            structure.TableCounter = new IncrementingInteger();
            typeof(CosmosQueryStructure).GetField("_containerAlias", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, CosmosQueryStructure.COSMOSDB_CONTAINER_DEFAULT_ALIAS);
            return structure;
        }
    }
}
