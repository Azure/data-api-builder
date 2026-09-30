// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class SqlQueryStructuresModelTests
    {
        [TestMethod]
        public void LabelledColumn_EqualityHandlesNullIdentityAndValues()
        {
            LabelledColumn column = new("dbo", "books", "id", "book_id");
            LabelledColumn equal = new("dbo", "books", "id", "book_id");

            Assert.IsFalse(column.Equals((LabelledColumn?)null));
            Assert.IsFalse(column.Equals((object?)null));
            Assert.IsTrue(column.Equals(column));
            Assert.IsTrue(column.Equals((object)column));
            Assert.IsTrue(column.Equals(equal));
            Assert.IsFalse(column.Equals(new LabelledColumn("dbo", "books", "id", "other")));
            Assert.IsFalse(column.Equals(new object()));
            Assert.AreNotEqual(0, column.GetHashCode());
            Assert.AreEqual(column.GetHashCode(), equal.GetHashCode(), "Equal columns must have equal hash codes.");
        }

        [TestMethod]
        public void PredicateOperand_NullConstructorsThrow()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new PredicateOperand((Column?)null));
            Assert.ThrowsException<ArgumentNullException>(() => new PredicateOperand((BaseQueryStructure?)null));
            Assert.ThrowsException<ArgumentNullException>(() => new PredicateOperand((string?)null));
            Assert.ThrowsException<ArgumentNullException>(() => new PredicateOperand((Predicate?)null));
        }

        [TestMethod]
        public void PredicateOperand_StringAndPredicateAccessorsReflectStoredType()
        {
            PredicateOperand text = new("value");
            Predicate predicate = new(null, PredicateOperation.EXISTS, text);
            PredicateOperand nested = new(predicate);

            Assert.AreEqual("value", text.AsString());
            Assert.IsNull(text.AsColumn());
            Assert.IsNull(text.AsPredicate());
            Assert.IsFalse(text.IsPredicate());
            Assert.AreSame(predicate, nested.AsPredicate());
            Assert.IsTrue(nested.IsPredicate());
        }
    }
}
