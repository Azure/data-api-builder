# Troubleshoot Data API builder usage

This article provides solutions to common problems that might arise when you're using Data API builder (preview).

## REST endpoints

### HTTP 400 “Bad Request” Errors

An HTTP 400 error is returned when the URL path points to an invalid route. Routes indicate if the request is set using REST or GraphQL protocols.

Make sure that the URL being used by the request points to a route that has been configured in the configuration file. Route for each protocol is defined in the `runtime` section.  For example, the following configuration:

```json
"runtime": {
    "rest": {
      "enabled": true,
      "path": "/api"
    },
    "graphql": {
      "allow-introspection": true,
      "enabled": true,
      "path": "/graphql"
    },
    [...]
}
```

Requires that URL path uses the format:

```shell
/api/<entity>
```

or

```shell
/graphql
```

To send a REST or a GraphQL request.

### HTTP 404 “Not Found” Errors

An HTTP 404 error is returned if the requested URL points to a route not associated with any entity. By default, the name of the entity is also the route name.  For example, if you've configured the sample `Todo` entity in the configuration file like in the following sample:

```json
"Todo": {
      "source": "dbo.todos",
      "permissions": [
        {
          "role": "anonymous",
          "actions": [
            "*"
          ]
        }
      ]
    }
```

The entity `Todo` is reachable via the following route:

```shell
/<rest-route>/Todo
```

If you've specified the `rest.path` option in the entity configuration, for example like:

```json
"Todo": {
    "source": "dbo.todos",
    "rest": {
      "path": "todo"
    },
    "permissions": [
      {
        "role": "anonymous",
        "actions": [
          "*"
        ]
      }
    ]
  }
```

Then the URL route to use the `Todo` entity is:

```shell
/<rest-route>/todo
```

## GraphQL endpoints

## HTTP 400 “Bad Request” Error

A request sent to the GraphQL endpoint returns HTTP 400 "Bad Request" error every time the GraphQL request isn't done properly. It could be that a non-existing entity field is specified, or that the entity name is misspelled. Data API builder returns a descriptive error in the response payload with details about the error itself.

If the return GraphQL error is *"Either the parameter query or the parameter ID has to be set."*, make sure the GraphQL request is sent using the HTTP POST method.

## HTTP 404 “Not Found” Error

Make sure the GraphQL request is sent using the HTTP POST method.

## General errors

## Request returns an HTTP 500 error

HTTP 500 errors indicate that Data API builder can't properly operate on the backend database. Make sure that

- Data API builder can still connect to the configured database
- The database objects used by Data API builder are still available and accessible

To avoid potential security risk, when configured to run in `production` mode, which is the default setting, Data API builder doesn't return detailed errors in the response payload, to avoid disclosing potential sensitive information.

To have the underlying error raised by the database also returned in the response payload, set the `runtime.host.mode`  configuration option to `development`.

```json
"runtime": {
    [...]
    "host": {
        "mode": "development",
        [...]
    }
}
```

In either configuration modes, detailed errors are sent to the console output to help with troubleshooting.

## GraphQL doesn't provide introspection ability

Make sure the GraphQL clients you are using support GraphQL introspections. Well known clients like Insomnia and Postman all support introspection. Make sure the configuration file has the option `allow-introspection` set to `true` in the `runtime.graphql` configuration section. For example:

```json
"runtime": {
    [...]
    "graphql": {
      "allow-introspection": true,
      "enabled": true,
      "path": "/graphql"
    },
    [...]
}
```

## Request aren't authorized

### HTTP 401 “Unauthorized” Errors

When using Azure Active Directory authentication, you get an HTTP 401 error if the provided bearer token isn't valid or can't be authenticated.
Make sure that you've generated a bearer token using the audience defined in the configuration file, in the `host.authentication` section. For example, in this sample configuration:

```json
"authentication": {
    "provider": "AzureAD",
    "jwt": {
        "issuer": "https://login.microsoftonline.com/24c24d79-9790-4a32-bbb4-a5a5c3ffedd5/v2.0/",
        "audience": "b455fa3c-15fa-4864-8bcd-88fd83d686f3"
    }
}
[...]
```

You must generate a token valid for the defined audience. Using AZ CLI, for example, you can do it by specifying the audience in the `resource` parameter:

```shell
az account get-access-token --resource "b455fa3c-15fa-4864-8bcd-88fd83d686f3"
```

Check out the Azure AD Authentication documentation(./authentication-azure-ad.md) file to get more details on Azure AD authentication with Data API builder

### HTTP 403 “Forbidden” Errors

If you're sending an authenticated request, either using Static Web Apps integration or Azure AD, you may receive the error HTTP 403 “Forbidden”. This error may indicate that you're trying to use a role that hasn't been configured in the configuration file.

If the request is sent without a `X-MS-API-ROLE` header, the request, once authenticated, is executed in the context of the system role `authenticated`. If such a role hasn't been defined in the configuration file, for the entity you're trying to access to, an HTTP 403 error is returned.

If instead the request is providing a `X-MS-API-ROLE` header, then make sure the role is spelled correctly and is equal to one of the roles defined for the entity you're trying to access to.

For example, if you've a configuration file as shown in the following example:

```json
"Todo": {
    [..]
    "role": "role1",
    "actions": [
        {
            "action": "*",
            "policy": {
            "database": "@item.visibility eq @claims.userId"
            }
        }
    ]
}
```

The `X-MS-API-ROLE` must be set to `role1` to be able to access the Todo entity using the `role1` role.

**ATTENTION**: Roles name matching is case-sensitive
