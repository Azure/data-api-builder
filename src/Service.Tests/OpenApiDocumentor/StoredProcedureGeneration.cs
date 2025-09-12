// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.OpenApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration
{
    /// <summary>
    /// Integration tests validating correct OpenAPI schema metadata
    /// for stored procedures is generated.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class StoredProcedureGeneration
    {
        private const string CONTENT_TYPE = "application/json";
        private const string CUSTOM_CONFIG = "sp-config.MsSql.json";
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;
        private const string SCHEMA_PROPERTY_ACCESSOR = "value";
        private const string SCHEMA_REF_PREFIX = "#/components/schemas/";

        // Error messages
        private const string SCHEMA_REF_ID_ERROR = "Unexpected schema reference id.";
        private static OpenApiDocument _openApiDocument;
        private static RuntimeEntities _runtimeEntities;

        /// <summary>
        /// Bootstraps a single test server instance using one runtime config file so
        /// each test need not boot the entire server to generate a description doc.
        /// Each test validates the OpenAPI description generated for a distinct entity.
        /// </summary>
        /// <param name="context">Test context required by MSTest for class init method.</param>
        [ClassInitialize]
        public static async Task ClassInitialize(TestContext context)
        {
            CreateEntities();
            _openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: _runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);
        }

        /// <summary>
        /// Populates _runtimeEntities with entity configuration to use in tests.
        /// All entities under test in this class must be added here.
        /// </summary>
        public static void CreateEntities()
        {
            Entity entity1 = new(
                Source: new(Object: "insert_and_display_all_books_for_given_publisher", EntitySourceType.StoredProcedure, null, null),
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: "Represents a stored procedure for books");

            Dictionary<string, Entity> entities = new()
            {
                { "sp1", entity1 }
            };

            _runtimeEntities = new(entities);
        }

        /// <summary>
        /// Validates that the generated request body references stored procedure parameters
        /// and not result set columns.
        /// </summary>
        /// <param name="entityName">Entity name</param>
        /// <param name="expectedParameters">Expected parameters in request body</param>
        /// <param name="expectedParametersJsonTypes">Expected parameter value types in request body.</param>
        [DataRow("sp1", new string[] { "title", "publisher_name" }, new string[] { "string", "string" }, DisplayName = "Validate request body parameters and parameter Json data types.")]
        [DataTestMethod]
        public void ValidateRequestBodyContents(string entityName, string[] expectedParameters, string[] expectedParametersJsonTypes)
        {
            Dictionary<OperationType, bool> configuredOperations = ResolveConfiguredOperations(_runtimeEntities[entityName]);
            foreach (OperationType opType in configuredOperations.Keys)
            {
                // Validate that the generated OpenAPI document has keys for all REST methods defined
                // in the runtime config for the stored procedure entity.
                bool operationRegisteredInOpenApiDoc = _openApiDocument.Paths["/" + entityName].Operations.ContainsKey(opType);
                Assert.AreEqual(configuredOperations[opType], operationRegisteredInOpenApiDoc);

                // Get and delete operations will not have request bodies, enforced by OpenAPI.NET
                if (opType is OperationType.Get || opType is OperationType.Delete)
                {
                    continue;
                }

                OpenApiRequestBody requestBody = GetOperationRequestBody(entityName, opType);
                OpenApiReference schemaComponentReference = GetRequestBodyReference(requestBody);
                string expectedSchemaReferenceId = $"{entityName}{OpenApiDocumentor.SP_REQUEST_SUFFIX}";

                ValidateOpenApiReferenceContents(schemaComponentReference, expectedSchemaReferenceId, expectedParameters, expectedParametersJsonTypes);
            }
        }

        /// <summary>
        /// Validates that the generated response body references stored procedure
        /// result set columns and not parameters.
        /// </summary>
        /// <param name="entityName">Entity to test, requires updating the CreateEntities() helper.</param>
        /// <param name="expectedColumns">Expected first result set columns</param>
        /// <param name="expectedColumnJsonTypes">Expected first result set column types (JSON)</param>
        [DataRow("sp1", new string[] { "id", "title", "publisher_id" }, new string[] { "number", "string", "number" }, DisplayName = "Validate response body parameters and parameter Json data types.")]
        [DataTestMethod]
        public void ValidateResponseBodyContents(string entityName, string[] expectedColumns, string[] expectedColumnJsonTypes)
        {
            // With the responses, we can validate the Properties and their types
            // can also validate Reference.Id 'sp1_sp_reponse' to ensure correct mapping.
            // Though, the fact that Properties is populated correctly, means the OpenAPI.NET mechanisms
            // populated Properties with the contents of the referenced schema component.
            OpenApiResponses responses = GetOperationResponses(entityName, OperationType.Get);

            // Validate the correct schema component is referenced in the response body.
            // The OpenApiResponses dictionary key represents the integer value of the HttpStatusCode,
            // which is returned when using Enum.ToString("D").
            // The "D" format specified "displays the enumeration entry as an integer value in the shortest representation possible."
            OpenApiReference schemaComponentReference = GetResponseBodyReference(responses[HttpStatusCode.OK.ToString("D")]);
            string expectedSchemaReferenceId = $"{entityName}{OpenApiDocumentor.SP_RESPONSE_SUFFIX}";

            ValidateOpenApiReferenceContents(schemaComponentReference, expectedSchemaReferenceId, expectedColumns, expectedColumnJsonTypes);
        }

        /// <summary>
        /// Integration tests validating that entity descriptions are included in the OpenAPI document.
        /// </summary>
        [TestMethod]
        public void OpenApiDocumentor_TagsIncludeEntityDescription()
        {
            // Arrange: The entity name and expected description
            string entityName = "sp1";
            string expectedDescription = "Represents a stored procedure for books"; // Set this to your actual description

            // Act: Get the tags from the OpenAPI document
            IList<OpenApiTag> tags = _openApiDocument.Tags;

            // Assert: There is a tag for the entity and it includes the description
            Assert.IsTrue(tags.Any(t => t.Name == entityName && t.Description == expectedDescription),
                $"Expected tag for '{entityName}' with description '{expectedDescription}' not found.");
        }

        /// <summary>
        /// Validates that the provided OpenApiReference object has the expected schema reference id
        /// and that that id is present in the list of component schema in the OpenApi document.
        /// Additionally, validates that the references object contains the expected properties and JSON data types:
        /// - Parameters when evaluating the schema reference object for a request body.
        /// - Output result set columns when evaluating the schema reference object for a response body.
        /// </summary>
        /// <param name="reference">OpenApiReference object for request body or response body to validate.</param>
        /// <param name="expectedSchemaReferenceId">Schema reference id with format: {entityname}_sp_{response/request}</param>
        /// <param name="expectedProperties">List of expected property names</param>
        /// <param name="expectedPropertyJsonTypes">List of expected property JSON data types.</param>
        private static void ValidateOpenApiReferenceContents(
            OpenApiReference reference,
            string expectedSchemaReferenceId,
            string[] expectedProperties,
            string[] expectedPropertyJsonTypes)
        {
            Assert.AreEqual(expected: expectedSchemaReferenceId, actual: reference.Id, message: SCHEMA_REF_ID_ERROR);
            Assert.AreEqual(expected: $"{SCHEMA_REF_PREFIX}{expectedSchemaReferenceId}", actual: reference.ReferenceV3);

            // It is possible to get the schema component from an OpenApiResponse object because OpenAPI.NET functionality
            // auto-resolves the reference to a concrete OpenApiSchema object.  (If the reference were not auto-resolved,
            // the property value would look like '"$ref": "#/components/schemas/<entity_name>_sp_<request/response>"'
            // However, to avoid testing OpenAPI.NET functionality,this test looks directly at the OpenAPI document's
            // "components" property to validate presence and composition of the generated schema component.
            Assert.IsTrue(_openApiDocument.Components.Schemas.ContainsKey(expectedSchemaReferenceId), message: "Unexpected absence of schema component definition.");
            Dictionary<string, OpenApiSchema> schemaComponentProperties = new(GetSchemaComponentProperties(expectedSchemaReferenceId));

            // Validate that the generated properties do not outnumber the count of expected columns.
            Assert.AreEqual(expectedProperties.Length, schemaComponentProperties.Count, message: "The number of generated properties is not expected.");

            // Validate property presence and accurate property JSON type.
            // Test input expectedProperties and expectedPropertyJsonTypes are always expected to have the same length.
            for (int propertyIdx = 0; propertyIdx < expectedProperties.Length; propertyIdx++)
            {
                string propertyName = expectedProperties[propertyIdx];
                string propertyType = expectedPropertyJsonTypes[propertyIdx];
                Assert.IsTrue(schemaComponentProperties.ContainsKey(propertyName), message: "Unexpected property absence in result schema component.");
                Assert.AreEqual(schemaComponentProperties[propertyName].Type, propertyType, message: "Unexpected property JSON type in result schema component.");
            }
        }

        /// <summary>
        /// Traverses the OpenAPI document to find and return the request body object.
        /// </summary>
        /// <param name="entityName">Name of the entity, used to generated the path key.</param>
        /// <param name="operationType">OpenAPI operation type.</param>
        /// <returns></returns>
        public static OpenApiRequestBody GetOperationRequestBody(string entityName, OperationType operationType)
        {
            return _openApiDocument.Paths["/" + entityName].Operations[operationType].RequestBody;
        }

        /// <summary>
        /// Traverses the OpenAPI document to find and return the Dictionary of response body objects.
        /// </summary>
        /// <param name="entityName">Name of the entity, used to generated the path key.</param>
        /// <param name="operationType">OpenAPI operation type.</param>
        /// <returns>Dictionary of OpenApiResponses generated.</returns>
        public static OpenApiResponses GetOperationResponses(string entityName, OperationType operationType)
        {
            return _openApiDocument.Paths["/" + entityName].Operations[operationType].Responses;
        }

        /// <summary>
        /// Returns the schema component properties which represent the request body or response body
        /// fields and field data types.
        /// </summary>
        /// <param name="schemaComponentReferenceId">Schema component id of the form {entityName}_sp_{request/response}.</param>
        /// <returns>Dictionary of stored procedure result columns/parameters names and their JSON value types.</returns>
        public static IDictionary<string, OpenApiSchema> GetSchemaComponentProperties(string schemaComponentReferenceId)
        {
            return _openApiDocument.Components.Schemas[schemaComponentReferenceId].Properties;
        }

        /// <summary>
        /// Returns the reference object which names the schema component that should be used to represent the
        /// response body.
        /// </summary>
        /// <param name="response">Response object </param>
        /// <returns>OpenApiReference containing the id of the schema component.</returns>
        public static OpenApiReference GetResponseBodyReference(OpenApiResponse response)
        {
            return response.Content[CONTENT_TYPE].Schema.Properties[SCHEMA_PROPERTY_ACCESSOR].Items.Reference;
        }

        /// <summary>
        /// Returns the reference object which names the schema component that should be used to represent the
        /// request body.
        /// </summary>
        /// <param name="requestBody">Request body object.</param>
        /// <returns>OpenApiReference containing the id of the schema component.</returns>
        public static OpenApiReference GetRequestBodyReference(OpenApiRequestBody requestBody)
        {
            return requestBody.Content[CONTENT_TYPE].Schema.Reference;
        }

        /// <summary>
        /// Returns collection of OpenAPI OperationTypes and associated flag indicating whether they are enabled
        /// for the engine's REST endpoint.
        /// Acts as a helper for stored procedures where the runtime config can denote any combination of REST verbs
        /// to enable.
        /// </summary>
        /// <param name="entity">Entity object.</param>
        /// <returns>Collection of OpenAPI OperationTypes and whether they should be created.</returns>
        public static Dictionary<OperationType, bool> ResolveConfiguredOperations(Entity entity)
        {
            Dictionary<OperationType, bool> configuredOperations = new()
            {
                [OperationType.Get] = false,
                [OperationType.Post] = false,
                [OperationType.Put] = false,
                [OperationType.Patch] = false,
                [OperationType.Delete] = false
            };

            List<SupportedHttpVerb> spRestMethods = entity.Rest.Methods.ToList();

            if (spRestMethods is null)
            {
                return configuredOperations;
            }

            foreach (SupportedHttpVerb restMethod in spRestMethods)
            {
                switch (restMethod)
                {
                    case SupportedHttpVerb.Get:
                        configuredOperations[OperationType.Get] = true;
                        break;
                    case SupportedHttpVerb.Post:
                        configuredOperations[OperationType.Post] = true;
                        break;
                    case SupportedHttpVerb.Put:
                        configuredOperations[OperationType.Put] = true;
                        break;
                    case SupportedHttpVerb.Patch:
                        configuredOperations[OperationType.Patch] = true;
                        break;
                    case SupportedHttpVerb.Delete:
                        configuredOperations[OperationType.Delete] = true;
                        break;
                    default:
                        break;
                }
            }

            return configuredOperations;
        }
    }
}
