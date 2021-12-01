using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Services
{
    public sealed class WebSocketCreateRequest : BaseRequest
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("authToken")]
        public string AuthToken { get; set; }

        [JsonProperty("cookie")]
        public string Cookie { get; set; }

        [JsonProperty("webSocketId")]
        public string WebSocketId { get; set; }

        public override async Task<DataGatewayServiceResponse> ProcessHubRequestAsync(HttpClient httpClient)
        {
            var targetSocket = new ClientWebSocket();
            if (!string.IsNullOrEmpty(this.AuthToken))
            {
                targetSocket.Options.SetRequestHeader("Authorization", $"Token {this.AuthToken}");
            }

            if (!string.IsNullOrEmpty(this.Cookie))
            {
                targetSocket.Options.SetRequestHeader("Cookie", this.Cookie);
            }

            string uri = baseUri + this.Path;
            var targetKernelsUri = new Uri(uri);

            var websocketUriBuilder = new UriBuilder(targetKernelsUri)
            {
                Scheme = (targetKernelsUri.Scheme == Uri.UriSchemeHttps) ? "wss" : "ws",
                Port = targetKernelsUri.Port
            };
            await targetSocket.ConnectAsync(websocketUriBuilder.Uri, CancellationToken.None);
            // TODO: Probably need to implement this :D
            //WebSocketResponseSender.Current.ReceiveMessageAsync(this.WebSocketId, targetSocket);
            //WebSocketsManager.Current.AddWebSocket(this.WebSocketId, targetSocket);
            return new DataGatewayServiceResponse { Response = "", StatusCode = HttpStatusCode.OK };
        }
    }
}
