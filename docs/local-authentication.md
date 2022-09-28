# Local Authentication

When developing a solution using Data API Builder locally, or when running Data API Builder on-premises, you will need to test the configured authentication and authorization options, by simulating a request with a specific role or claim.

To simulate authenticated request without having the need to set up a full integration with an authentication provider (like Azure AD, for example), you can use the following steps:

## 1. Use the `StaticWebApps` provider

Make sure that in the configuration file you are using the `StaticWebApps` provider. The `host` configuration section should look like the following:

```json
"host": {
  "mode": "development",
  "authentication": {
    "provider": "StaticWebApps"
  }
}
```

## 2. Issue requests providing a generate `X-MS-CLIENT-PRINCIPAL` header

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

In order to be passed into the X-MS-CLIENT-PRINCIPAL header, the JSON data must be Base64-encoded. You can use any online or offline tool to do that. A recommeded tool is the [DevToys](https://github.com/veler/DevToys) tool. A sample Base64 encoded payload that contains the JSON used before as example is the following:

```text
eyAgDQogICJpZGVudGl0eVByb3ZpZGVyIjogInRlc3QiLA0KICAidXNlcklkIjogIjEyMzQ1IiwNCiAgInVzZXJEZXRhaWxzIjogImpvaG5AY29udG9zby5jb20iLA0KICAidXNlclJvbGVzIjogWyJhdXRob3IiLCAiZWRpdG9yIl0sDQogICJjbGFpbXMiOiBbew0KICAgICJ0eXAiOiAiRmlyc3ROYW1lIiwNCiAgICAidmFsIjogIkpvaG4iDQogIH0sDQogIHsNCiAgICAidHlwIjogIkxhc3ROYW1lIiwNCiAgICAidmFsIjogIkRvZSINCiAgfV0NCn0=
```

a sample cURL request to simulate an authenticated request to retrieve the list of available element in the `book` entity, using the `author` role is the following:

```bash
curl --request GET \
  --url http://localhost:5000/api/books \
  --header 'X-MS-API-ROLE: author' \
  --header 'X-MS-CLIENT-PRINCIPAL: eyAgDQogICJpZGVudGl0eVByb3ZpZGVyIjogInRlc3QiLA0KICAidXNlcklkIjogIjEyMzQ1IiwNCiAgInVzZXJEZXRhaWxzIjogImpvaG5AY29udG9zby5jb20iLA0KICAidXNlclJvbGVzIjogWyJyb2xlMSIsICJyb2xlMiIsICJhdXRob3IiXSwNCiAgImNsYWltcyI6IFt7DQogICAgInR5cCI6ICJGaXJzdE5hbWUiLA0KICAgICJ2YWwiOiAiSm9obiINCiAgfSwNCiAgew0KICAgICJ0eXAiOiAiTGFzdE5hbWUiLA0KICAgICJ2YWwiOiAiRG9lIg0KICB9XQ0KfQ=='
```
