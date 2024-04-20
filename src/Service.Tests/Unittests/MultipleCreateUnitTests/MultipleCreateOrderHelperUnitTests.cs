// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    public abstract class MultipleCreateOrderHelperUnitTests : SqlTestBase
    {

        #region Order determination test for relationships not backed by an FK constraint
        /// <summary>
        /// Test to validate that when no FK constraint exists between the source and target entity and all the relationship fields
        /// in the source/target entity are non-autogenerated, we cannot determine which entity should be considered as the referencing entity
        /// when the input for both the source and the target entity contain value for one or more relationship fields.
        ///
        /// Here, the relationship between entities: 'User_NonAutogenRelationshipColumn' and 'UserProfile_NonAutogenRelationshipColumn'
        /// is defined as User_NonAutogenRelationshipColumn(username) -> UserProfile_NonAutogenRelationshipColumn(username)
        /// where the field 'username' is non-autogenerated in both the entities.
        /// </summary>
        [TestMethod]
        public void ValidateIndeterministicReferencingEntityForNonAutogenRelationshipColumns()
        {
            IMiddlewareContext context = SetupMiddlewareContext();
            string sourceEntityName = "User_NonAutogenRelationshipColumn";
            string targetEntityName = "UserProfile";

            // Setup column input in source entity.
            Dictionary<string, IValueNode> columnDataInSourceBody = new()
            {
                { "username", new StringValueNode("DAB") },
                { "email", new StringValueNode("dab@microsoft.com") }
            };

            // Setup column input for target entity.
            ObjectValueNode targetNodeValue = new();
            List<ObjectFieldNode> fields = new()
            {
                new ObjectFieldNode("username", "DAB"),
                new ObjectFieldNode("profilepictureurl", "dab/profilepicture"),
                new ObjectFieldNode("userid", 1)
            };
            targetNodeValue = targetNodeValue.WithFields(fields);

            // Since the non-autogenerated relationship field 'username' is present in the input for both
            // the source and target entity, assert that we get the expected exception.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => MultipleCreateOrderHelper.GetReferencingEntityName(
                relationshipName: "UserProfile_NonAutogenRelationshipColumn",
                context: context,
                sourceEntityName: sourceEntityName,
                targetEntityName: targetEntityName,
                metadataProvider: _sqlMetadataProvider,
                columnDataInSourceBody: columnDataInSourceBody,
                targetNodeValue: targetNodeValue,
                nestingLevel: 1));
        }

        /// <summary>
        /// Test to validate that when no FK constraint exists between the source and target entity and all the relationship fields in the source/target entity
        /// are non-autogenerated, either the input for the source or the target entity should contain the value for the relationship fields but not both.
        /// The entity which contains the values for all relationship fields is considered as the referenced entity, and the other
        /// entity is considered as the referencing entity.
        ///
        /// Here, the relationship between entities: 'User_NonAutogenRelationshipColumn' and 'UserProfile_NonAutogenRelationshipColumn'
        /// is defined as User_NonAutogenRelationshipColumn(username) -> UserProfile_NonAutogenRelationshipColumn(username)
        /// where the field 'username' is non-autogenerated in both the entities.
        /// </summary>
        [TestMethod]
        public void ValidateDeterministicReferencingEntityForNonAutogenRelationshipColumns()
        {
            // Test 1: The value for relationship field 'username' is present in the input for the source entity.
            // Expected referencing entity: UserProfile (target entity).
            // The complete graphQL mutation looks as follows:
            //  mutation{
            //          createUser_NonAutogenRelationshipColumn(item: {
            //            username: "DAB",
            //            email: "dab@microsoft.com",
            //            UserProfile_NonAutogenRelationshipColumn: {
            //              profilepictureurl: "dab/profilepicture",
            //              userid: 10
            //            }
            //          }){
            //            <selection_set>   
            //          }
            //      }

            IMiddlewareContext context = SetupMiddlewareContext();
            string sourceEntityName = "User_NonAutogenRelationshipColumn";
            string targetEntityName = "UserProfile";

            // Setup column input in source entity.
            Dictionary<string, IValueNode> columnDataInSourceBody = new()
            {
                { "username", new StringValueNode("DAB") },
                { "email", new StringValueNode("dab@microsoft.com") }
            };

            // Setup column input in source entity.
            ObjectValueNode targetNodeValue = new();
            List<ObjectFieldNode> fields = new()
            {
                new ObjectFieldNode("profilepictureurl", "dab/profilepicture"),
                new ObjectFieldNode("userid", 10)
            };
            targetNodeValue = targetNodeValue.WithFields(fields);

            // Get the referencing entity name. Since the source entity contained the value for relationship field,
            // it act as the referenced entity, and the target entity act as the referencing entity.
            // To provide users with a more helpful message in case of an exception, in addition to other relevant info,
            // the nesting level is also returned to quicky identify the level in the input request where error has occurred.
            // Since, in this test, there is only one level of nesting, the nestingLevel param is set to 1.
            string referencingEntityName = MultipleCreateOrderHelper.GetReferencingEntityName(
                relationshipName: "UserProfile_NonAutogenRelationshipColumn",
                context: context,
                sourceEntityName: sourceEntityName,
                targetEntityName: targetEntityName,
                metadataProvider: _sqlMetadataProvider,
                columnDataInSourceBody: columnDataInSourceBody,
                targetNodeValue: targetNodeValue,
                nestingLevel: 1);
            Assert.AreEqual(targetEntityName, referencingEntityName);

            // Test 2: The value for relationship field 'username' is present in the input for the target entity.
            // Expected referencing entity: User_NonAutogenRelationshipColumn (source entity).
            // The complete graphQL mutation looks as follows:
            //  mutation{
            //          createUser_NonAutogenRelationshipColumn(item: {
            //            email: "dab@microsoft.com",
            //            UserProfile_NonAutogenRelationshipColumn: {
            //              profilepictureurl: "dab/profilepicture",
            //              userid: 10,
            //              username: "DAB"
            //            }
            //          }){
            //            <selection_set>   
            //          }
            //      }

            // Setup column input in source entity.
            columnDataInSourceBody = new()
            {
                { "email", new StringValueNode("dab@microsoft.com") }
            };

            // Setup column input in target entity.
            targetNodeValue = new();
            fields = new()
            {
                new ObjectFieldNode("profilepictureurl", "dab/profilepicture"),
                new ObjectFieldNode("userid", 10),
                new ObjectFieldNode("username", "DAB")
            };
            targetNodeValue = targetNodeValue.WithFields(fields);

            // Get the referencing entity name.
            referencingEntityName = MultipleCreateOrderHelper.GetReferencingEntityName(
                relationshipName: "UserProfile_NonAutogenRelationshipColumn",
                context: context,
                sourceEntityName: sourceEntityName,
                targetEntityName: targetEntityName,
                metadataProvider: _sqlMetadataProvider,
                columnDataInSourceBody: columnDataInSourceBody,
                targetNodeValue: targetNodeValue,
                nestingLevel: 1);
            // Since the target entity contained the value for relationship field,
            // it act as the referenced entity, and the source entity act as the referencing entity.
            Assert.AreEqual(sourceEntityName, referencingEntityName);
        }

        /// <summary>
        /// Test to validate that when no FK constraint exists between the source and target entity and the relationship fields
        /// in the source/target entity  contain an autogenerated field, it is not possible to determine a referencing entity.
        /// This is because we cannot provide a value for insertion for an autogenerated relationship field in any of the entity.
        /// Hence, none of the entity can act as a referencing/referenced entity.
        ///
        /// Here, the relationship between entities: 'User_AutogenRelationshipColumn' and 'UserProfile_AutogenRelationshipColumn'
        /// is defined as User_AutogenRelationshipColumn(userid) -> UserProfile_AutogenRelationshipColumn(profileid)
        /// where both the relationships fields are autogenerated in the respective entities.
        /// </summary>
        [TestMethod]
        public void ValidateIndeterministicReferencingEntityForAutogenRelationshipColumns()
        {
            IMiddlewareContext context = SetupMiddlewareContext();
            string sourceEntityName = "User_AutogenRelationshipColumn";
            string targetEntityName = "UserProfile";

            // Setup column input for source entity.
            Dictionary<string, IValueNode> columnDataInSourceBody = new()
            {
                { "email", new StringValueNode("dab@microsoft.com") }
            };

            // Setup column input for target entity.
            ObjectValueNode targetNodeValue = new();
            List<ObjectFieldNode> fields = new()
            {
                new ObjectFieldNode("profilepictureurl", "dab/profilepicture"),
                new ObjectFieldNode("userid", 1)
            };

            targetNodeValue = targetNodeValue.WithFields(fields);

            // Since the relationship fields in both the entities are autogenerated, assert that we get the expected exception.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => MultipleCreateOrderHelper.GetReferencingEntityName(
                relationshipName: "UserProfile_AutogenRelationshipColumn",
                context: context,
                sourceEntityName: sourceEntityName,
                targetEntityName: targetEntityName,
                metadataProvider: _sqlMetadataProvider,
                columnDataInSourceBody: columnDataInSourceBody,
                targetNodeValue: targetNodeValue,
                nestingLevel: 1));
        }

        /// <summary>
        /// Test to validate that when no FK constraint exists between the source and target entity and when a relationship field in one of the entity
        /// is autogenerated, then if the value of atleast one (or more) other non-autogenerated fields is specified in the input for the other entity,
        /// it is not possible to determine a referencing entity. This is because both of the entities on its own, will contain values for
        /// all the columns required to do insertion and act as a referenced entity. Hence, we throw an appropriate exception.
        ///
        /// Here, the relationship between entities: 'User_AutogenToNonAutogenRelationshipColumn' and
        /// 'UserProfile_NonAutogenToAutogenRelationshipColumn' is defined as:
        /// User_AutogenToNonAutogenRelationshipColumn(userid, username) -> UserProfile_NonAutogenToAutogenRelationshipColumn(userid, username)
        /// where the relationship field User_AutogenToNonAutogenRelationshipColumn.userid is an autogenerated field while all other
        /// relationship fields in either entities are non-autogenerated.
        ///
        /// User_AutogenToNonAutogenRelationshipColumn.userid being an autogenerated field AND a relationshipfield tells DAB that
        /// User_AutogenToNonAutogenRelationshipColumn wants to be inserted first so that the autogenerated userId value can be provided to the
        /// insert operation of UserProfile_NonAutogenToAutogenRelationshipColumn.
        /// However, the user provided a value for UserProfile_NonAutogenToAutogenRelationshipColumn.username, which tells DAB that
        /// UserProfile_NonAutogenToAutogenRelationshipColumn wants to be inserted first. Because UserProfile_NonAutogenToAutogenRelationshipColumn.UserName
        /// is a relationship field and a value was provided for that field, DAB thinks that UserProfile_NonAutogenToAutogenRelationshipColumn wants
        /// to be inserted first and become the "referenced" entity. Hence, thi results in conflict because of multiple candidates for referenced entity.
        /// </summary>
        [TestMethod]
        public void ValidateIndeterministicReferencingEntityForAutogenAndNonAutogenRelationshipColumns()
        {
            // Test 1
            IMiddlewareContext context = SetupMiddlewareContext();
            string sourceEntityName = "User_AutogenToNonAutogenRelationshipColumn";
            string targetEntityName = "UserProfile";

            // Setup column input in source entity.
            Dictionary<string, IValueNode> columnDataInSourceBody = new()
            {
                { "email", new StringValueNode("dab@microsoft.com") }
            };

            // Setup column input in target entity.
            ObjectValueNode targetNodeValue = new();
            List<ObjectFieldNode> fields = new()
            {
                new ObjectFieldNode("profilepictureurl", "dab/profilepicture"),
                new ObjectFieldNode("username", "DAB")
            };
            targetNodeValue = targetNodeValue.WithFields(fields);

            // Since the source entity contains an autogenerated relationship field (userid) and the input for target entity
            // contains the relationship field 'username' in it, assert that we get the expected exception as both entity are a potential candidate
            // of being the referenced entity.
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(() => MultipleCreateOrderHelper.GetReferencingEntityName(
                relationshipName: "UserProfile_AutogenToNonAutogenRelationshipColumn",
                context: context,
                sourceEntityName: sourceEntityName,
                targetEntityName: targetEntityName,
                metadataProvider: _sqlMetadataProvider,
                columnDataInSourceBody: columnDataInSourceBody,
                targetNodeValue: targetNodeValue,
                nestingLevel: 1));
        }

        /// <summary>
        /// Test to validate that when no FK constraint exists between the source and target entity and a relationship field in one of the entity
        /// is autogenerated, then the values of all other non-autogenerated fields should also be specified in the input for the same entity,
        /// to successfully determine this entity will act as the referenced entity and the other entity will act as the referencing entity.
        /// This is because the first entity, on its own, will contain values for all the columns required to do insertion and act as a referenced entity.
        ///
        /// Here, the relationship between entities: 'User_AutogenToNonAutogenRelationshipColumn' and
        /// 'UserProfile_NonAutogenToAutogenRelationshipColumn' is defined as:
        /// User_AutogenToNonAutogenRelationshipColumn(userid, username) -> UserProfile_NonAutogenToAutogenRelationshipColumn(userid, username)
        /// where the relationship field User_AutogenToNonAutogenRelationshipColumn.userid is an autogenerated field while all other
        /// relationship fields from either entities are non-autogenerated.
        /// </summary>
        [TestMethod]
        public void ValidateDeterministicReferencingEntityForAutogenAndNonAutogenRelationshipColumns()
        {
            // Test 1
            IMiddlewareContext context = SetupMiddlewareContext();
            string sourceEntityName = "User_AutogenToNonAutogenRelationshipColumn";
            string targetEntityName = "UserProfile";

            // Setup column input in source entity.
            Dictionary<string, IValueNode> columnDataInSourceBody = new()
            {
                { "email", new StringValueNode("dab@microsoft.com") },
                { "username", new StringValueNode("DAB") }
            };

            // Setup column input in target entity.
            ObjectValueNode targetNodeValue = new();
            List<ObjectFieldNode> fields = new()
            {
                new ObjectFieldNode("profilepictureurl", "dab/profilepicture")
            };

            targetNodeValue = targetNodeValue.WithFields(fields);

            string referencingEntityName = MultipleCreateOrderHelper.GetReferencingEntityName(
                relationshipName: "UserProfile_AutogenToNonAutogenRelationshipColumn",
                context: context,
                sourceEntityName: sourceEntityName,
                targetEntityName: targetEntityName,
                metadataProvider: _sqlMetadataProvider,
                columnDataInSourceBody: columnDataInSourceBody,
                targetNodeValue: targetNodeValue,
                nestingLevel: 1);

            Assert.AreEqual(targetEntityName, referencingEntityName);
        }

        #endregion

        #region Order determination test for relationships backed by an FK constraint
        /// <summary>
        /// Test to validate the functionality of the MultipleCreateOrderHelper.TryDetermineReferencingEntityBasedOnEntityRelationshipMetadata()
        /// which successfully determines the referencing entity when FK constraint exists between the entities.
        /// The entity which holds the foreign key reference acts as the referencing entity.
        /// </summary>
        [TestMethod]
        public void ValidateReferencingEntityBasedOnEntityMetadata()
        {
            // Validate that for a relationship of cardinality N:1 between Review - Book where FK constraint
            // exists from Review -> Book, irrespective of which entity is the source in multiple create operation,
            // we successfully determine at the startup, that Review is the referencing entity.
            ValidateReferencingEntityForRelationship(
                sourceEntityName: "Review",
                targetEntityName: "Book",
                expectedReferencingEntityName: "Review");
            ValidateReferencingEntityForRelationship(
                sourceEntityName: "Book",
                targetEntityName: "Review",
                expectedReferencingEntityName: "Review");

            // Validate that for a relationship of cardinality 1:N between Book - Publisher where FK constraint
            // exists from Book -> Publisher,irrespective of which entity is the source in multiple create operation,
            // we successfully determine at the startup, that Book is the referencing entity.
            ValidateReferencingEntityForRelationship(
                sourceEntityName: "Book",
                targetEntityName: "Publisher",
                expectedReferencingEntityName: "Book");
            ValidateReferencingEntityForRelationship(
                sourceEntityName: "Publisher",
                targetEntityName: "Book",
                expectedReferencingEntityName: "Book");

            // Validate that for a relationship of cardinality 1:1 between Stock - stocks_price where FK constraint
            // exists from stocks_price -> Stock, we successfully determine at the startup, that stocks_price is the
            // referencing entity.
            // Stock is the source entity.
            ValidateReferencingEntityForRelationship(
                sourceEntityName: "Stock",
                targetEntityName: "stocks_price",
                expectedReferencingEntityName: "stocks_price");
        }

        #endregion

        #region Helpers
        private static void ValidateReferencingEntityForRelationship(
            string sourceEntityName,
            string targetEntityName,
            string expectedReferencingEntityName)
        {
            // Setup mock IMiddlewareContext.
            IMiddlewareContext context = SetupMiddlewareContext();

            // Get the referencing entity.
            string actualReferencingEntityName = MultipleCreateOrderHelper.GetReferencingEntityName(
                relationshipName: string.Empty, // Don't need relationship name while testing determination of referencing entity using metadata.
                context: context,
                sourceEntityName: sourceEntityName,
                targetEntityName: targetEntityName,
                metadataProvider: _sqlMetadataProvider,
                columnDataInSourceBody: new(),
                targetNodeValue: null,
                nestingLevel: 1);
            Assert.AreEqual(expectedReferencingEntityName, actualReferencingEntityName);
        }
        #endregion

        #region Setup
        private static IMiddlewareContext SetupMiddlewareContext()
        {
            Mock<IMiddlewareContext> context = new();
            Mock<IVariableValueCollection> variables = new();
            context.Setup(x => x.Variables).Returns(variables.Object);
            return context.Object;
        }
        #endregion
    }
}
