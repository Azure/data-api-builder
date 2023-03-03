# Authentication with Azure AD

For authentication with Azure AD to work properly, Azure AD needs to be configured so that it can authenticate a request sent to Data API builder (the "Server App").

The first step is to configure Azure AD to perform authentication for Data API builder

1. [Create Azure AD tenant (Optional)](#create-azure-ad-tenant-optional)
1. [Create Server App Registration](#create-server-app-registration)
1. [Configure Server App Registration](#configure-server-app-registration)
1. [Assign roles to your account](#assign-roles-to-your-account)

Azure AD can now perform authentication on behalf of Data API builder. In order to send authenticated request, a client application (the "Client App") must be registered too, so that Azure AD can properly identify and authorize it.

One way to get a JWT token that can be used to send authenticate request to Data API builder is to use AZ CLI:

1. [Configure AZ CLI to get an authentication token](#configure-az-cli-to-get-an-authentication-token)

Now that you have a way to get a valid JWT token, you can use it to call a Data API builder endpoint:

1. [Update Data API builder configuration file](#update-data-api-builder-configuration-file)
1. [Send authenticated request to Data API builder](#send-authenticated-request-to-data-api-builder)

If you prefer to use a third party application like PostMan to send authenticated request to Data API builder, follow these steps:

1. [Create Postman Client App Registration](#create-postman-client-app-registration)
1. [Postman Configuration](#postman-configuration)

## Create Azure AD tenant (Optional)

Make sure you have an Azure Tenant that you can use. If you want or need to create a new Tenant, use the following instructions

 1. Create an Azure AD tenant in your Azure Subscription by following [this guide](https://learn.microsoft.com/azure/active-directory/fundamentals/active-directory-access-create-new-tenant).
 2. Make note of your Tenant ID which can be found on the Overview page of your newly created tenant resource in the Azure Portal.

## Create Server App Registration

1. Switch to the Azure AD tenant that you want to use
1. Open the "Azure Active Directory" resource blade
1. Select: **App Registration**
1. Select: **New Registration**
1. *Name*: `Data API builder`
1. *Supported Account Types*: Leave default selection "Accounts in this organizational directory only."
1. *Redirect URI*: Leave default (empty)
1. Select: Register

## Configure Server App Registration

Note: The following steps can also be found in the Microsoft Doc: [QuickStart: Configure an application to expose a web API](https://learn.microsoft.com/azure/active-directory/develop/quickstart-configure-app-expose-web-apis).

1. Navigate to `Expose an API` from your App Registration (`Data API builder`) overview page.
1. Create an `Application ID URI`, by clicking on **Set**, just before the section *Scopes defined by this API*,
1. Under *Scopes defined by this API*, select **Add a scope**. For more details on why scopes are defined, see this [Microsoft Identity Platform doc](https://learn.microsoft.com/azure/active-directory/develop/v2-permissions-and-consent#request-the-permissions-from-a-directory-admin).
   1. Scope name: `Endpoint.Access`
   1. Who can consent?: `Admins and users`
   1. Admin consent display name: `Execute requests against Data API builder`
   1. Admin consent description: `Allows client app to send requests to Data API builder endpoint.`
   1. User consent display name: `Execute requests against Data API builder`
   1. User consent description: `Allows client app to send requests to DataAPIbuilder endpoint.`
   1. State: `Enabled`
   1. Click on **Add scope**
1. Navigate to `App roles` from your App Registration overview page.
   1. Click on **Create app role**
      1. DisplayName: `SampleRole`
      2. Allowed member types: **Users/Groups**
      3. Value: `Sample.Role` (Note: this is the value that shows up in role claims in your access token).
      4. Description: `A Sample Role to be used with Data API builder.`
      5. Do you want to enable this app role?: Ensure checkbox is checked.
      6. Select **Apply**
1. Navigate to `Manifest` from your App Registration overview page.
    1. Update the JSON document so that `accessTokenAcceptedVersion` is set to `2`

## Assign roles to your account

1. Open the Azure Active Directory resource blade and then select **Enterprise Applications**
2. Search for `Data API builder` (An Enterprise App has been created automatically when you did the "Data API builder App" Registration)
3. Navigate to **Users and groups**
4. Select **Add user/group** to add a role assignment
   1. Users: select your user account and select **Select** to save your choice.
   2. Select a role: choose a role you want to assign to your account. (Note: if you don't see your role, wait a few minutes for Azure AD replication to finish from when you added the role to your App Registration earlier.)
   3. Repeat this step for all the roles you want to add to your account.

Where the`APP_ID` is the "Application (client) ID" and the `TENANT_ID` is the "Directory (tenant) ID". Both values can be found in the App Registration overview page in the Azure Active Directory blade.

## Configure AZ CLI to get an authentication token

Make sure you are logged in to AZ CLI with the account that you want to use to authenticate against Data API builder.

```sh
az login
```

and select the subscription where you have [configured the Data API builder App Registration](#configure-server-app-registration).

```sh
az account set --subscription <subscription name or id>
```

then run the following command to try to authenticate against the newly created scope:

```sh
az account get-access-token --scope api://<Application ID>/Endpoint.Access
```

It should return an error like the following:

```text
AADSTS65001: The user or administrator has not consented to use the application with ID '<AZ CLI Application ID GUID>' named 'Microsoft Azure CLI'. Send an interactive authorization request for this user and resource.
```

The \<AZ CLI Application ID GUID\>, which represents AZ CLI, must be allowed to authenticate against the Data API builder Azure AD Application. To do that, search for the "Data API builder" application in the Azure Portal or go to the Azure Active Directory portal blade and select **App Registrations**. Select the "Data API builder" application and then:

1. Navigate to **Expose an API** from your App Registration overview page.
   1. Under *Authorized client applications*, select **Add a client application**
      1. ClientID: Use the \<AZ CLI Application ID GUID\>
      2. Authorized Scopes: `api://<Application ID>/Endpoint.Access`
      3. Select **Add application**

If you now run the AZ CLI command again, you'll get an access token:

```json
{
  "accessToken": "***",
  "expiresOn": "2022-11-03 14:55:37.000000",
  "subscription": "***",
  "tenant": "***",
  "tokenType": "Bearer"
}
```

you can inspect the token using a online debugging tool like [jwt.ms](http://jwt.ms). The decoded token will contain something like:

```json
{
   "roles": [
       "Sample.Role"
   ],
   "scp": "Endpoint.Access"
}
```

that shows you can access the scope "Endpoint.Access" and that you have been assigned the "Sample.Role".

You can now use the generated Bearer Token to send authenticated request against your Data API builder.

## Update Data API builder configuration file

Update the `dab-config.json` file so that in the `authentication` section you'll have something like:

```json
"authentication": {
  "provider": "AzureAD",
  "jwt": {
    "audience": "<APP_ID>",
    "issuer": "https://login.microsoftonline.com/<TENANT_ID>/v2.0"
  }
}
```

To test out that authentication is working fine, you can update your configuration file so that an entity will allow access only to authenticated request with the `Sample.Role` assigned. For example:

```json
"entities": {
  "Book": {
    "source": "dbo.books",
    "permissions": [
      {
        "role": "Sample.Role",
        "actions": [ "read" ]
      }
    ]
  }
}
```

## Send authenticated request to Data API builder

You can use any HTTP client now to send an authenticated request. Firstly get the token:

```sh
az account get-access-token --scope api://<Application ID>/Endpoint.Access --query "accessToken" -o tsv
```

then you can use the obtained access token to issue a successful authenticated request to a protected endpoint:

```sh
curl -k -r GET -H 'Authorization: Bearer ey...xA' -H 'X-MS-API-ROLE: Sample.Role' https://localhost:5001/api/Book
```

## Create Postman Client App Registration

This step creates the app registration for the application that sends requests to the Data API builder.

1. Navigate to your Azure AD tenant in the Azure Portal
1. Select: **App Registration**
1. Select: **New Registration**
1. *Name*: `Postman`
1. *Supported Account Types*: Leave default selection "Accounts in this organizational directory only."
1. *Redirect URI*: Leave default (empty)
1. Select: Register

### Configure Client App Registration

1. Navigate to your App Registration (`Postman`) overview page.
2. Save the client app id value for use later.
3. Under *Authentication*, click on **Add a platform**, and choose **Web**  for configuring Redirect URIs.
   1. Redirect URI: `https://oauth.pstmn.io/v1/callback`
   2. Select **Configure**
4. Under *Certificates & secrets* and *Client Secrets*, select **New client secret**
   1. Add a description and expiration setting.
   2. Select **Add**
   3. Save the **Value** of the created secret somewhere as it will be needed later, and it will not be visible anymore once you navigate to another page

The following steps configure [delegated permissions](https://learn.microsoft.com/azure/active-directory/develop/v2-permissions-and-consent#permission-types) for the client app registration. This means that the client app will be delegated with the permission to act as a signed-in user when it makes calls to the target resource (Data API builder).

1. Navigate to your App Registration (`Postman`) overview page.
2. Under *API permissions*, select **Add a permission**
   1. Under *Select an API*, select **My APIs**
   2. Select `Data API builder`
   3. Select **Delegated permissions**
   4. Select `Endpoint.Access` API
   5. Select **Add permissions**

## Postman Configuration

1. Create a new Postman collection: you will configure authorization for the collection, so it can be used for all requests (REST and GraphQL).
2. Select the collection, then navigate to **Authorization**
   1. Type: **OAuth 2.0**
   2. Add auth data to: **Request Headers**
   3. Header Prefix: `Bearer`
   4. Under *Configure New Token*, and under *Configuration Options*
      1. Token Name: `Azure AD Token`
      2. Grant Type: **Authorization Code**
      3. Callback URL: `https://oauth.pstmn.io/v1/callback`
         1. Remember this was set on redirect URIs for your client app registration.
         2. For more information on this redirect URI, see PostMan's OAuth 2.0 Configuration [Documentation](https://learning.postman.com/docs/sending-requests/authorization/#requesting-an-oauth-20-token).
      4. Select: **Authorize using browser**
      5. Auth URL: `https://login.microsoftonline.com/<TENANT_ID>/oauth2/v2.0/authorize`
      6. Access Token URL: `https://login.microsoftonline.com/<TENANT_ID>/oauth2/v2.0/token`
      7. Client ID: `Client_APP_Registration_ID`
         1. Recommended: store as Postman variable, and use value `{{ClientID_VariableName}}` here.
         2. The client ID value can be found on the client app registration overview page.
      8. Client Secret: `Client_APP_Secret` this was created earlier. (Recommended: store as Postman variable, and use value `{{ClientSecret_VariableName}}` here)
      9. Scope: `api://<APP_ID>/Endpoint.Access` (Note: don't forget this or authentication will fail.)
3. Select Get New Access Token, and sign in with your Azure AD tenant credentials.
   1. It is expected that you will get a Consent screen. This is normal and you must agree to authenticate successfully.
