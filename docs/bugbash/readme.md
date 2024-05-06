## Basic understanding of the Product:
- Basic Understanding of Cosmos DB No SQL: https://learn.microsoft.com/en-us/azure/cosmos-db/introduction
- Basic Understanding of GraphQL: https://graphql.org/learn/
- Basic Understanding of DAB: https://learn.microsoft.com/en-us/azure/data-api-builder/
 
## Set Up

### Cosmos DB Account
1. Install Cosmos Db Emulator from here: https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-develop-emulator?tabs=windows%2Ccsharp&pivots=api-nosql#install-the-emulator \
Alternatively, You can create an account on portal https://learn.microsoft.com/en-us/azure/cosmos-db/try-free?tabs=nosql
2. Create a database (as `graphqldb`) and container(as `planet`)
3. Upload this data to emulator or real account: https://github.com/Azure/data-api-builder/blob/users/sourabhjain/bugbash/docs/bugbash/mydata.json

**Upload Data in Emulator:** 
![image](https://github.com/Azure/data-api-builder/assets/6362382/e1b65905-d6e4-4993-8eb2-617214f12668)

**Upload Data in Portal:**
![image](https://github.com/Azure/data-api-builder/assets/6362382/0edb0b0f-6fe8-42b5-baf5-daa063fb382c)

### Data API Builder(DAB) [Release 0.13.0-rc]

**Download below files:**
- Schema file: https://github.com/Azure/data-api-builder/blob/users/sourabhjain/bugbash/docs/bugbash/schema.gql
- Runtime Config JSON: https://github.com/Azure/data-api-builder/blob/main/src/Service.Tests/dab-config.CosmosDb_NoSql.json

**Run DAB** \
\
Follow this for quick start: https://learn.microsoft.com/en-us/azure/data-api-builder/quickstart-nosql#install-the-data-api-builder-cli \
OR Alternatively, run following command: \
1. Install latest DAB package: `dotnet tool install --global Microsoft.DataApiBuilder --version 0.13.0-rc`
2. OR, Update to the latest package: `dotnet tool update --global Microsoft.DataApiBuilder --version 0.13.0-rc`
3. Confirm if it is installed `dotnet tool list --global`
   ![image](https://github.com/Azure/data-api-builder/assets/6362382/63f77ab1-db94-4d4c-abb9-2df164b256e2)
4. Go to the same location where you have above 2 files downloaded and run `dab start`
5. Open https://localhost:5000/graphql

## Scenarios:
**Making connection to Cosmos DB**
- [ ] You should be able to use DAB using Connection String.
- [ ] You should be able to use DAB using Managed Identity (ref, how to set up RBAC for Cosmos DB https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-setup-rbac) \
      _**Note:** Applicable only when you are using DAB from Azure resources i.e SWA, in that case follow this setup https://learn.microsoft.com/en-us/azure/data-api-builder/quickstart-azure-cosmos-db-nosql#configure-the-database-connection_
      
**Mutation Operation**
- [ ] Should be able to Create a simple/complex Item

      mutation {
        createPlanet (item: {id: "id", name: "name", stars: [{ id: "starId" }] }) {
         id
         name
        }
      }
      
- [ ] Should be able to Delete an item
      
       mutation {
        deletePlanet (id: "id", _partitionKeyValue: "id") {
            id
            name
        }
      }

      
- [ ] Should be able to Update an item (*Expectation is, it will replace the existing item, with the new item*)

       mutation {
         updatePlanet (id: "id", _partitionKeyValue: "id", item: { id: "id", name: "newName", stars: [{ id: "starId" }] }) {
          id
          name
      }
      
- [ ] Should be able to Patch an item (*Expectation is, it will update only passed information*)

       mutation {
           patchPlanet (id: "id", _partitionKeyValue: "id", item: {{name: "newName", stars: [{{ id: "starId" }}] }}) {{
               id
               name
               stars
               {
                   id
               }
           }
       }

**Query Operation**
- [ ] Should able to read item with different filters (*With different operators like `eq`, `neq`, `lt`, `gt`, `lte`, `gte`, `contains`, `notcontains`, `startwith`, `endswith`*)

       planets(first: 10, filter: {character: {star: {name: {eq: "Earth_star"}}}})
           {
               items {
                   name
               }
           }
       }
    
- [ ] Should able to read item in particular order (*`Order By`*)

      planets (first: 10, after: null, orderBy: {id: ASC, name: null }) {
          items {
              id
              name
          },
          endCursor,
          hasNextPage
      }

- [ ] Should able to read item with filters have *`AND`* and *`OR`* condition

      planets(first: 10, filter: {
                   and: [
                       { additionalAttributes: {name: {eq: ""volcano1""}}}
                       { moons: {name: {eq: ""1 moon""}}}
                       { moons: {details: {contains: ""11""}}}
                   ]   
                })
           {
               items {
                   name
               }
           }
       }
      
- [ ] Should able to read items from multiple pages.

**Authorization**
- [ ]  Should honor the given permissions assign to a role in Runtime Config file (*i.e. dab-config.CosmosDb_NoSql.json*)
- [ ]  Should be able to make `Patch` and `Update` operation if `update` permission is there.
- [ ]  Should be able to apply field level authorization using include/exclude fields
- [ ]  Should be able to apply item level authorization by defining database policy. (Operation supported in the policy condition are `eq`, `neql`, `>` , `>=`, `<`, `<=`)

To checkout more scenarios, you can refer the release notes: https://github.com/Azure/data-api-builder/releases

## Where to report the bug?
Tou can put your findings  at https://microsoftapc-my.sharepoint.com/:x:/g/personal/sourabhjain_microsoft_com/ESkNLFvEfSNHp6s3RUkLV7MBEqRBmb1YK2j4sJsTdiy-wQ?e=2ltHN4
