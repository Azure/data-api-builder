# Setting up Azure AD

## Create Azure AD tenant

 1. Create an Azure AD tenant in your Azure Subscription by following [this guide](https://docs.microsoft.com/en-us/azure/active-directory/fundamentals/active-directory-access-create-new-tenant).

## Create Client App Registration

This step creates the app registration for the application that sends requests to the DataAPIBuilder.
Examples include a frontend webpage, or PostMan (this guide is for PostMan).

1. Navigate to your Azure AD tenant in the Azure Portal
1. Select: **App Registration**
1. Select: **New Registration**
1. *Name*: `PostmanDataApiBuilderClient`
1. *Supported Account Types*: Leave default selection "Accounts in this organizational directory only."
1. *Redirect URI*: Leave default (empty)
1. Select: Register

### Configure Client App Registration

1. Navigate to your App Registration (`PostmanDataApiBuilderClient`) overview page.
2. Save the client app id value for use later.
3. Under *Authentication*, find the **Web** dropdown for configuring Redirect URIs.
   1. Redirect URI: `https://oauth.pstmn.io/v1/callback`
   2. Select **Save**
4. Under *Certificates & secrets* and *Client Secrets*, select **New client secret**
   1. Add a description and expiration setting.
   2. Select **Add**

## Create Server App Registration

1. Navigate to your Azure AD tenant in the Azure Portal
1. Select: **App Registration**
1. Select: **New Registration**
1. *Name*: `DataAPIBuilder`
1. *Supported Account Types*: Leave default selection "Accounts in this organizational directory only."
1. *Redirect URI*: Leave default (empty)
1. Select: Register

### Configure Server App Registration

1. Navigate to `Expose an API` from your App Registration (`DataAPIBuilder`) overview page.
2. Under *Scopes defined by this API*, select **Add a scope**
   1. Scope name: `EndpointAccess`
   2. Who can consent?: `Admins and users`
   3. Admin consent display name: `Execute requests against DataAPIBuilder`
   4. Admin consent description: `Allows client app to send requests to DataAPIBuilder endpoint.`
   5. User consent display name: `Execute requests against DataAPIBuilder`
   6. user consent description: `Allows client app to send requests to DataAPIBuilder endpoint.`
   7. State: `Enabled`
   8. Select **Add scope**
3. Navigate to `App roles` from your App Registration overview page.
   1. Select **Create app role**
      1. DisplayName: `contributor`
      2. Allowed member types: **Users/Groups**
      3. Value: `contributor` (Note: this is the value that shows up in role claims in your access token).
      4. Description: `contributors can provide content.`
      5. Do you want to enable this app role?: Ensure checkbox is checked.
      6. Select **Apply**
4. Navigate to `Expose an API` from your App Registration overview page.
   1. Under *Authorized client applications*, select **Add a client application**
      1. ClientID: `Value Saved earlier, client ID of your client app registration`
      2. Authorized Scopes: `api://<APP_ID_DataAPIBuilder>/EndpointAccess`
      3. Select **Add application**

## Addition Client App Registration Configuration

1. Navigate to your App Registration (`PostmanDataApiBuilderClient`) overview page.
1. Under *API permissions*, select **Add a permission**
   1. Under *Select an API*, select **My APIs**
   2. Select `DataAPIBuilder`
   3. Select **Delegated permissions**
   4. Select **Add permissions**

## Postman Configuration

1. Create a new Postman collection, you will configuration authorization for the collection 
   so it can be used for all requests (REST and GraphQL).
2. Select the collection, then navigate to **Authorization**
   1. Type: **OAuth 2.0**
   2. Add auth data to: **Request Headers**
   3. Header Prefix: `Bearer`
   4. Under *Configure New Token*, and under *Configuration Options*
      1. Token Name: `Azure AD Token`
      2. Grant Type: **Authorization Code**
      3. Callback URL: `https://oauth.pstmn.io/v1/callback` (remember this was set on redirect URIs for your client app registration)
      4. Select: **Authorize using browser**
      5. Auth URL:
      6. Access Token URL:
      7. Client ID: `Client_APP_Registration_ID` (Recommended: store as Postman variable, and use value `{{ClientID_VariableName}}` here)
      8. Client Secret: `Client_APP_Secret` this was created earlier. (Recommended: store as Postman variable, and use value `{{ClientSecret_VariableName}}` here)
      9. Scope: `api://<APP_ID_DataAPIBuilder>/EndpointAccess` (Note: don't forget this or authentication will fail.)
3. Select Get New Access Token, and sign in with your Azure AD tenant credentials.
   1. It is expected that you will get a Consent screen. This is normal and you must agree to authenticate successfully.

## Assign roles to your account

1. From your Azure AD tenant overview screen, navigate to **Enterprise Applications**
2. Select the entry for `DataAPIBuilder` (This will be created automatically when you register your DataAPIBuilder App Registration)
3. Navigate to **Users and groups**
4. Select **Add user/group** to add a role assignment
   1. Users: select your user account and select **Select** to save your choice.
   2. Select a role: choose a role you want to assign to your account. (Note: if you don't see your role, wait a few minutes for Azure AD replication to finish from when you added the role to your App Registration earlier.)
   3. Repeat this step for all the roles you want to add to your account.
5. To be assigned these new roles, you must acquire a new access token in PostMan.

## DataAPIBuilder Runtime Configuration

1. Using the Application Registration ID from your `DataAPIBuilder` App registration, add this value to your runtime configuration.

```json
      "authentication": {
        "provider": "AzureAD",
        "jwt": {
          "audience": "<ID_DataAPIBuilder>",
          "issuer": "https://login.microsoftonline.com/<AZURE_AD_ TENANT_ID>/v2.0"
        }
```
