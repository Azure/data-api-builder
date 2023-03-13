# Local development with Data API builder for Azure Databases

Data API builder can be used completely on-premises, if needed. This can be helpful both in case you prefer to build cloud-solution offline, and then deploy everything in the cloud using a CI/CD pipeline, or in case you want to use Data API builder to give access to on-premises developers to on-premises databases available in your environment.

Depending on what you want to do, you have several options to run Data API builder locally:

- [Run Data API builder using the CLI tool](./running-using-dab-cli.md)
- [Run Data API builder in a container](./running-using-a-container.md)
- [Run Data API builder from source code](./running-from-source-code.md)

Data API builder works in the same way if run in Azure or if run locally or in a self-hosted environment. The only difference is related to how authentication can be done or simulated locally, so that even when using a local development experience you can test what happens when an authenticated request be received by Data API builder. Read more how you can simulate authenticated request locally here: [Local Authentication](./local-authentication.md).

## Static Web Apps CLI integration

[Static Web Apps CLI](https://azure.github.io/static-web-apps-cli/) has been integrated to support Data API builder so that you can have a full end-to-end full-stack development experience completely offline, and then deploy everything in the cloud using a CI/CD pipeline.

To learn more about how to use Data API builder with Static Web Apps CLI, read the following documentation: [Quickstart: Use Data API builder with Azure Databases](https://learn.microsoft.com/en-us/azure/data-api-builder/getting-started/getting-started-with-data-api-builder)
