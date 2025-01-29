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
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using HotChocolate.Language;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class GraphQLSchemaBuilderTests
    {
        /// <summary>
        /// Validates building of cosmos gql schema.
        /// 1. Loads customer base schema.
        /// 2. Calls on schema builder to build root document and the input type objects.
        /// 3. Root document should be created based on loaded schema.
        /// 4. Input type objects should be created based on entity types and not be duplicated.
        /// </summary>
        [DataTestMethod]
        public async Task CosmosSchemaBuilderTestAsync()
        {
            string fileContents = await File.ReadAllTextAsync("Multidab-config.CosmosDb_NoSql.json");

            string cosmosFileContents = await File.ReadAllTextAsync("schema.gql");

            IFileSystem fs = new MockFileSystem(new Dictionary<string, MockFileData>()
            {
                { "dab-config.json", new MockFileData(fileContents) },
                { "schema.gql", new MockFileData(cosmosFileContents) }
            });

            FileSystemRuntimeConfigLoader loader = new(fs);

            RuntimeConfigProvider provider = new(loader);

            Mock<IAbstractQueryManagerFactory> queryManagerfactory = new();
            Mock<IQueryEngineFactory> queryEngineFactory = new();
            Mock<IMutationEngineFactory> mutationEngineFactory = new();
            Mock<ILogger<ISqlMetadataProvider>> logger = new();
            IMetadataProviderFactory metadataProviderFactory = new MetadataProviderFactory(provider, queryManagerfactory.Object, logger.Object, fs, handler: null);
            Mock<IAuthorizationResolver> authResolver = new();

            GraphQLSchemaCreator creator = new(provider, queryEngineFactory.Object, mutationEngineFactory.Object, metadataProviderFactory, authResolver.Object);
            (DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypeObjects) = creator.GenerateGraphQLObjects();

            Assert.IsNotNull(root);
            Assert.IsNotNull(inputTypeObjects);
            Assert.AreEqual(9, inputTypeObjects.Count, $"{nameof(InputObjectTypeDefinitionNode)} is invalid. input Type objects have not been created correctly.");

            // 11 input types generated for the 3 entity types in the schema.gql. IntFilter,StringFilter etc should not be duplicated.
            Assert.AreEqual(13, root.Definitions.Count, $"{nameof(DocumentNode)}:Root is invalid. root definitions count does not match expected count.");
        }
    }
}
