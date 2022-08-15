# Authentication

Data API builder allows developers to define the authentication mechanisms they want to use to authenticate incoming requests.

Authentication is not performed by Data API builder, but is delegated to one of the supported authentication providers. The supported authentication providers are:

- EasyAuth
- JWT

## EasyAuth

When using this provider, Data API builder will expect to EasyAuth to have authenticated the request, and to have authentication data available in the `X-MS-CLIENT-PRINCIPAL` HTTP header, as described here: [Work with user identities in Azure App Service authentication](https://docs.microsoft.com/en-us/azure/app-service/configure-authentication-user-identities).

To use this provider you need to specify the following configuration in the `runtime.host` section of the configuration file:

```json
"authentication": {
    "provider": "StaticWebApps"
}
```

Using the EasyAuth provider is useful when you plan to run Data API builder in Azure, hosting it using an App Service and running it in a continer: [Run a custom container in Azure](https://docs.microsoft.com/en-us/azure/app-service/quickstart-custom-container?tabs=dotnet&pivots=container-linux-vscode).

## JWT

To use the JWT provider, you need to configuration the `runtime.host` by providing the needed information to verify the recived JWT token:

```json
"authentication": {
    "provider": "AzureAD",
    "jwt": {
        "audience": "<APP_ID>",
        "issuer": "https://login.microsoftonline.com/<AZURE_AD_TENANT_ID>/v2.0"
    }
}
```

## Roles Selection

Once a request has been authenticated via any of the available mechanisms, the roles defined in the claims will be used to determine how permission rules will be applied to [authorize](./authorization.md) the request.

## Anonymous Request

Request can also be made without being authenticated. In such case the request will be automatically assigned to the `anonymous` system role so that it can be properly [authorized](./authorization.md).

