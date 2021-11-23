using Azure.DataGateway.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Functions
{
    /// <summary>
    /// This hosts the function that runs when we send a request to /{entityName}/{primarykey}/{value}.
    /// The equivalent of the RestController in the DotNetCore project.
    /// </summary>
    public class RestApis
    {
        /// <summary>
        /// Service providing REST Api executions.
        /// </summary>
        private readonly RestService _restService;

        public RestApis(RestService restService)
        {
            this._restService = restService;
        }

        /// <summary>
        /// Function for FindById GET request. Equivalent of the FindById action in the RestController.
        /// </summary>
        /// <param name="req">http request</param>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="primaryKeyRoute">The string representing the primary key route
        /// primary_key = [shard_value/]id_key_value
        /// Expected URL template is of the following form:
        /// CosmosDb: URL template: /<EntityName></EntityName>/[<shard_key>/<shard_value>]/[<id_key>/]<id_key_value>
        /// MsSql/PgSql: URL template: /<EntityName>/[<primary_key_column_name>/<primary_key_value>
        /// <returns></returns>
        [Function("FindById")]
        public async Task<JsonDocument> FindById(
            [HttpTrigger(AuthorizationLevel.Function, "GET", Route = "{entityName}/{*primaryKeyRoute}")] HttpRequestData req,
            string entityName,
            string primaryKeyRoute)
        {
            string queryString = string.Empty;
            if (req.Url is not null)
            {
                queryString = req.Url.Query;
            }

            JsonDocument resultJson = await _restService.ExecuteFindAsync(entityName, primaryKeyRoute, queryString);
            return resultJson;
        }
    }
}
