// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.OpenApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Focused unit tests for the OpenAPI stored-procedure GET query-parameter generation
    /// path. The full integration tests in <c>OpenApiDocumentor/StoredProcedureGeneration</c>
    /// require a live MSSQL fixture; these tests exercise the description-augmentation
    /// logic in isolation via the internal-visible
    /// <see cref="OpenApiDocumentor.AddStoredProcedureInputParameters"/> helper.
    /// </summary>
    [TestClass]
    public class OpenApiStoredProcedureInputParametersTests
    {
        /// <summary>
        /// Per spec #3331, the OpenAPI document must surface the auto-embed indicator
        /// on every parameter exposure path (POST body, GET query string, GraphQL,
        /// MCP). Stage 3.17 added the indicator to the POST body and GraphQL paths;
        /// this test pins the GET query-parameter path.
        ///
        /// Setup: a sproc with two params, one marked auto-embed.
        /// Assert: the auto-embed query parameter's description contains the auto-embed
        /// indicator; the non-auto-embed parameter's description does not.
        /// </summary>
        [TestMethod]
        public void AddStoredProcedureInputParameters_AutoEmbedParam_DescriptionContainsIndicator()
        {
            StoredProcedureDefinition spDefinition = new()
            {
                Parameters = new Dictionary<string, ParameterDefinition>
                {
                    ["queryText"] = new() { SystemType = typeof(string) },
                    ["topK"] = new() { SystemType = typeof(int) }
                }
            };

            List<ParameterMetadata> configParams = new()
            {
                new ParameterMetadata { Name = "queryText", AutoEmbed = true },
                new ParameterMetadata { Name = "topK", AutoEmbed = false }
            };

            OpenApiOperation operation = new();
            OpenApiDocumentor.AddStoredProcedureInputParameters(operation, spDefinition, configParams);

            OpenApiParameter autoParam = operation.Parameters.Single(p => p.Name == "queryText");
            StringAssert.Contains(autoParam.Description, "auto-embed",
                "Auto-embed query parameter description should contain the auto-embed indicator.");

            OpenApiParameter normalParam = operation.Parameters.Single(p => p.Name == "topK");
            Assert.IsFalse(normalParam.Description.Contains("auto-embed"),
                "Non-auto-embed query parameter description must not contain the auto-embed indicator.");
        }

        /// <summary>
        /// Backward-compat: when configParams is null (path used by upstream callers that
        /// don't have config metadata available), the original generic description is used
        /// for every parameter — no auto-embed indicator is added.
        /// </summary>
        [TestMethod]
        public void AddStoredProcedureInputParameters_NullConfigParams_UsesBaseDescription()
        {
            StoredProcedureDefinition spDefinition = new()
            {
                Parameters = new Dictionary<string, ParameterDefinition>
                {
                    ["queryText"] = new() { SystemType = typeof(string) }
                }
            };

            OpenApiOperation operation = new();
            OpenApiDocumentor.AddStoredProcedureInputParameters(operation, spDefinition, configParams: null);

            OpenApiParameter param = operation.Parameters.Single(p => p.Name == "queryText");
            Assert.IsFalse(param.Description.Contains("auto-embed"),
                "When configParams is null, no auto-embed indicator should appear.");
        }
    }
}
