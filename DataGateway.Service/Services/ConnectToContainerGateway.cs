using Azure.DataGateway.Service.configurations;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service
{
    public enum ConnectionStatus
    {
        Connecting,
        Connected,
        Disconnected,
    }

    public sealed class AllocateGatewayToContainerResponse
    {
        [JsonProperty("gatewayAddress")]
        public string GatewayAddress { get; set; }

        [JsonProperty("gatewayAuthJwtToken")]
        public string GatewayAuthJwtToken { get; set; }
    }

    public class ConnectToContainerGateway
    {
        public static HubConnection connection;
        private static HttpClient _httpClient;
        private ConnectionStatus _webSocketConnectionStatus;

        public ConnectToContainerGateway()
        {
            _webSocketConnectionStatus = ConnectionStatus.Disconnected;
            _httpClient = new HttpClient();
        }

        private void BuildHubConnection(string containerGatewayNodeUrl, string containerBootstrapToken, IConfiguration configuration)
        {
            string hubUrl = "";
            // Only used in development environment
            string containerGatewayUrlFromAppSettings = configuration["ContainerGatewayUrl"];
            if (!string.IsNullOrEmpty(containerGatewayUrlFromAppSettings))
            {
                hubUrl = $"{containerGatewayUrlFromAppSettings}api/containergateway/containerconnection";
            }
            else
            {
                hubUrl = $"{containerGatewayNodeUrl}api/containergateway/containerconnection";
            }

            connection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(containerBootstrapToken);
                        options.Headers.Add("Container-Type", "Hawaii");
                    })
                    .Build();

            connection.ServerTimeout = TimeSpan.FromMinutes(Constants.webSocketServerTimeoutInMinutes);

            connection.Closed += (error) =>
            {
                //this.logger.LogInformation("Websocket connection closed.");
                _webSocketConnectionStatus = ConnectionStatus.Disconnected;
                return Task.FromResult<object>(null);
            };

            connection.On(Constants.HubMethodNames.ContainerRegistered, () =>
            {
                //this.logger.LogInformation("Websocket connected.");
                _webSocketConnectionStatus = ConnectionStatus.Connected;
            });

            connection.On(typeof(HttpRelayRequest).Name,
                new HubClientResponseHelper<HttpRelayRequest>(connection, _httpClient).SendResponseToHub);

            //connection.On(typeof(WebSocketCreateRequest).Name,
            //    new HubClientResponseHelper<WebSocketCreateRequest>(connection, _httpClient).SendResponseToHub);

            //connection.On(typeof(WebSocketMessage).Name,
            //    new HubClientResponseHelper<WebSocketMessage>(connection, _httpClient).SendResponseToHub);
        }

        public async Task RunAsync(CancellationToken stoppingToken, IConfiguration configuration)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (_webSocketConnectionStatus == ConnectionStatus.Connected)
                {
                    //this.logger.LogInformation("Websocket already connected");
                    return;
                }

                if (_webSocketConnectionStatus == ConnectionStatus.Connecting)
                {
                    //this.logger.LogInformation("Websocket is trying to connect.");
                    return;
                }

                _webSocketConnectionStatus = ConnectionStatus.Connecting;
                //this.logger.LogInformation("Connecting web socket ...");

                string authToken = ContainerGatewayUtils.GetAuthTokenFromFileAsync(configuration);

                if (!string.IsNullOrEmpty(authToken))
                {
                    var authHeader = new AuthenticationHeaderValue("Bearer", authToken);
                    _httpClient.DefaultRequestHeaders.Authorization = authHeader;
                }

                string hubAssignmentUrl = $"{configuration["ControlPlaneUrl"]}api/controlplane/containerpooling/allocategateway";

                string allocateGatewayToContainerResponseString = await _httpClient.GetStringAsync(hubAssignmentUrl);
                if (allocateGatewayToContainerResponseString != null)
                {
                    AllocateGatewayToContainerResponse allocateGatewayToContainerResponse = JsonConvert.DeserializeObject<AllocateGatewayToContainerResponse>(allocateGatewayToContainerResponseString);
                    ContainerGatewayUtils.RewriteAuthTokenFile(allocateGatewayToContainerResponse.GatewayAuthJwtToken, configuration);
                    this.BuildHubConnection(allocateGatewayToContainerResponse.GatewayAddress, allocateGatewayToContainerResponse.GatewayAuthJwtToken, configuration);
                }
                else
                {
                    throw new Exception("Container gateway node was not assigned.");
                }

                await connection.StartAsync(stoppingToken);

                //WebSocketResponseSender.Current.SetHubConnection(connection);
            }
            catch (Exception)
            {
                _webSocketConnectionStatus = ConnectionStatus.Disconnected;
                //this.logger.LogError($"Websocket registration failed - {e}");
            }
        }
    }
}
