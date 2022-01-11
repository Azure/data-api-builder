# Summary
Customers will provide information for how to filter their results by using the keyword $filter in their
request following the Microsoft REST Api Guidelines

We want to parse the $filter querystring and use the results as a part of query generation.

Example: return all Products whose Price is less than $10.00

GET https://server-address:port/entity?$filter=price lt 10.00


We have several options for how we can address the parsing of the $filter querystring and this document will
attempt to present the different options and their pros and cons.


# OData Parser

OData provides a parser which can handle the parsing of a $filter querystring. The parser requires an Entity Data Model which describes
the customers data model, including entities, column names, and types. The parser then returns an AST that can be traversed to extract
the predicates we need for our query generation.

This is the approach that was taken by the Azure Cognitive Search team, and the entry point to the code for their parser can be found here: https://msdata.visualstudio.com/Azure%20Search/_git/AzureSearch?path=/Source/Search/Product/SearchCore/Services/ODataQuery/FilterParser/FilterParser.cs

## Versions
OData v4.0 is the most recent version and supports JSON as the recommended format. 

## Entity Data Model from JSON
An EDM model can be created from JSON. This can be done programmatically by reading a JSON with the relevant data, and then
using that data to build an EDM model with some sort of dynamic edm model builder. This is an approach similar to what Azure Cognitive Search
has done. Their OData dynamic model builder can be found here: https://msdata.visualstudio.com/Azure%20Search/_git/AzureSearch?path=%2FSource%2FCommon%2FProduct%2FWebExtensions%2FOData%2FModelBuilder%2FDynamicODataModelBuilder.cs&_a=contents&version=GBcurrent

And their Index model builder can be found here: https://msdata.visualstudio.com/Azure%20Search/_git/AzureSearch?path=/Source/Common/Product/ClusterCore/Services/OData/IndexEdmModelBuilder.cs&version=GBcurrent&_a=contents


## Entity Data Model from XML
An EDM model can be generated automatically from properly formated edm xml files. In thise case, the relevant meta data must be populated in the xml in the correct format,
and then an xxml reader can be created from reading this file. That reader can be used to generate an EDM model. This would require these xml files to either be pre-generated, or
generated from JSON.

## Entity Data Model from Classes
Similar to how we can create a model from reading in the relevant data from JSON and passing the information off to a builder, OData supports the creation of EDM models
by passing the relevant data that is stored in class files. This would require the classes to already exist with at the very least the correct fields to match the customers
data model.

## Future Work
Odata has other libraries beyond $filter, such as $orderby, which could prove useful for future work. Much of the work done to get OData to support parsing for $filter should
help to make understanding how to get it to work for something like $orderby easier. Other options for parsing include, $select, $expand, $search, $top, $skip, $count
https://docs.microsoft.com/en-us/odata/odatalib/use-uri-parser

## Potential Drawbacks
If we use the OData Uri Parser then this introduces a dependancy into our service. It is possible that our spec and what the OData Uri Parser support or expect could diverge at some
point, in which case we would need to redo whatever work we were relying on this library for. It also means any limitations that are introduced into OData will either need to be 
patched individually by us, or not supported until they are supported in OData. We are also forced to use the patterns that OData supports in building our service, we may effect our
flexibility when making changes to our code.

#### Known Issues
There are a few known issues associated with OData:

Incorrect case usage causing unexpected exceptions (ie: $Filter), which has a work-around using case-insensitivity (ie: treat $Filter as $filter). 

Embedded $count within $filter will throw not implemented exception. 

IN operator parsing breaks if the collection has a trailing comma (ie: $filter=PropertyString in ('a','b',)).

https://github.com/OData/WebApi/issues/425

https://github.com/OData/WebApi/issues/194

https://github.com/OData/odata.net/issues/1378

# Custom Parser
Another option for parsing the $filter query string would be to write a custom parser. This would be flexible and allow us to handle parsing however we chose. It would however require
everything to be created by our team whch means more work, and a need to think through all of the potential pit falls of parsing a $filter string of a form that complies with the
Microsoft REST API Guidelines. It also means that we would need to create other parsers, or expand this parsing to cover other keywords such as $orderby if we wish to expand support.

## Data Model
Much like the OData parser, in order to correctly generate the queries we need to from the parsed $filter string, we will need to have relevant meta data about the customers data model.
This will require us to generate our own model similar to how OData would work, although we would have the flexibility to handle this however we desire.

## Future Work
Future parsing work may be similar to the parsing that is done with $filter, but this would require more work to expand the parser.