# Project Hawaii

## Introduction

Project Hawaii provides a consistent, productive abstraction for building GraphQL and REST API applications with data. Powered by Azure Databases, Project Hawaii allows developers to declaritive define what objects from the database will be made available as REST and/or GraphQL endpoints, therefore alleviating the developer from the burden and the responsability of writing all the needed plumbing code. This will give back more time to developers so that they can focuse more on the added-value code, and will also provide developer experiences that meet developers where they are.

## Setup

### 1. Clone Repository

Clone the repository with your preferred method or locally navigate to where you'd like the repository to be and clone with the following command, make sure you replace `<directory name>`

```bash
git clone https://github.com/Azure/hawaii-gql.git <directory name>
```

### 2. Configure Database Engine

You will need to provide a database to be used with Project Hawaii. DataGateway supports SQL Server, CosmosDB, PostgreSQL, and MySQL.

### 3. Create a Configuration File

Project Hawaii need a minimal configuration in order to get started. Create a new Project Hawaii configuration file by using the provided template `hawaii-config.template.json`. Make a copy of the template and rename it to `hawaii-config.json` and open the file in VS Code or your favourite IDE.

An alternative to manual configuration of the configuration file is to use [Hawaii CLI](https://github.com/Azure/hawaii-cli), that will help you to quickly define an maintain the configuation file.




