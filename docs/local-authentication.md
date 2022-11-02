# Local Authentication

When developing a solution using Data API Builder locally, or when running Data API Builder on-premises, you will need to test the configured authentication and authorization options by simulating a request with a specific role or claim.

To simulate an authenticated request without configuring an authentication provider (like Azure AD, for example), you can utilize either the `Simulator` or `StaticWebApps` authentication providers:

## Use the `Simulator` provider

`Simulator` is a configurable authentication provider which instructs the Data API Builder engine to treat all requests as authenticated.

- At a minimum, all requests will be evaluated in the context of the system role `Authenticated`.
- If desired, the request will be evaluated in the context of any role denoted in the `X-MS-API-ROLE` Http header.
  - Note: While the desired role will be honored, authorization permissions defining database policies will not work because custom claims can't be set for the authenticated user with the `Simulator` provider. Continue to the section [Use the `StaticWebApps` provider](#use-the-staticwebapps-provider) for testing database authorization policies.

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

With `Simulator` as Data API Builder's authentication provider, no custom header is necessary to set the role context to the system role `Authenticated`:

```bash
curl --request GET \
  --url http://localhost:5000/api/books \
```

To set the role context to any other role, including the system role `Anonymous`, the X-MS-API-ROLE header must be included with the desired role:

```bash
curl --request GET \
  --url http://localhost:5000/api/books \
  --header 'X-MS-API-ROLE: author' \
```

## Use the `StaticWebApps` provider

The `StaticWebApps` authentication provider instructs Data API Builder to look for a set of Http headers only present when running within a Static Web Apps environment. Such Http headers can be set by the client when running locally to simulate an authenticated user including any role membership or custom claims.

- **Note:** The client defined Http headers described in this section will only work locally [because they would be dropped](https://learn.microsoft.com/en-us/azure/static-web-apps/user-information?tabs=javascript#:~:text=When%20a%20user%20is%20logged%20in%2C%20the%20x%2Dms%2Dclient%2Dprincipal%20header%20is%20added%20to%20the%20requests%20for%20user%20information%20via%20the%20Static%20Web%20Apps%20edge%20nodes.) when in a real Static Web Apps environment.

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

Once Data API Builder is running locally and configured to use the `StaticWebApps` authentication provider, you can generate a client principal object manually using the following template:

```json
{  
  "identityProvider": "test",
  "userId": "12345",
  "userDetails": "john@contoso.com",
  "userRoles": ["author", "editor"],
  "claims": [{
    "typ": "FirstName",
    "val": "John"
  },
  {
    "typ": "LastName",
    "val": "Doe"
  }]
}
```

Expected elements of the client principal object are the following:

|Property|Description|
|---|---|
|identityProvider|Any string value.|
|userId|A unique identifier for the user.|
|userDetails|Username or email address of the user.|
|userRoles|An array of the user's assigned roles.|
|claims|An array of claims returned by your custom authentication provider.|

In order to be passed into the X-MS-CLIENT-PRINCIPAL header, the JSON data must be Base64-encoded. You can use any online or offline tool to do that. A recommended tool is the [DevToys](https://github.com/veler/DevToys) tool. A sample Base64 encoded payload that contains the JSON used before as example is the following:

```text
eyAgDQogICJpZGVudGl0eVByb3ZpZGVyIjogInRlc3QiLA0KICAidXNlcklkIjogIjEyMzQ1IiwNCiAgInVzZXJEZXRhaWxzIjogImpvaG5AY29udG9zby5jb20iLA0KICAidXNlclJvbGVzIjogWyJhdXRob3IiLCAiZWRpdG9yIl0sDQogICJjbGFpbXMiOiBbew0KICAgICJ0eXAiOiAiRmlyc3ROYW1lIiwNCiAgICAidmFsIjogIkpvaG4iDQogIH0sDQogIHsNCiAgICAidHlwIjogIkxhc3ROYW1lIiwNCiAgICAidmFsIjogIkRvZSINCiAgfV0NCn0=
```

a sample cURL request to simulate an authenticated request to retrieve the list of available element in the `book` entity, using the `author` role is the following:

```bash
curl --request GET \
  --url http://localhost:5000/api/books \
  --header 'X-MS-API-ROLE: author' \
  --header 'X-MS-CLIENT-PRINCIPAL: eyAgDQogICJpZGVudGl0eVByb3ZpZGVyIjogInRlc3QiLA0KICAidXNlcklkIjogIjEyMzQ1IiwNCiAgInVzZXJEZXRhaWxzIjogImpvaG5AY29udG9zby5jb20iLA0KICAidXNlclJvbGVzIjogWyJyb2xlMSIsICJyb2xlMiIsICJhdXRob3IiXSwNCiAgImNsYWltcyI6IFt7DQogICAgInR5cCI6ICJGaXJzdE5hbWUiLA0KICAgICJ2YWwiOiAiSm9obiINCiAgfSwNCiAgew0KICAgICJ0eXAiOiAiTGFzdE5hbWUiLA0KICAgICJ2YWwiOiAiRG9lIg0KICB9XQ0KfQ=='
