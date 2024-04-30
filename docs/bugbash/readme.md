## Set Up

Install CosmosDb Emulator from here
How to test Mutation/Queries?
Click on this Link (SWA App): GraphiQL create-react-app Example (5.azurestaticapps.net)

How to test DB Policy feature? 
For this, you need to set it up locally.

Data Schema would look like this.
Permission and configuration would look like this:

## Basic understanding of the Product:
	
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

