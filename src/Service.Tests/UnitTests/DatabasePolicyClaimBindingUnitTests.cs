// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Security.Claims;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.OData.UriParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Tests the complete database-policy claim binding path from an authenticated claim
    /// through OData AST creation and SQL/Cosmos query parameter collection.
    /// </summary>
    [TestClass]
    public class DatabasePolicyClaimBindingUnitTests
    {
        private const string ENTITY_NAME = AuthorizationHelpers.TEST_ENTITY;
        private const string ROLE_NAME = AuthorizationHelpers.TEST_ROLE;
        private const EntityActionOperation OPERATION = EntityActionOperation.Read;

        /// <summary>
        /// Verifies that encoded and literal syntax remains claim data throughout the complete
        /// SQL and Cosmos policy pipelines.
        /// </summary>
        [DataTestMethod]
        [DataRow("alice%27 or 1 eq 1 or %27", DisplayName = "Percent-encoded quote")]
        [DataRow("alice%2527 or 1 eq 1 or %2527", DisplayName = "Double-encoded quote")]
        [DataRow("alice%252527 or 1 eq 1 or %27", DisplayName = "Mixed nested encodings")]
        [DataRow("alice' or 1 eq 1 or '", DisplayName = "Literal quote")]
        [DataRow("50% complete", DisplayName = "Legitimate percent character")]
        public void StringClaim_RemainsBoundParameterAcrossSqlAndCosmos(string claimValue)
        {
            const string policy = "@item.textCol eq @claims.value";
            (AuthorizationResolver resolver, DefaultHttpContext context) = CreateAuthorizationContext(
                policy,
                new Claim("value", claimValue, ClaimValueTypes.String));
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();

            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver);
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                OPERATION,
                sqlStructure,
                context,
                resolver,
                metadataProvider.Object);

            Assert.AreEqual("([textCol] = @param0)", sqlStructure.GetDbPolicyForOperation(OPERATION));
            AssertParameterValues(sqlStructure, claimValue);

            FilterClause filterClause = ResolveFilterClause(resolver, context, metadataProvider.Object);
            TestQueryStructure cosmosStructure = new(metadataProvider.Object, resolver);
            string cosmosPredicate = filterClause.Expression.Accept(
                new ODataASTCosmosVisitor("c", cosmosStructure));

            Assert.AreEqual("(c.textCol = @param0)", cosmosPredicate);
            AssertParameterValues(cosmosStructure, claimValue);
        }

        /// <summary>
        /// Verifies aliases in root and unary Boolean positions are replaced throughout the AST
        /// and generate executable, parameterized predicates for both SQL and Cosmos DB.
        /// </summary>
        [DataTestMethod]
        [DataRow("@claims.value", "true", "(@param0 = @param1)", DisplayName = "Root Boolean claim")]
        [DataRow("not @claims.value", "false", "(NOT (@param0 = @param1) )", DisplayName = "Unary Boolean claim")]
        public void BooleanClaim_InRootOrUnaryPosition_IsResolvedAcrossSqlAndCosmos(
            string policy,
            string claimValue,
            string expectedPredicate)
        {
            bool expectedValue = bool.Parse(claimValue);
            (AuthorizationResolver resolver, DefaultHttpContext context) = CreateAuthorizationContext(
                policy,
                new Claim("value", claimValue, ClaimValueTypes.Boolean));
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();

            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver);
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                OPERATION,
                sqlStructure,
                context,
                resolver,
                metadataProvider.Object);

            Assert.AreEqual(expectedPredicate, sqlStructure.GetDbPolicyForOperation(OPERATION));
            AssertParameterValues(sqlStructure, expectedValue, true);

            FilterClause filterClause = ResolveFilterClause(resolver, context, metadataProvider.Object);
            TestQueryStructure cosmosStructure = new(metadataProvider.Object, resolver);
            string cosmosPredicate = filterClause.Expression.Accept(
                new ODataASTCosmosVisitor("c", cosmosStructure));

            Assert.AreEqual(expectedPredicate, cosmosPredicate);
            AssertParameterValues(cosmosStructure, expectedValue, true);
        }

        /// <summary>
        /// Verifies aliases remain resolved when Boolean predicates are nested under logical operators.
        /// </summary>
        [TestMethod]
        public void BooleanClaims_InNestedLogicalExpression_AreResolvedAcrossSqlAndCosmos()
        {
            const string policy = "@claims.first and not @claims.second";
            const string expectedPredicate = "((@param0 = @param1) AND (NOT (@param2 = @param3) ))";
            (AuthorizationResolver resolver, DefaultHttpContext context) = CreateAuthorizationContext(
                policy,
                new Claim("first", "true", ClaimValueTypes.Boolean),
                new Claim("second", "false", ClaimValueTypes.Boolean));
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();

            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver);
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                OPERATION,
                sqlStructure,
                context,
                resolver,
                metadataProvider.Object);

            Assert.AreEqual(expectedPredicate, sqlStructure.GetDbPolicyForOperation(OPERATION));
            AssertParameterValues(sqlStructure, true, true, false, true);

            FilterClause filterClause = ResolveFilterClause(resolver, context, metadataProvider.Object);
            TestQueryStructure cosmosStructure = new(metadataProvider.Object, resolver);
            string cosmosPredicate = filterClause.Expression.Accept(
                new ODataASTCosmosVisitor("c", cosmosStructure));

            Assert.AreEqual(expectedPredicate, cosmosPredicate);
            AssertParameterValues(cosmosStructure, true, true, false, true);
        }

        /// <summary>
        /// Verifies ordinary comparison predicates are not rewritten as comparisons to Boolean true.
        /// </summary>
        [TestMethod]
        public void StaticComparisonPolicy_RemainsValidAcrossSqlAndCosmos()
        {
            const string policy = "@item.intCol ne 6 and @item.doubleCol gt 0";
            const string expectedSqlPredicate = "(([intCol] != @param0) AND ([doubleCol] > @param1))";
            const string expectedCosmosPredicate = "((c.intCol != @param0) AND (c.doubleCol > @param1))";
            (AuthorizationResolver resolver, DefaultHttpContext context) = CreateAuthorizationContext(policy);
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();

            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver);
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                OPERATION,
                sqlStructure,
                context,
                resolver,
                metadataProvider.Object);

            Assert.AreEqual(expectedSqlPredicate, sqlStructure.GetDbPolicyForOperation(OPERATION));
            AssertParameterValues(sqlStructure, 6, 0d);

            FilterClause filterClause = ResolveFilterClause(resolver, context, metadataProvider.Object);
            TestQueryStructure cosmosStructure = new(metadataProvider.Object, resolver);
            string cosmosPredicate = filterClause.Expression.Accept(
                new ODataASTCosmosVisitor("c", cosmosStructure));

            Assert.AreEqual(expectedCosmosPredicate, cosmosPredicate);
            AssertParameterValues(cosmosStructure, 6, 0d);
        }

        /// <summary>
        /// Verifies a bare Boolean claim can be combined with an ordinary comparison without
        /// rewriting the comparison predicate as "predicate equals true".
        /// </summary>
        [TestMethod]
        public void BooleanClaim_CombinedWithComparison_OnlyNormalizesBareClaim()
        {
            const string policy = "@item.intCol ne 6 and @claims.allowed";
            const string expectedSqlPredicate = "(([intCol] != @param0) AND (@param1 = @param2))";
            const string expectedCosmosPredicate = "((c.intCol != @param0) AND (@param1 = @param2))";
            (AuthorizationResolver resolver, DefaultHttpContext context) = CreateAuthorizationContext(
                policy,
                new Claim("allowed", "true", ClaimValueTypes.Boolean));
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();

            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver);
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                OPERATION,
                sqlStructure,
                context,
                resolver,
                metadataProvider.Object);

            Assert.AreEqual(expectedSqlPredicate, sqlStructure.GetDbPolicyForOperation(OPERATION));
            AssertParameterValues(sqlStructure, 6, true, true);

            FilterClause filterClause = ResolveFilterClause(resolver, context, metadataProvider.Object);
            TestQueryStructure cosmosStructure = new(metadataProvider.Object, resolver);
            string cosmosPredicate = filterClause.Expression.Accept(
                new ODataASTCosmosVisitor("c", cosmosStructure));

            Assert.AreEqual(expectedCosmosPredicate, cosmosPredicate);
            AssertParameterValues(cosmosStructure, 6, true, true);
        }

        /// <summary>
        /// Verifies existing authorization resolver implementations remain usable for static policies.
        /// </summary>
        [TestMethod]
        public void LegacyAuthorizationResolver_StaticPolicy_RemainsSupported()
        {
            Mock<IAuthorizationResolver> resolver = new();
            resolver
                .Setup(instance => instance.GetDBPolicyForRequest(ENTITY_NAME, ROLE_NAME, OPERATION))
                .Returns("@item.intCol eq 42");
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();
            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver.Object);
            DefaultHttpContext context = new();
            context.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = ROLE_NAME;

            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                OPERATION,
                sqlStructure,
                context,
                resolver.Object,
                metadataProvider.Object);

            Assert.AreEqual("([intCol] = @param0)", sqlStructure.GetDbPolicyForOperation(OPERATION));
            AssertParameterValues(sqlStructure, 42);
        }

        /// <summary>
        /// Verifies existing string-only resolver implementations fail closed for claim-bearing policies.
        /// </summary>
        [TestMethod]
        public void LegacyAuthorizationResolver_ClaimPolicy_FailsClosed()
        {
            Mock<IAuthorizationResolver> resolver = new();
            resolver
                .Setup(instance => instance.GetDBPolicyForRequest(ENTITY_NAME, ROLE_NAME, OPERATION))
                .Returns("@item.textCol eq @claims.value");
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();
            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver.Object);
            DefaultHttpContext context = new();
            context.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = ROLE_NAME;

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
                AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                    OPERATION,
                    sqlStructure,
                    context,
                    resolver.Object,
                    metadataProvider.Object));

            Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed, exception.SubStatusCode);
            Assert.AreEqual(0, sqlStructure.Parameters.Count);
        }

        /// <summary>
        /// Verifies string claims are promoted to the target column's numeric type before
        /// SQL and Cosmos parameters are created.
        /// </summary>
        [TestMethod]
        public void StringClaim_IsPromotedToNumericColumnType()
        {
            const string policy = "@item.intCol eq @claims.value";
            (AuthorizationResolver resolver, DefaultHttpContext context) = CreateAuthorizationContext(
                policy,
                new Claim("value", "42", ClaimValueTypes.String));
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();

            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver);
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                OPERATION,
                sqlStructure,
                context,
                resolver,
                metadataProvider.Object);

            Assert.AreEqual("([intCol] = @param0)", sqlStructure.GetDbPolicyForOperation(OPERATION));
            AssertParameterValues(sqlStructure, 42);
            Assert.AreEqual(DbType.Int32, sqlStructure.Parameters["@param0"].DbType);

            FilterClause filterClause = ResolveFilterClause(resolver, context, metadataProvider.Object);
            TestQueryStructure cosmosStructure = new(metadataProvider.Object, resolver);
            string cosmosPredicate = filterClause.Expression.Accept(
                new ODataASTCosmosVisitor("c", cosmosStructure));

            Assert.AreEqual("(c.intCol = @param0)", cosmosPredicate);
            AssertParameterValues(cosmosStructure, 42);
        }

        /// <summary>
        /// Verifies null claims remain typed null AST constants and do not create provider parameters.
        /// </summary>
        [TestMethod]
        public void NullClaim_ProducesNullPredicateWithoutParameter()
        {
            const string policy = "@item.textCol eq @claims.value";
            (AuthorizationResolver resolver, DefaultHttpContext context) = CreateAuthorizationContext(
                policy,
                new Claim("value", "null", JsonClaimValueTypes.JsonNull));
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();

            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver);
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                OPERATION,
                sqlStructure,
                context,
                resolver,
                metadataProvider.Object);

            Assert.AreEqual("([textCol] IS NULL)", sqlStructure.GetDbPolicyForOperation(OPERATION));
            Assert.AreEqual(0, sqlStructure.Parameters.Count);

            FilterClause filterClause = ResolveFilterClause(resolver, context, metadataProvider.Object);
            TestQueryStructure cosmosStructure = new(metadataProvider.Object, resolver);
            string cosmosPredicate = filterClause.Expression.Accept(
                new ODataASTCosmosVisitor("c", cosmosStructure));

            Assert.AreEqual("(c.textCol IS NULL)", cosmosPredicate);
            Assert.AreEqual(0, cosmosStructure.Parameters.Count);
        }

        /// <summary>
        /// Verifies non-finite floating-point claims fail before either query structure can
        /// collect a provider parameter.
        /// </summary>
        [DataTestMethod]
        [DataRow("NaN")]
        [DataRow("Infinity")]
        [DataRow("-Infinity")]
        [DataRow("1e9999")]
        public void NonFiniteDoubleClaim_FailsBeforeParameterCollection(string claimValue)
        {
            const string policy = "@item.doubleCol eq @claims.value";
            (AuthorizationResolver resolver, DefaultHttpContext context) = CreateAuthorizationContext(
                policy,
                new Claim("value", claimValue, ClaimValueTypes.Double));
            Mock<ISqlMetadataProvider> metadataProvider = CreateMetadataProvider();
            TestSqlQueryStructure sqlStructure = new(metadataProvider.Object, resolver);

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
                AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(
                    OPERATION,
                    sqlStructure,
                    context,
                    resolver,
                    metadataProvider.Object));

            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnsupportedClaimValueType, exception.SubStatusCode);
            Assert.AreEqual(0, sqlStructure.Parameters.Count);
        }

        /// <summary>
        /// Verifies resolved policies own a read-only snapshot, including the shared empty value.
        /// </summary>
        [TestMethod]
        public void ResolvedPolicyClaimValues_AreImmutableSnapshots()
        {
            Dictionary<string, object?> source = new() { ["@claim"] = "original" };
            ResolvedDatabasePolicy policy = new("value eq @claim", source);
            source["@claim"] = "modified";

            Assert.AreEqual("original", policy.ClaimValues["@claim"]);
            Assert.IsInstanceOfType<IDictionary<string, object?>>(ResolvedDatabasePolicy.Empty.ClaimValues);
            IDictionary<string, object?> emptyValues = (IDictionary<string, object?>)ResolvedDatabasePolicy.Empty.ClaimValues;
            Assert.ThrowsException<NotSupportedException>(() => emptyValues.Add("@claim", "value"));
        }

        private static FilterClause ResolveFilterClause(
            AuthorizationResolver resolver,
            DefaultHttpContext context,
            ISqlMetadataProvider metadataProvider)
        {
            ResolvedDatabasePolicy resolvedPolicy = resolver.ResolveDBPolicy(
                ENTITY_NAME,
                ROLE_NAME,
                OPERATION,
                context);

            return AuthorizationPolicyHelpers.GetDBPolicyClauseForQueryStructure(
                resolvedPolicy,
                ENTITY_NAME,
                $"{ENTITY_NAME}.{metadataProvider.EntityToDatabaseObject[ENTITY_NAME].FullName}",
                metadataProvider)!;
        }

        private static (AuthorizationResolver Resolver, DefaultHttpContext Context) CreateAuthorizationContext(
            string policy,
            params Claim[] claims)
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: ENTITY_NAME,
                roleName: ROLE_NAME,
                operation: OPERATION,
                databasePolicy: policy);
            AuthorizationResolver resolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            ClaimsIdentity identity = new(
                claims,
                authenticationType: "TestAuth",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);
            DefaultHttpContext context = new()
            {
                User = new ClaimsPrincipal(identity)
            };
            context.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = ROLE_NAME;

            return (resolver, context);
        }

        private static Mock<ISqlMetadataProvider> CreateMetadataProvider()
        {
            SourceDefinition sourceDefinition = new();
            sourceDefinition.Columns.Add("id", new ColumnDefinition(typeof(int)) { DbType = DbType.Int32 });
            sourceDefinition.Columns.Add("flag", new ColumnDefinition(typeof(bool)) { DbType = DbType.Boolean });
            sourceDefinition.Columns.Add("textCol", new ColumnDefinition(typeof(string)) { DbType = DbType.String });
            sourceDefinition.Columns.Add("intCol", new ColumnDefinition(typeof(int)) { DbType = DbType.Int32 });
            sourceDefinition.Columns.Add("doubleCol", new ColumnDefinition(typeof(double)) { DbType = DbType.Double });
            sourceDefinition.PrimaryKey.Add("id");

            DatabaseObject databaseObject = new DatabaseTable(schemaName: "dbo", tableName: "PolicyTable");
            Dictionary<string, DatabaseObject> entities = new()
            {
                [ENTITY_NAME] = databaseObject
            };

            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.SetupGet(provider => provider.EntityToDatabaseObject).Returns(entities);
            metadataProvider.Setup(provider => provider.GetEntityNamesAndDbObjects()).Returns(entities);
            metadataProvider.Setup(provider => provider.GetLinkingEntities())
                .Returns(new Dictionary<string, Entity>());
            metadataProvider.Setup(provider => provider.GetSourceDefinition(ENTITY_NAME)).Returns(sourceDefinition);
            metadataProvider.Setup(provider => provider.GetDatabaseType()).Returns(DatabaseType.MSSQL);
            metadataProvider.Setup(provider => provider.GetQueryBuilder()).Returns(new MsSqlQueryBuilder());

            string? exposedName;
            metadataProvider
                .Setup(provider => provider.TryGetExposedColumnName(It.IsAny<string>(), It.IsAny<string>(), out exposedName))
                .Callback(new ColumnNameCallback((string _, string column, out string? name) => name = column))
                .Returns(true);

            string? backingName;
            metadataProvider
                .Setup(provider => provider.TryGetBackingColumn(It.IsAny<string>(), It.IsAny<string>(), out backingName))
                .Callback(new ColumnNameCallback((string _, string column, out string? name) => name = column))
                .Returns(true);

            ODataParser parser = new();
            parser.BuildModel(metadataProvider.Object);
            metadataProvider.Setup(provider => provider.GetODataParser()).Returns(parser);
            return metadataProvider;
        }

        private static void AssertParameterValues(BaseQueryStructure structure, params object?[] expectedValues)
        {
            object?[] actualValues = structure.Parameters.Values
                .Select(parameter => parameter.Value)
                .ToArray();
            CollectionAssert.AreEqual(expectedValues, actualValues);
        }

        private delegate void ColumnNameCallback(string entity, string column, out string? name);

        private sealed class TestSqlQueryStructure : BaseSqlQueryStructure
        {
            public TestSqlQueryStructure(
                ISqlMetadataProvider metadataProvider,
                IAuthorizationResolver authorizationResolver)
                : base(
                    metadataProvider,
                    authorizationResolver,
                    gQLFilterParser: null!,
                    entityName: ENTITY_NAME)
            {
            }
        }

        private sealed class TestQueryStructure : BaseQueryStructure
        {
            public TestQueryStructure(
                ISqlMetadataProvider metadataProvider,
                IAuthorizationResolver authorizationResolver)
                : base(
                    metadataProvider,
                    authorizationResolver,
                    gQLFilterParser: null!,
                    entityName: ENTITY_NAME)
            {
            }
        }
    }
}
