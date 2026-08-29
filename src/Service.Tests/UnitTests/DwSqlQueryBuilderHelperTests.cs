// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.DWSQL)]
    public class DwSqlQueryBuilderHelperTests
    {
        [TestMethod]
        public void HasToOneOrNoRelation_NullStructureReturnsTrue()
        {
            Assert.IsTrue(InvokeHasToOneOrNoRelation(null, false));
        }

        [TestMethod]
        public void HasToOneOrNoRelation_EmptyAndNestedPointQueriesReturnTrue()
        {
            SqlQueryStructure child = CreateStructure(isList: false);
            SqlQueryStructure parent = CreateStructure(isList: true, ("child", child));

            Assert.IsTrue(InvokeHasToOneOrNoRelation(parent, false));
        }

        [TestMethod]
        public void HasToOneOrNoRelation_NestedListQueryReturnsFalse()
        {
            SqlQueryStructure child = CreateStructure(isList: true);
            SqlQueryStructure parent = CreateStructure(isList: true, ("children", child));

            Assert.IsFalse(InvokeHasToOneOrNoRelation(parent, false));
            Assert.IsFalse(InvokeHasToOneOrNoRelation(child, true));
        }

        [TestMethod]
        public void GenerateColumnsAsJsonObject_HandlesSingleAndMultipleColumns()
        {
            SqlQueryStructure single = CreateStructure(false);
            SetBaseProperty(single, "Columns", new List<LabelledColumn>
            {
                new("dbo", "books", "id", "id", "table0")
            });
            SqlQueryStructure multiple = CreateStructure(false);
            SetBaseProperty(multiple, "Columns", new List<LabelledColumn>
            {
                new("dbo", "books", "id", "id", "table0"),
                new("dbo", "books", "title", "bookTitle", "table0")
            });

            Assert.AreEqual("JSON_OBJECT('id': [id])", InvokeStatic<string>("GenerateColumnsAsJsonObject", single));
            Assert.AreEqual("JSON_OBJECT('id': [id],'bookTitle': [bookTitle])", InvokeStatic<string>("GenerateColumnsAsJsonObject", multiple));
        }

        [TestMethod]
        public void BuildProcedureParameterList_FormatsValuesAndHandlesEmptyInput()
        {
            Assert.AreEqual(string.Empty, InvokeStatic<string>("BuildProcedureParameterList", new Dictionary<string, object>()));
            Assert.AreEqual("@id = @param0, @name = @param1", InvokeStatic<string>(
                "BuildProcedureParameterList",
                new Dictionary<string, object> { ["id"] = "@param0", ["name"] = "@param1" }));
        }

        [TestMethod]
        public void Build_OptimizedSimpleQueryUsesJsonFunctions()
        {
            SqlQueryStructure structure = CreateBuildableStructure(isList: true);

            string query = new DwSqlQueryBuilder(enableNto1JoinOpt: true).Build(structure);

            StringAssert.Contains(query, "SELECT TOP 100 [table0].[id] AS [id]");
            StringAssert.Contains(query, "FROM [dbo].[books] AS [table0]");
            StringAssert.Contains(query, "FOR JSON PATH, INCLUDE_NULL_VALUES");
            Assert.IsFalse(query.Contains("STRING_AGG"));
        }

        [TestMethod]
        public void Build_UnoptimizedSimpleQueryUsesStringAggregation()
        {
            SqlQueryStructure structure = CreateBuildableStructure(isList: true);

            string query = new DwSqlQueryBuilder(enableNto1JoinOpt: false).Build(structure);

            StringAssert.Contains(query, "STRING_AGG");
            StringAssert.Contains(query, "FROM [dbo].[books] AS [table0]");
            Assert.IsFalse(query.Contains("FOR JSON PATH"));
        }

        [TestMethod]
        public void BuildWithJsonFunc_SubqueryWrapsColumnsAsJsonObject()
        {
            SqlQueryStructure structure = CreateBuildableStructure(isList: false);

            string query = InvokeInstance<string>(
                new DwSqlQueryBuilder(enableNto1JoinOpt: true),
                "BuildWithJsonFunc",
                structure,
                true);

            StringAssert.StartsWith(query, "SELECT JSON_OBJECT('id': [id])");
            StringAssert.Contains(query, "FROM (SELECT TOP 1");
            StringAssert.Contains(query, "AS [table0]");
        }

        [TestMethod]
        public void BuildFetchEnabledTriggersQuery_ReturnsTriggerMetadataQuery()
        {
            string query = new DwSqlQueryBuilder(enableNto1JoinOpt: true).BuildFetchEnabledTriggersQuery();

            StringAssert.Contains(query, "FROM sys.triggers");
            StringAssert.Contains(query, "ST.parent_id = object_id(@param0 + '.' + @param1)");
            StringAssert.Contains(query, "ST.is_disabled = 0");
        }

        private static bool InvokeHasToOneOrNoRelation(SqlQueryStructure? structure, bool isSubQuery) =>
            InvokeStatic<bool>("HasToOneOrNoRelation", structure, isSubQuery);

        private static T InvokeStatic<T>(string methodName, params object?[] arguments)
        {
            MethodInfo method = typeof(DwSqlQueryBuilder).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
            return (T)method.Invoke(null, arguments)!;
        }

        private static T InvokeInstance<T>(DwSqlQueryBuilder builder, string methodName, params object?[] arguments)
        {
            MethodInfo method = typeof(DwSqlQueryBuilder)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
            return (T)method.Invoke(builder, arguments)!;
        }

        private static SqlQueryStructure CreateStructure(bool isList, params (string Alias, SqlQueryStructure Query)[] joins)
        {
            SqlQueryStructure structure = (SqlQueryStructure)RuntimeHelpers.GetUninitializedObject(typeof(SqlQueryStructure));
            structure.IsListQuery = isList;
            Dictionary<string, SqlQueryStructure> joinQueries = new();
            foreach ((string alias, SqlQueryStructure query) in joins)
            {
                joinQueries.Add(alias, query);
            }

            typeof(SqlQueryStructure).GetField("<JoinQueries>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, joinQueries);
            return structure;
        }

        private static SqlQueryStructure CreateBuildableStructure(bool isList)
        {
            SqlQueryStructure structure = CreateStructure(isList);
            SourceDefinition sourceDefinition = new();
            sourceDefinition.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int) });
            DatabaseTable databaseTable = new("dbo", "books") { TableDefinition = sourceDefinition };
            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(x => x.GetSourceDefinition("Book")).Returns(sourceDefinition);

            SetField(structure, "EntityName", "Book");
            SetField(structure, "MetadataProvider", metadataProvider.Object);
            SetField(structure, "DatabaseObject", databaseTable);
            SetField(structure, "SourceAlias", "table0");
            SetField(structure, "Columns", new List<LabelledColumn>
            {
                new("dbo", "books", "id", "id", "table0")
            });
            SetField(structure, "Predicates", new List<Predicate>());
            SetField(structure, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            SetField(structure, "Joins", new List<SqlJoinStructure>());
            SetField(structure, "FilterPredicates", string.Empty);
            SetField(structure, "OrderByColumns", new List<OrderByColumn>());
            SetField(structure, "PaginationMetadata", new PaginationMetadata(structure));
            SetField(structure, "GroupByMetadata", new GroupByMetadata());
            typeof(SqlQueryStructure).GetField("_limit", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, (uint?)100);
            return structure;
        }

        private static void SetField<T>(SqlQueryStructure structure, string name, T value)
        {
            for (System.Type? type = typeof(SqlQueryStructure); type is not null; type = type.BaseType)
            {
                FieldInfo? field = type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field is not null)
                {
                    field.SetValue(structure, value);
                    return;
                }
            }

            Assert.Fail($"Could not find backing field for {name}.");
        }

        private static void SetBaseProperty<T>(SqlQueryStructure structure, string name, T value)
        {
            typeof(BaseQueryStructure).GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(structure, value);
        }
    }
}
