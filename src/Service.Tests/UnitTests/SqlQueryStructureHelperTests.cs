// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class SqlQueryStructureHelperTests
    {
        /// <summary>
        /// Verifies point queries are limited to one row while list or paginated shapes retain the configured limit.
        /// </summary>
        [DataTestMethod]
        [DataRow(false, false, 25u, 1u, DisplayName = "Point query forces a one-row limit")]
        [DataRow(true, false, 25u, 25u, DisplayName = "List query retains the configured limit")]
        [DataRow(false, true, 25u, 25u, DisplayName = "Paginated query retains the configured limit")]
        public void Limit_ReflectsQueryAndPaginationShape(bool isList, bool isPaginated, uint configured, uint expected)
        {
            SqlQueryStructure structure = CreateStructure();
            structure.IsListQuery = isList;
            typeof(SqlQueryStructure).GetField("_limit", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, configured);
            structure.PaginationMetadata = new PaginationMetadata(structure) { IsPaginated = isPaginated };

            Assert.AreEqual(expected, structure.Limit());
        }

        [TestMethod]
        public void Limit_ListQueryWithNullLimit_ReturnsNull()
        {
            SqlQueryStructure structure = CreateStructure();
            structure.IsListQuery = true;
            typeof(SqlQueryStructure).GetField("_limit", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, null);
            structure.PaginationMetadata = new PaginationMetadata(structure);

            Assert.IsNull(structure.Limit());
        }

        [TestMethod]
        public void IsSubqueryColumn_EvaluatesTableAliasAgainstJoinQueries()
        {
            SqlQueryStructure structure = CreateStructure();
            structure.JoinQueries.Add("joined", CreateStructure());

            Assert.IsFalse(structure.IsSubqueryColumn(new Column("dbo", "books", "id")));
            Assert.IsFalse(structure.IsSubqueryColumn(new Column("dbo", "books", "id", "missing")));
            Assert.IsTrue(structure.IsSubqueryColumn(new Column("dbo", "books", "id", "joined")));
        }

        [TestMethod]
        public void AddCacheControlOptions_CopiesRequestHeader()
        {
            SqlQueryStructure structure = CreateStructure();
            HeaderDictionary headers = new() { ["Cache-Control"] = "no-store" };

            GetPrivateMethod("AddCacheControlOptions").Invoke(structure, new object[] { headers });

            Assert.AreEqual("no-store", structure.CacheControlOption);
        }

        /// <summary>
        /// Verifies each pagination selection sets only its corresponding requested-output flag.
        /// </summary>
        [TestMethod]
        public void ProcessPaginationFields_SetsEveryRequestedFlag()
        {
            SqlQueryStructure structure = CreateStructure();
            ISelectionNode[] selections =
            {
                new FieldNode(QueryBuilder.PAGINATION_FIELD_NAME),
                new FieldNode(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME),
                new FieldNode(QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME),
                new FieldNode(QueryBuilder.GROUP_BY_FIELD_NAME)
            };

            GetPrivateMethod("ProcessPaginationFields").Invoke(structure, new object[] { selections });

            Assert.IsTrue(structure.PaginationMetadata.RequestedItems);
            Assert.IsTrue(structure.PaginationMetadata.RequestedEndCursor);
            Assert.IsTrue(structure.PaginationMetadata.RequestedHasNextPage);
            Assert.IsTrue(structure.PaginationMetadata.RequestedGroupBy);
        }

        [TestMethod]
        public void AddGraphQLFields_FragmentSpreadWithoutContextThrows()
        {
            SqlQueryStructure structure = CreateStructure();
            ISelectionNode[] selections =
            {
                new FragmentSpreadNode(null, new NameNode("BookFields"), System.Array.Empty<DirectiveNode>())
            };

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                GetPrivateMethod("AddGraphQLFields").Invoke(structure, new object?[] { selections, null }));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        [TestMethod]
        public void AddGraphQLFields_InlineFragmentSkipsIntrospectionField()
        {
            SqlQueryStructure structure = CreateStructure();
            InlineFragmentNode fragment = new(
                location: null,
                typeCondition: null,
                directives: System.Array.Empty<DirectiveNode>(),
                selectionSet: new SelectionSetNode(new ISelectionNode[] { new FieldNode("__typename") }));

            GetPrivateMethod("AddGraphQLFields").Invoke(
                structure,
                new object?[] { new ISelectionNode[] { fragment }, null });
        }

        [TestMethod]
        public void AddGraphQLFields_UnsupportedSelectionThrows()
        {
            SqlQueryStructure structure = CreateStructure();
            Mock<ISelectionNode> selection = new();
            selection.SetupGet(node => node.Kind).Returns(SyntaxKind.Directive);

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                GetPrivateMethod("AddGraphQLFields").Invoke(
                    structure,
                    new object?[] { new[] { selection.Object }, null }));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        [TestMethod]
        public void ProcessGroupByFieldSelections_NullSelectionReturns()
        {
            SqlQueryStructure structure = CreateStructure();

            GetPrivateMethod("ProcessGroupByFieldSelections").Invoke(
                structure,
                new object[] { new FieldNode("fields"), new HashSet<string>() });
        }

        [TestMethod]
        public void ProcessGroupByFieldSelections_MismatchedFieldThrows()
        {
            SqlQueryStructure structure = CreateStructure();
            FieldNode fields = new FieldNode("fields").WithSelectionSet(
                new SelectionSetNode(new ISelectionNode[] { new FieldNode("missing") }));

            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                GetPrivateMethod("ProcessGroupByFieldSelections").Invoke(
                    structure,
                    new object[] { fields, new HashSet<string> { "id" } }));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        private static SqlQueryStructure CreateStructure()
        {
            SqlQueryStructure structure = (SqlQueryStructure)RuntimeHelpers.GetUninitializedObject(typeof(SqlQueryStructure));
            SetAutoProperty(structure, "JoinQueries", new Dictionary<string, SqlQueryStructure>());
            structure.PaginationMetadata = new PaginationMetadata(structure);
            return structure;
        }

        private static void SetAutoProperty<T>(SqlQueryStructure structure, string propertyName, T value)
        {
            typeof(SqlQueryStructure).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, value);
        }

        private static MethodInfo GetPrivateMethod(string methodName) =>
            typeof(SqlQueryStructure).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
    }
}
