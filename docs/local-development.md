# Local development with Data API builder for Azure Databases

Data API builder can be used completely on-prem, if needed. This can be helpful both in case you prefer to build cloud-solution offline, and then deploy everything in the cloud using a CI/CD pipeline, or in case you want to use Data API builder to give access to on-prem developers to on-prem databases available in your environment.

Depending on what you want to do, you have several options to run Data API builder locally:

- [Running from source code](./running-from-source-code.md)
- [Running using a container](./running-using-a-container.md)
- [Running using the CLI tool](./running-using-dab-cli.md)

Data API builder works in the same way if run in Azure or if run locally or in a self-hosted environment. The only difference is related to how authentication can be done or simulated locally, so that even when using a local development experience you can test what happens when an authenticated request be received by Data API builder. Read more how you can simulate authenticated request locally here: [Local Authentication](./local-authentication.md).
