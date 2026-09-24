// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class CosmosQueryBuilderHelperTests
    {
        [TestMethod]
        public void BuildPaginationPredicate_ReturnsEmptyString()
        {
            TestCosmosQueryBuilder builder = new();

            Assert.AreEqual(string.Empty, builder.BuildPaginationPredicate(null));
        }

        [TestMethod]
        public void BuildPredicate_NullAndUnknownOperationThrow()
        {
            TestCosmosQueryBuilder builder = new();

            Assert.ThrowsException<ArgumentNullException>(() => builder.BuildPredicate(null));
            Assert.ThrowsException<ArgumentException>(() => builder.BuildOperation(PredicateOperation.None));
            Assert.ThrowsException<ArgumentException>(() => builder.BuildOperation(PredicateOperation.IN));
        }

        [TestMethod]
        public void ResolveOperand_NullAndEmptyOperandThrow()
        {
            TestCosmosQueryBuilder builder = new();
            PredicateOperand emptyOperand = (PredicateOperand)RuntimeHelpers.GetUninitializedObject(typeof(PredicateOperand));

            Assert.ThrowsException<ArgumentNullException>(() => builder.Resolve(null));
            Assert.ThrowsException<ArgumentException>(() => builder.Resolve(emptyOperand));
        }

        [TestMethod]
        public void ResolveOperand_CosmosQueryStructureBuildsNestedQuery()
        {
            TestCosmosQueryBuilder builder = new();
            CosmosQueryStructure structure = CreateStructure();

            string query = builder.Resolve(new PredicateOperand(structure));

            Assert.AreEqual("SELECT c.id FROM c", query);
        }

        [TestMethod]
        public void BuildExistsQueryForCosmos_WithoutPredicatesOmitsWhereClause()
        {
            Assert.AreEqual(
                "EXISTS (SELECT VALUE 1 FROM item IN c.items )",
                CosmosQueryBuilder.BuildExistsQueryForCosmos("item IN c.items", null));
        }

        private static CosmosQueryStructure CreateStructure()
        {
            CosmosQueryStructure structure =
                (CosmosQueryStructure)RuntimeHelpers.GetUninitializedObject(typeof(CosmosQueryStructure));
            SetField(structure, "Columns", new List<LabelledColumn>
            {
                new(string.Empty, "c", "id", "id")
            });
            SetField(structure, "Predicates", new List<Predicate>());
            SetField(structure, "DbPolicyPredicatesForOperations", new Dictionary<EntityActionOperation, string?>());
            SetField(structure, "OrderByColumns", new List<OrderByColumn>());
            return structure;
        }

        private static void SetField(object instance, string propertyName, object value)
        {
            for (Type? type = instance.GetType(); type is not null; type = type.BaseType)
            {
                FieldInfo? field = type.GetField(
                    $"<{propertyName}>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field is not null)
                {
                    field.SetValue(instance, value);
                    return;
                }
            }

            Assert.Fail($"Unable to find backing field for '{propertyName}'.");
        }

        private sealed class TestCosmosQueryBuilder : CosmosQueryBuilder
        {
            public string BuildPaginationPredicate(KeysetPaginationPredicate? predicate) => Build(predicate);

            public string BuildOperation(PredicateOperation operation) => Build(operation);

            public string BuildPredicate(Predicate? predicate) => Build(predicate);

            public string Resolve(PredicateOperand? operand) => ResolveOperand(operand);
        }
    }
}
