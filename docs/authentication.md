# Authentication

Data API builder allows developers to define the authentication mechanism (identity provider) they want Data API builder to use to authenticate who is making requests.

Authentication is delegated to a supported identity provider where access token can be issued. An acquired access token must be included with incoming requests to Data API builder. Data API builder then validates any presented access tokens, ensuring that Data API builder was the intended audience of the token.

The supported identity provider configuration options are:

- StaticWebApps
- JWT

## Azure Static Web Apps authentication (EasyAuth)

When using the option `StaticWebApps`, Data API builder will expect Azure Static Web Apps authentication (EasyAuth) to have authenticated the request, and to have provided metadata about the authenticated user in the `X-MS-CLIENT-PRINCIPAL` HTTP header. The authenticated user metadata provided by Static Web Apps can be referenced in the following documentation: [Accessing User Information](https://learn.microsoft.com/azure/static-web-apps/user-information?tabs=csharp).

To use the `StaticWebApps` provider you need to specify the following configuration in the `runtime.host` section of the configuration file:

```json
"authentication": {
    "provider": "StaticWebApps"
}
```

Using the `StaticWebApps` provider is useful when you plan to run Data API builder in Azure, hosting it using App Service and running it in a container: [Run a custom container in Azure App Service](https://learn.microsoft.com/azure/app-service/quickstart-custom-container?tabs=dotnet&pivots=container-linux-vscode).

## JWT

To use the JWT provider, you need to configure the `runtime.host.authentication` section by providing the needed information to verify the received JWT token:

```json
"authentication": {
    "provider": "AzureAD",
    "jwt": {
        "audience": "<APP_ID>",
        "issuer": "https://login.microsoftonline.com/<AZURE_AD_TENANT_ID>/v2.0"
    }
}
```

The supported providers are the following:

- [Azure AD](./authentication-azure-ad.md)

## Roles Selection

Once a request has been authenticated via any of the available options, the roles defined in the token will be used to help determine how permission rules will be evaluated to [authorize](./authorization.md) the request. Any authenticated request will be automatically assigned to the `authenticated` system role, unless a user role is requested to be used, as described in the [Authorization](./authorization.md.md) document.

## Anonymous Requests

Requests can also be made without being authenticated. In such case the request will be automatically assigned to the `anonymous` system role so that it can be properly [authorized](./authorization.md).
