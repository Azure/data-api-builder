## Basic understanding of the Product:
Basic Understanding of Cosmos DB No SQL: https://learn.microsoft.com/en-us/azure/cosmos-db/introduction
Basic Understanding of GraphQL: https://graphql.org/learn/
Basic Understanding of DAB: https://learn.microsoft.com/en-us/azure/data-api-builder/
 
## Set Up
1. Install Cosmos Db Emulator from here: https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-develop-emulator?tabs=windows%2Ccsharp&pivots=api-nosql#install-the-emulator \
Alternatively, You can create an account on portal https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/how-to-create-account?tabs=azure-portal
2. Upload this data to emulator or real account:
3. Use this runtime config JSON to start: https://github.com/Azure/data-api-builder/blob/main/src/Service.Tests/dab-config.CosmosDb_NoSql.json

## Scenarios:
**Making connection to Cosmos DB**
- [ ] You should be able to use DAB using Connection String.
- [ ] You should be able to use DAB using Managed Identity.

**Mutation Operation**
- [ ] Should be able to Create a simple/complex Item
- [ ] Should be able to Delete an item
- [ ] Should be able to Update an item (*Expectation is, it will replace the existing item, with the new item*)
- [ ] Should be able to Patch an item (*Expectation is, it will update only passed information*)

**Query Operation**
- [ ] Should able to read item with different filters (*With different operators like `eq`, `neq`, `lt`, `gt`, `lte`, `gte`, `contains`, `notcontains`, `startwith`, `endswith`*)
- [ ] Should able to read item in particular order (*`Order By`*)
- [ ] Should able to read item with filters have *`AND`* and *`OR`* condition

**Authorization**
- [ ]  Should honor the given permissions assign to a role in Runtime Config file (*i.e. dab-config.CosmosDb_NoSql.json*)
- [ ]  Should be able to make `Patch` and `Update` operation if `update` permission is there.
- [ ]  Should be able to apply field level authorization using include/exclude fields
- [ ]  Should be able to apply item level authorization by defining database policy. (Operation supported in the policy condition are `eq`, `neql`, `>` , `>=`, `<`, `<=`)

To checkout more scenarios, you can refer the release notes: https://github.com/Azure/data-api-builder/releases
