# Local Authentication

When developing a solution using Data API builder locally, or when running Data API builder on-premises, you will need to test the configured authentication and authorization options by simulating a request with a specific role or claim.

To simulate an authenticated request without configuring an authentication provider (like Azure AD, for example), you can utilize either the `Simulator` or `StaticWebApps` authentication providers:

## Use the `Simulator` provider

`Simulator` is a configurable authentication provider which instructs the Data API builder engine to treat all requests as authenticated.

- At a minimum, all requests will be evaluated in the context of the system role `Authenticated`.
- If desired, the request will be evaluated in the context of any role denoted in the `X-MS-API-ROLE` Http header.
>[!NOTE] While the desired role will be honored, authorization permissions defining database policies will not work because custom claims can't be set for the authenticated user with the `Simulator` provider. Continue to the section [Use the `StaticWebApps` provider](#use-the-staticwebapps-provider) for testing database authorization policies.

### 1. Update the runtime configuration authentication provider

Make sure that in the configuration file you are using the `Simulator` authentication provider and `development` mode is specified. The `host` configuration section should look like the following:

```json
"host": {
  "mode": "development",
  "authentication": {
    "provider": "Simulator"
  }
}
```

### 2. Specify the role context of the request

With `Simulator` as Data API builder's authentication provider, no custom header is necessary to set the role context to the system role `Authenticated`:

```bash
curl --request GET \
  --url http://localhost:5000/api/books \
```

To set the role context to any other role, including the system role `Anonymous`, the `X-MS-API-ROLE` header must be included with the desired role:

```bash
curl --request GET \
  --url http://localhost:5000/api/books \
  --header 'X-MS-API-ROLE: author' \
```

## Use the `StaticWebApps` provider

The `StaticWebApps` authentication provider instructs Data API builder to look for a set of Http headers only present when running within a Static Web Apps environment. Such Http headers can be set by the client when running locally to simulate an authenticated user including any role membership or custom claims.

>[!NOTE] Client provided instances of the Http header, `x-ms-client-principal`, will only work when developing locally because production Azure Static Web Apps environments [drop all client provided instances](https://learn.microsoft.com/azure/static-web-apps/user-information?tabs=javascript#:~:text=When%20a%20user%20is%20logged%20in%2C%20the%20x%2Dms%2Dclient%2Dprincipal%20header%20is%20added%20to%20the%20requests%20for%20user%20information%20via%20the%20Static%20Web%20Apps%20edge%20nodes.) of that header.

Make sure that in the configuration file you are using the `StaticWebApps` authentication provider. The `host` configuration section should look like the following:

```json
"host": {
  "mode": "development",
  "authentication": {
    "provider": "StaticWebApps"
  }
}
```

### 1. Send requests providing a generated `X-MS-CLIENT-PRINCIPAL` header

Once Data API builder is running locally and configured to use the `StaticWebApps` authentication provider, you can generate a client principal object manually using the following template:

```json
{  
  "identityProvider": "test",
  "userId": "12345",
  "userDetails": "john@contoso.com",
  "userRoles": ["author", "editor"]
}
```

Static Web App's [authenticated user metadata](https://learn.microsoft.com/azure/static-web-apps/user-information?tabs=javascript#client-principal-data) has the following properties:

|Property|Description|
|---|---|
|identityProvider|Any string value.|
|userId|A unique identifier for the user.|
|userDetails|Username or email address of the user.|
|userRoles|An array of the user's assigned roles.|

>[!NOTE] As noted in [Static Web Apps documentation](https://learn.microsoft.com/azure/static-web-apps/user-information?tabs=javascript#:~:text=The%20x%2Dms%2Dclient%2Dprincipal%20header%20accessible%20in%20the%20API%20function%20does%20not%20contain%20the%20claims%20array.), the `x-ms-client-principal` header does not contain the `claims` array.

In order to be passed with the `X-MS-CLIENT-PRINCIPAL` header, the JSON payload must be Base64-encoded. You can use any online or offline tool to do that. One such tool is [DevToys](https://github.com/veler/DevToys). A sample Base64 encoded payload that represents the JSON previously referenced:

```text
eyAgCiAgImlkZW50aXR5UHJvdmlkZXIiOiAidGVzdCIsCiAgInVzZXJJZCI6ICIxMjM0NSIsCiAgInVzZXJEZXRhaWxzIjogImpvaG5AY29udG9zby5jb20iLAogICJ1c2VyUm9sZXMiOiBbImF1dGhvciIsICJlZGl0b3IiXQp9
```

The following cURL request simulates an authenticated user retrieving the list of available `book` entity records in the context of the `author` role:

```bash
curl --request GET \
  --url http://localhost:5000/api/books \
  --header 'X-MS-API-ROLE: author' \
  --header 'X-MS-CLIENT-PRINCIPAL: eyAgCiAgImlkZW50aXR5UHJvdmlkZXIiOiAidGVzdCIsCiAgInVzZXJJZCI6ICIxMjM0NSIsCiAgInVzZXJEZXRhaWxzIjogImpvaG5AY29udG9zby5jb20iLAogICJ1c2VyUm9sZXMiOiBbImF1dGhvciIsICJlZGl0b3IiXQp9'
