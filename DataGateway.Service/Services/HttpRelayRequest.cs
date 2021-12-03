using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Services
{
    public sealed class HttpRelayRequest : BaseRequest
    {
        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("authHeader")]
        public string AuthHeader { get; set; }

        public override async Task<DataGatewayServiceResponse> ProcessHubRequestAsync(HttpClient httpClient)
        {
            AuthenticationHeaderValue authToken = null;
            if (!string.IsNullOrEmpty(this.AuthHeader))
            {
                string[] authHeaderParts = this.AuthHeader.Split(" ");
                authToken = new AuthenticationHeaderValue(authHeaderParts[0], authHeaderParts[1]);
            }

            httpClient.DefaultRequestHeaders.Authorization = authToken;

            HttpResponseMessage response = null;
            HttpContent content = null;

            string uri = baseUri + this.Path;

            if (this.Content != null)
            {
                content = new StringContent(this.Content, Encoding.UTF8, "application/json");
            }

            if (this.Method == HttpMethod.Get.ToString())
            {
                response = await httpClient.GetAsync(uri);
            }
            else if (this.Method == HttpMethod.Post.ToString())
            {
                response = await httpClient.PostAsync(uri, content);
            }
            else if (this.Method == HttpMethod.Delete.ToString())
            {
                response = await httpClient.DeleteAsync(uri);
            }
            else if (this.Method == HttpMethod.Put.ToString())
            {
                response = await httpClient.PutAsync(uri, content);
            }
            else if (this.Method == HttpMethod.Patch.ToString())
            {
                response = await httpClient.PatchAsync(uri, content);
            }

            string result = await response.Content.ReadAsStringAsync();
            var headers = new Dictionary<string, string[]>();
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            return new DataGatewayServiceResponse { Response = result, StatusCode = response.StatusCode, Headers = headers };
        }
    }
}
