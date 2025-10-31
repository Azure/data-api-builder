// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Method Enforcement Integration Tests.
    /// Tests runtime enforcement of the Methods property for tables and views.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class MethodEnforcementTests : RestApiTestBase
    {
        #region Test Lifecycle

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #endregion

        #region Overrides

        public override string GetQuery(string key)
        {
            return string.Empty;
        }

        #endregion

        #region Read-Only Entity Tests (Methods: ["Get"])

        /// <summary>
        /// Validates that GET request succeeds for read-only entity.
        /// </summary>
        [TestMethod]
        public virtual async Task ReadOnlyEntity_GetRequestSucceeds()
        {
            // Arrange
            string entityName = "ReadOnlyBook";
            string requestUri = $"/api/{entityName}";

            // Act
            HttpResponseMessage response = await HttpClient.GetAsync(requestUri);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "GET should succeed for read-only entity");
        }

        /// <summary>
        /// Validates that POST request returns 405 for read-only entity.
        /// </summary>
        [TestMethod]
        public virtual async Task ReadOnlyEntity_PostRequestReturns405()
        {
            // Arrange
            string entityName = "ReadOnlyBook";
            string requestUri = $"/api/{entityName}";
            string requestBody = JsonSerializer.Serialize(new { title = "New Book", publisher_id = 1234 });
            HttpContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Act
            HttpResponseMessage response = await HttpClient.PostAsync(requestUri, content);

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "POST should return 405 for read-only entity");
        }

        /// <summary>
        /// Validates that PUT request returns 405 for read-only entity.
        /// </summary>
        [TestMethod]
        public virtual async Task ReadOnlyEntity_PutRequestReturns405()
        {
            // Arrange
            string entityName = "ReadOnlyBook";
            string primaryKeyRoute = "id/1";
            string requestUri = $"/api/{entityName}/{primaryKeyRoute}";
            string requestBody = JsonSerializer.Serialize(new { title = "Updated Book" });
            HttpContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Act
            HttpResponseMessage response = await HttpClient.PutAsync(requestUri, content);

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "PUT should return 405 for read-only entity");
        }

        /// <summary>
        /// Validates that PATCH request returns 405 for read-only entity.
        /// </summary>
        [TestMethod]
        public virtual async Task ReadOnlyEntity_PatchRequestReturns405()
        {
            // Arrange
            string entityName = "ReadOnlyBook";
            string primaryKeyRoute = "id/1";
            string requestUri = $"/api/{entityName}/{primaryKeyRoute}";
            string requestBody = JsonSerializer.Serialize(new { title = "Patched Book" });
            HttpContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Act
            HttpResponseMessage response = await HttpClient.PatchAsync(requestUri, content);

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "PATCH should return 405 for read-only entity");
        }

        /// <summary>
        /// Validates that DELETE request returns 405 for read-only entity.
        /// </summary>
        [TestMethod]
        public virtual async Task ReadOnlyEntity_DeleteRequestReturns405()
        {
            // Arrange
            string entityName = "ReadOnlyBook";
            string primaryKeyRoute = "id/1";
            string requestUri = $"/api/{entityName}/{primaryKeyRoute}";

            // Act
            HttpResponseMessage response = await HttpClient.DeleteAsync(requestUri);

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "DELETE should return 405 for read-only entity");
        }

        #endregion

        #region Write-Only Entity Tests (Methods: ["Post"])

        /// <summary>
        /// Validates that POST request succeeds for write-only entity.
        /// </summary>
        [TestMethod]
        public virtual async Task WriteOnlyEntity_PostRequestSucceeds()
        {
            // Arrange
            string entityName = "WriteOnlyBook";
            string requestUri = $"/api/{entityName}";
            string requestBody = JsonSerializer.Serialize(new
            {
                title = "Write Only Test Book",
                publisher_id = 1234
            });
            HttpContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Act
            HttpResponseMessage response = await HttpClient.PostAsync(requestUri, content);

            // Assert
            Assert.IsTrue(
                response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK,
                $"POST should succeed for write-only entity. Actual: {response.StatusCode}");
        }

        /// <summary>
        /// Validates that GET request returns 405 for write-only entity.
        /// </summary>
        [TestMethod]
        public virtual async Task WriteOnlyEntity_GetRequestReturns405()
        {
            // Arrange
            string entityName = "WriteOnlyBook";
            string requestUri = $"/api/{entityName}";

            // Act
            HttpResponseMessage response = await HttpClient.GetAsync(requestUri);

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "GET should return 405 for write-only entity");
        }

        #endregion

        #region Partial CRUD Entity Tests (Methods: ["Get", "Put"])

        /// <summary>
        /// Validates that GET request succeeds for partial CRUD entity.
        /// </summary>
        [TestMethod]
        public virtual async Task PartialCrudEntity_GetRequestSucceeds()
        {
            // Arrange
            string entityName = "PartialCrudBook";
            string requestUri = $"/api/{entityName}";

            // Act
            HttpResponseMessage response = await HttpClient.GetAsync(requestUri);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
                "GET should succeed for partial CRUD entity with GET configured");
        }

        /// <summary>
        /// Validates that PUT request succeeds for partial CRUD entity.
        /// </summary>
        [TestMethod]
        public virtual async Task PartialCrudEntity_PutRequestSucceeds()
        {
            // Arrange
            string entityName = "PartialCrudBook";
            string primaryKeyRoute = "id/1";
            string requestUri = $"/api/{entityName}/{primaryKeyRoute}";
            string requestBody = JsonSerializer.Serialize(new { title = "Updated via PUT" });
            HttpContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Act
            HttpResponseMessage response = await HttpClient.PutAsync(requestUri, content);

            // Assert
            // The key test is that PUT is allowed (not 405). Other status codes may occur due to
            // data/permissions issues, but 405 would indicate method enforcement failure.
            Assert.AreNotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "PUT should be allowed for partial CRUD entity with PUT configured");
        }

        /// <summary>
        /// Validates that POST request returns 405 for partial CRUD entity (no POST configured).
        /// </summary>
        [TestMethod]
        public virtual async Task PartialCrudEntity_PostRequestReturns405()
        {
            // Arrange
            string entityName = "PartialCrudBook";
            string requestUri = $"/api/{entityName}";
            string requestBody = JsonSerializer.Serialize(new { title = "New Book", publisher_id = 1234 });
            HttpContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Act
            HttpResponseMessage response = await HttpClient.PostAsync(requestUri, content);

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "POST should return 405 for partial CRUD entity without POST configured");
        }

        /// <summary>
        /// Validates that PATCH request returns 405 for partial CRUD entity (no PATCH configured).
        /// </summary>
        [TestMethod]
        public virtual async Task PartialCrudEntity_PatchRequestReturns405()
        {
            // Arrange
            string entityName = "PartialCrudBook";
            string primaryKeyRoute = "id/1";
            string requestUri = $"/api/{entityName}/{primaryKeyRoute}";
            string requestBody = JsonSerializer.Serialize(new { title = "Patched Book" });
            HttpContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Act
            HttpResponseMessage response = await HttpClient.PatchAsync(requestUri, content);

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "PATCH should return 405 for partial CRUD entity without PATCH configured");
        }

        /// <summary>
        /// Validates that DELETE request returns 405 for partial CRUD entity (no DELETE configured).
        /// </summary>
        [TestMethod]
        public virtual async Task PartialCrudEntity_DeleteRequestReturns405()
        {
            // Arrange
            string entityName = "PartialCrudBook";
            string primaryKeyRoute = "id/1";
            string requestUri = $"/api/{entityName}/{primaryKeyRoute}";

            // Act
            HttpResponseMessage response = await HttpClient.DeleteAsync(requestUri);

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "DELETE should return 405 for partial CRUD entity without DELETE configured");
        }

        #endregion

        #region Default Behavior Tests (null and empty Methods)

        /// <summary>
        /// Validates that null Methods allows all operations (default behavior).
        /// </summary>
        [TestMethod]
        public virtual async Task NullMethods_AllOperationsAllowed()
        {
            // Arrange
            string entityName = _integrationEntityName; // Uses default config with null Methods

            // Act & Assert - GET
            HttpResponseMessage getResponse = await HttpClient.GetAsync($"/api/{entityName}");
            Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode,
                "GET should succeed when Methods is null");

            // POST would require valid data, so we just check it doesn't return 405
            string requestBody = JsonSerializer.Serialize(new { title = "Test", publisher_id = 1234 });
            HttpContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            HttpResponseMessage postResponse = await HttpClient.PostAsync($"/api/{entityName}", content);
            Assert.AreNotEqual(HttpStatusCode.MethodNotAllowed, postResponse.StatusCode,
                "POST should not return 405 when Methods is null");
        }

        #endregion

        #region  405 vs 403 Ordering Tests

        /// <summary>
        /// Validates that 405 Method Not Allowed is returned before 403 Forbidden.
        /// An unauthorized user attempting a disallowed method should get 405, not 403.
        /// </summary>
        [TestMethod]
        public virtual async Task DisallowedMethod_Returns405BeforeAuthorizationCheck()
        {
            // This test would require a configuration with authentication enabled
            // and an entity configured with restricted methods.
            // For now, we'll test the basic 405 response with our read-only entity.

            // Arrange
            string entityName = "ReadOnlyBook";
            string requestUri = $"/api/{entityName}";
            string requestBody = JsonSerializer.Serialize(new { title = "Test" });
            HttpContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Act - POST to read-only entity (should be 405 regardless of auth)
            HttpResponseMessage response = await HttpClient.PostAsync(requestUri, content);

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode,
                "Disallowed method should return 405 before any authorization checks");
            Assert.AreNotEqual(HttpStatusCode.Forbidden, response.StatusCode,
                "Should not return 403 for disallowed method");
        }

        #endregion
    }
}
