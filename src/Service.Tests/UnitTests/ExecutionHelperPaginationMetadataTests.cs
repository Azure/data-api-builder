// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Reflection;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate;
using HotChocolate.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Tests that <see cref="ExecutionHelper"/> keeps the pagination metadata of each field in the
    /// request context apart. Resolvers of sibling fields can run in any order (Hot Chocolate
    /// runs them through the middleware pipeline when a field has an authorization directive),
    /// so a sibling writing its metadata must not overwrite the metadata of another field.
    /// </summary>
    [TestClass]
    public class ExecutionHelperPaginationMetadataTests
    {
        /// <summary>
        /// Stores the metadata of two sibling list fields before the fields nested in
        /// the first one are resolved, then verifies each field reads its own metadata.
        /// </summary>
        [TestMethod]
        public void GetMetadataObjectField_SiblingListsBelowObjectRoot_ReadOwnMetadata()
        {
            Dictionary<string, object?> contextData = new();
            Path root = Path.Root.Append("organization_by_pk");
            Path addressItems = root.Append("addresses").Append("items");
            Path emailItems = root.Append("emails").Append("items");
            PaginationMetadata addressMetadata = PaginationMetadata.MakeEmptyPaginationMetadata();
            PaginationMetadata emailMetadata = PaginationMetadata.MakeEmptyPaginationMetadata();

            SetNewMetadataChildren(addressItems, addressMetadata, contextData);
            SetNewMetadataChildren(emailItems, emailMetadata, contextData);

            Assert.AreSame(addressMetadata, GetMetadataObjectField(addressItems.Append(0).Append("addressTypes"), contextData));
            Assert.AreSame(emailMetadata, GetMetadataObjectField(emailItems.Append(0).Append("emailTypes"), contextData));
        }

        /// <summary>
        /// Fields with the same name below different sibling relationships of a
        /// list must not share metadata, even though their path depth is the same.
        /// </summary>
        [TestMethod]
        public void GetMetadataObjectField_SameFieldNameBelowSiblingRelationships_ReadOwnMetadata()
        {
            Dictionary<string, object?> contextData = new();
            Path bookItem = Path.Root.Append("books").Append("items").Append(0);
            Path authorBooks = bookItem.Append("authors").Append("items").Append(0).Append("books").Append("items");
            Path reviewBooks = bookItem.Append("reviews").Append("items").Append(0).Append("books").Append("items");
            PaginationMetadata authorBooksMetadata = PaginationMetadata.MakeEmptyPaginationMetadata();
            PaginationMetadata reviewBooksMetadata = PaginationMetadata.MakeEmptyPaginationMetadata();

            SetNewMetadataChildren(authorBooks, authorBooksMetadata, contextData);
            SetNewMetadataChildren(reviewBooks, reviewBooksMetadata, contextData);

            Assert.AreSame(authorBooksMetadata, GetMetadataObjectField(authorBooks.Append(0).Append("publishers"), contextData));
            Assert.AreSame(reviewBooksMetadata, GetMetadataObjectField(reviewBooks.Append(0).Append("publishers"), contextData));
        }

        /// <summary>
        /// All items of a list share the pagination metadata of the
        /// list, regardless of the item index a field is nested under.
        /// </summary>
        [TestMethod]
        public void GetMetadataObjectField_DifferentItemsOfList_ReadSameMetadata()
        {
            Dictionary<string, object?> contextData = new();
            Path bookItems = Path.Root.Append("books").Append("items");
            PaginationMetadata itemsMetadata = PaginationMetadata.MakeEmptyPaginationMetadata();

            SetNewMetadataChildren(bookItems, itemsMetadata, contextData);

            Assert.AreSame(itemsMetadata, GetMetadataObjectField(bookItems.Append(0).Append("publishers"), contextData));
            Assert.AreSame(itemsMetadata, GetMetadataObjectField(bookItems.Append(7).Append("publishers"), contextData));
        }

        private static void SetNewMetadataChildren(Path path, PaginationMetadata metadata, Dictionary<string, object?> contextData)
        {
            InvokeExecutionHelper("SetNewMetadataChildren", CreateContext(path, contextData), metadata);
        }

        private static object? GetMetadataObjectField(Path path, Dictionary<string, object?> contextData)
        {
            return InvokeExecutionHelper("GetMetadataObjectField", CreateContext(path, contextData));
        }

        private static object? InvokeExecutionHelper(string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(ExecutionHelper).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
            return method.Invoke(null, arguments);
        }

        private static IResolverContext CreateContext(Path path, Dictionary<string, object?> contextData)
        {
            Mock<IResolverContext> context = new();
            context.SetupGet(x => x.Path).Returns(path);
            context.SetupGet(x => x.ContextData).Returns(contextData);
            return context.Object;
        }
    }
}
