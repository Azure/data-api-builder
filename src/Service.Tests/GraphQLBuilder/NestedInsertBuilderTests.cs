// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Models;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Azure.DataApiBuilder.Core.Services.Cache;
using ZiggyCreatures.Caching.Fusion;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Config.ObjectModel;
using System;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using System.Linq;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class NestedInsertBuilderTests
    {
        private static RuntimeConfig _runtimeConfig;
        private static GraphQLSchemaCreator _schemaCreator;

        [ClassInitialize]
        public static async Task SetUpAsync(TestContext context)
        {
            (_schemaCreator, _runtimeConfig) = await SetUpGQLSchemaCreator("MsSql");
        }

        private static async Task<Tuple<GraphQLSchemaCreator, RuntimeConfig>> SetUpGQLSchemaCreator(string databaseType)
        {
            string fileContents = await File.ReadAllTextAsync($"dab-config.{databaseType}.json");

            IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(fileContents) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);
            RuntimeConfigProvider provider = new(loader);
            RuntimeConfig runtimeConfig = provider.GetConfig();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            Mock<ILogger<IQueryExecutor>> qMlogger = new();
            Mock<IFusionCache> cache = new();
            DabCacheService cacheService = new(cache.Object, logger: null, httpContextAccessor.Object);
            IAbstractQueryManagerFactory queryManagerfactory = new QueryManagerFactory(provider, qMlogger.Object, httpContextAccessor.Object);
            Mock<ILogger<ISqlMetadataProvider>> metadatProviderLogger = new();
            IMetadataProviderFactory metadataProviderFactory = new MetadataProviderFactory(provider, queryManagerfactory, metadatProviderLogger.Object, fs);
            await metadataProviderFactory.InitializeAsync();
            GQLFilterParser graphQLFilterParser = new(provider, metadataProviderFactory);
            IAuthorizationResolver authzResolver = new AuthorizationResolver(provider, metadataProviderFactory);
            Mock<ILogger<IQueryEngine>> queryEngineLogger = new();
            IQueryEngineFactory queryEngineFactory = new QueryEngineFactory(
                runtimeConfigProvider: provider,
                queryManagerFactory: queryManagerfactory,
                metadataProviderFactory: metadataProviderFactory,
                cosmosClientProvider: null,
                contextAccessor: httpContextAccessor.Object,
                authorizationResolver: authzResolver,
                gQLFilterParser: graphQLFilterParser,
                logger: queryEngineLogger.Object,
                cache: cacheService);
            Mock<IMutationEngineFactory> mutationEngineFactory = new();

            return new(new GraphQLSchemaCreator(provider, queryEngineFactory, mutationEngineFactory.Object, metadataProviderFactory, authzResolver), runtimeConfig);
        }

        [DataTestMethod]
        [DataRow("Book", "Author", DisplayName = "Validate absence of linking object for Book->Author M:N relationship")]
        [DataRow("Author", "Book", DisplayName = "Validate absence of linking object for Author->Book M:N relationship")]
        [DataRow("BookNF", "AuthorNF", DisplayName = "Validate absence of linking object for BookNF->AuthorNF M:N relationship")]
        [DataRow("AuthorNF", "BookNF", DisplayName = "Validate absence of linking object for BookNF->AuthorNF M:N relationship")]
        public void ValidateAbsenceOfLinkingObjectDefinitionsInDocumentNodeForMNRelationships(string sourceEntityName, string targetEntityName)
        {
            (DocumentNode documentNode, Dictionary<string, InputObjectTypeDefinitionNode> _inputTypeObjects) = _schemaCreator.GenerateGraphQLObjects();
            string linkingEntityName = Entity.GenerateLinkingEntityName(sourceEntityName, targetEntityName);

            // Validate that the document node does not expose the object type definition for linking table.
            IEnumerable<IHasName> definitions = documentNode.Definitions.Where(d => d is IHasName).Cast<IHasName>();
            ObjectTypeDefinitionNode linkingObjectTypeDefinitionNode = GetObjectTypeDefinitionNode(new NameNode(linkingEntityName), definitions);
            Assert.IsNull(linkingObjectTypeDefinitionNode);
        }

        [DataTestMethod]
        [DataRow("Book", "Author", DisplayName = "Validate presence of source->target linking object for Book->Author M:N relationship")]
        [DataRow("Author", "Book", DisplayName = "Validate presence of source->target linking object for Author->Book M:N relationship")]
        [DataRow("BookNF", "AuthorNF", DisplayName = "Validate presence of source->target linking object for BookNF->AuthorNF M:N relationship")]
        [DataRow("AuthorNF", "BookNF", DisplayName = "Validate presence of source->target linking object for BookNF->AuthorNF M:N relationship")]
        public void ValidatePresenceOfSourceTargetLinkingObjectDefinitionsInDocumentNodeForMNRelationships(string sourceEntityName, string targetEntityName)
        {
            (DocumentNode documentNode, Dictionary<string, InputObjectTypeDefinitionNode> _inputTypeObjects) = _schemaCreator.GenerateGraphQLObjects();
            // Validate that we have indeed inferred the object type definition for all the source->target linking objects.
            IEnumerable<IHasName> definitions = documentNode.Definitions.Where(d => d is IHasName).Cast<IHasName>();
            NameNode sourceTargetLinkingNodeName = new(GenerateLinkingNodeName(
                        GetDefinedSingularName(sourceEntityName, _runtimeConfig.Entities[sourceEntityName]),
                        GetDefinedSingularName(targetEntityName, _runtimeConfig.Entities[targetEntityName])));
            ObjectTypeDefinitionNode sourceTargetLinkingObjectTypeDefinitionNode = GetObjectTypeDefinitionNode(sourceTargetLinkingNodeName, definitions);
            Assert.IsNotNull(sourceTargetLinkingObjectTypeDefinitionNode);
            
        }

        private static ObjectTypeDefinitionNode GetObjectTypeDefinitionNode(NameNode sourceTargetLinkingNodeName, IEnumerable<IHasName> definitions)
        {
            IHasName definition = definitions.FirstOrDefault(d => d.Name.Value == sourceTargetLinkingNodeName.Value);
            return definition is ObjectTypeDefinitionNode objectTypeDefinitionNode ? objectTypeDefinitionNode : null;
        }
    }
}
