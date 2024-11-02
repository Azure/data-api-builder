// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Configuration.HotReload;

[TestClass]
public class AuthorizationResolverHotReloadTests
{
    private static TestServer _testServer;
    private static HttpClient _testClient;
    private const string AUTHZ_HR_FILENAME = "authZ-resolver-hotreload.json";
    private const string INITIAL_ENTITY_NAME = "Books";

    /// <summary>
    /// Validates that a hot reload operation signals the AuthorizationResolver to refresh its
    /// internal state by updating the entity permissions with a new entity and new permissions.
    /// DAB "forgets" the initial entity and its permissions and honors the new entity and permissions.
    /// Original config specifies Entity (Book) -> Role1 -> Include: id, publisher_id Exclude: title
    /// Hot Reloaded config specifies: Entity (Publisher) -> Role2 -> Include: id Exclude: name
    /// Hot-Reload tests in this class must not be parallelized as each test overwrites the same config file
    /// and uses the same test server instance.
    /// </summary>
    [TestMethod]
    [DoNotParallelize]
    [TestCategory(TestCategory.MSSQL)]
    public async Task ValidateAuthorizationResolver_HotReload()
    {
        // Arrange
        // Build config that we are swapping with the initial config.
        const string ENTITY_NAME_HR = "Publisher";
        EntityActionFields hrFieldPermissions = new(
            Include: new HashSet<string>()
            {
                "id"
            },
            Exclude: new HashSet<string>()
            {
                "name"
            }
        );

        EntityAction actionForRoleHR = new(
            Action: EntityActionOperation.Read,
            Fields: hrFieldPermissions,
            Policy: null
            );

        EntityPermission permissionsHR = new(
            Role: "Role2",
            Actions: new[] { actionForRoleHR }
        );

        Entity requiredEntityHR = new(
            Source: new("publishers", EntitySourceType.Table, null, null),
            Rest: new(Enabled: true),
            GraphQL: new(Singular: "", Plural: "", Enabled: false),
            Permissions: new[] { permissionsHR },
            Relationships: null,
            Mappings: null);

        Dictionary<string, Entity> entityMap = new()
        {
            { ENTITY_NAME_HR, requiredEntityHR }
        };

        // ACT
        // Update the config file to trigger hot-reload which then notifies
        // AuthorizationResolver to refresh state.
        // DAB takes a few seconds to detect and process the file changes during hot-reload.
        CreateCustomConfigFile(fileName: AUTHZ_HR_FILENAME, entityMap);
        Thread.Sleep(4000);

        // Assert
        // Request #1 - Validate that DAB is hydrated with new entity settings by ensuring
        // the entity is accessible.
        HttpRequestMessage entityValidationRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{ENTITY_NAME_HR}");
        entityValidationRequest.Headers.Add("X-MS-API-ROLE", "Role2");
        HttpResponseMessage hrResponse = await _testClient.SendAsync(entityValidationRequest);
        Assert.AreEqual(
            expected: HttpStatusCode.OK,
            actual: hrResponse.StatusCode,
            message: "After hot-reload, the Publisher entity should be readable using 'Role2'.");

        // Request #2 - Validate that DAB is honoring the new entity "excluded" field config.
        HttpRequestMessage fieldPermissionValidationRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{ENTITY_NAME_HR}?$select=id,name");
        fieldPermissionValidationRequest.Headers.Add("X-MS-API-ROLE", "Role2");
        hrResponse = await _testClient.SendAsync(fieldPermissionValidationRequest);
        Assert.AreEqual(
            expected: HttpStatusCode.Forbidden,
            actual: hrResponse.StatusCode,
            message: "After hot-reload, the Publisher entity's name field should be inaccessible resulting in HTTP403 Forbidden.");

        // Validate explicitly that old settings are no longer honored. 
        HttpRequestMessage obsoletePermissionValidationRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{INITIAL_ENTITY_NAME}");
        hrResponse = await _testClient.SendAsync(obsoletePermissionValidationRequest);
        Assert.AreEqual(
            expected: HttpStatusCode.NotFound,
            actual: hrResponse.StatusCode,
            message: "After hot-reload, the intially configured entity must not be recognized.");
    }

    /// <summary>
    /// Helper function to write custom hot-reload configuration file.
    /// </summary>
    /// <param name="fileName">Name of custom config file to create/write to.</param>
    /// <param name="entityMap">Collection of entityName -> Entity object.</param>
    private static void CreateCustomConfigFile(string fileName, Dictionary<string, Entity> entityMap)
    {
        DataSource dataSource = new(DatabaseType.MSSQL, ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        // Using the Simulator provider enables us to simply add the Role header to the request.
        // HostMode must be development to enable hot-reload.
        HostOptions hostOptions = new(Cors: new(Origins: Array.Empty<string>()), Authentication: new() { Provider = "Simulator" }, Mode: HostMode.Development);

        RuntimeConfig runtimeConfig = new(
            Schema: "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json",
            DataSource: dataSource,
            Runtime: new(
                Rest: new(Enabled: true),
                GraphQL: new(), // GraphQL doesn't yet support hot-reload
                Host: hostOptions
            ),
            Entities: new(entityMap));

        File.WriteAllText(
            path: fileName,
            contents: runtimeConfig.ToJson());
    }

    /// <summary>
    /// Create initial configuration file and start the test server.
    /// The testserver is used in all tests to validate the hot-reload behavior
    /// as each test will overwrite this configuration file.
    /// </summary>
    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext context)
    {
        // Arrange
        EntityActionFields initialFieldPermissions = new(
            Include: new HashSet<string>()
            {
                "id",
                "publisher_id"
            },
            Exclude: new HashSet<string>()
            {
                "title"
            }
        );

        EntityAction actionForRole = new(
            Action: EntityActionOperation.Read,
            Fields: initialFieldPermissions,
            Policy: null
            );

        EntityPermission permissions = new(
            Role: "Role1",
            Actions: new[] { actionForRole }
        );

        // At least one entity is required in the runtime config for the engine to start.
        // Even though this entity is not under test, it must be supplied to the config
        // file creation function.
        Entity requiredEntity = new(
            Source: new("books", EntitySourceType.Table, null, null),
            Rest: new(Enabled: true),
            GraphQL: new(Singular: "", Plural: "", Enabled: false),
            Permissions: new[] { permissions },
            Relationships: null,
            Mappings: null);

        Dictionary<string, Entity> entityMap = new()
        {
            { INITIAL_ENTITY_NAME, requiredEntity }
        };

        CreateCustomConfigFile(fileName: AUTHZ_HR_FILENAME, entityMap);

        string[] args = new[]
        {
            $"--ConfigFileName={AUTHZ_HR_FILENAME}"
        };

        _testServer = new(Program.CreateWebHostBuilder(args));
        _testClient = _testServer.CreateClient();

        // Setup validation - ensure initial config is honored.
        HttpRequestMessage initialRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{INITIAL_ENTITY_NAME}?$select=id,publisher_id");
        initialRequest.Headers.Add("X-MS-API-ROLE", "Role1");
        HttpResponseMessage initialResponse = await _testClient.SendAsync(initialRequest);

        Assert.AreEqual(
            expected: HttpStatusCode.OK,
            actual: initialResponse.StatusCode,
            message: "Initial configuration bootstrap failed.");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (File.Exists(AUTHZ_HR_FILENAME))
        {
            File.Delete(AUTHZ_HR_FILENAME);
        }

        _testServer.Dispose();
        _testClient.Dispose();
    }
}
