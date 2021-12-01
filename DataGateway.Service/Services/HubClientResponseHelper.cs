using Azure.DataGateway.Service.configurations;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Services
{
    public sealed class DataGatewayServiceResponse
    {
        public string Response { get; set; }

        public HttpStatusCode StatusCode { get; set; }

        public Dictionary<string, string[]> Headers { get; set; }
    }

    public abstract class BaseRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        public abstract Task<DataGatewayServiceResponse> ProcessHubRequestAsync(HttpClient httpClient);

        public string baseUri = $"http://localhost:{Environment.GetEnvironmentVariable("DATAGATEWAY_PORT") ?? "7071"}";
    }

    public class HubClientResponseHelper<T> where T : BaseRequest
    {

        public Func<string, Task> SendResponseToHub { get; }

        public HubClientResponseHelper(HubConnection connection, HttpClient httpClient)
        {
            this.SendResponseToHub = async (hubRequestString) =>
            {
                string requestType = typeof(T).Name;
                T hubRequest = JsonConvert.DeserializeObject<T>(hubRequestString);
                try
                {
                    DataGatewayServiceResponse notebookServiceResponse = await hubRequest.ProcessHubRequestAsync(httpClient);
                    if (notebookServiceResponse != null)
                    {
                        await connection.InvokeAsync(Constants.HubMethodNames.ProcessContainerHttpResponse, hubRequest.Id, notebookServiceResponse.Response, notebookServiceResponse.StatusCode, JsonConvert.SerializeObject(notebookServiceResponse.Headers));
                    }
                }
                catch (Exception e)
                {
                    await connection.InvokeAsync(Constants.HubMethodNames.ProcessContainerHttpResponse, hubRequest.Id, e.Message, HttpStatusCode.InternalServerError, null);
                }
            };
        }
    }
}
