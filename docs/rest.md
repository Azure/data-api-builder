# REST in Data API builder

Entities configured to be available via REST will be available at the path 

```
http://<dab-server>/api/<entity>
```

Using the [Getting Started](./getting-started/getting-started.md) example, where there are the `books` and the `authors` entity configured for REST access, the path would, for example:

```
http://localhost:5000/api/book
```

Depending on the permission defined on the entity in the configuration file, the following HTTP verbs are available:

- [GET](#get): Get zero, one or more items
- [POST](#post): Create a new item
- [PUT](#put): Create or replace an item
- [PATCH](#patch): Update an item
- [DELETE](#delete): Delete an item

## Resultset format

The returned result is a JSON object with this format:

```json
{
    "value": []    
}
```

The items related to the requested entity will be available in the `value` array. For example:

```json
{
  "value": [
    {
      "id": 1000,
      "title": "Foundation"
    },
    {
      "id": 1001,
      "title": "Foundation and Empire"
    }
  ]
}
```

> **Attention!**: Only the first 100 items are returned by default.

## GET

Using the GET method you can retrieve one or more items of the desired entity

### URL parameters

REST endpoints support the ability to return an item via its primary key, using URL parameter:

```
http://<dab-server>/api/<entity>/<primary-key-column>/<primary-key-value>
```

for example:

```
http://localhost:5000/api/book/id/1001
```

### Query parameters

REST endpoints support the following query parameters to control the returned items:

- [`$select`](#select): returns only the selected columns
- [`$filter`](#filter): filters the returned items
- [`$orderby`](#orderby): defines how the returned data will be sorted
- [`$first` and `$after`](#first-and-after): returns only the top `n` items

Query parameters can be used togheter

#### `$select`

The query parameter `$select` allow to specify which fields must be returned. For example:

```
http://localhost:5000/api/author?$select=first_name,last_name
```

will only return `first_name` and `last_name` fields.

If any of the requested fields do not exist or it is not accessible due to configured permissions, a `400 - Bad Request` will be returned.

#### `$filter`

The value of the `$filter` option is predicate expression (an expression that returns a boolean value) using entity's fields. Only items where the expression evaluates to true are included in the response. For example:

```
http://localhost:5000/api/author?$filter=last_name eq 'Asimov'
```

will only return those author whose last name is `Asimov`

The operators supported by the `$filter` option are:

Operator                 | Description           | Example
--------------------     | --------------------- | -----------------------------------------------------
**Comparison Operators** |                       |
eq                       | Equal                 | title eq 'Hyperion'
ne                       | Not equal             | title ne 'Hyperion'
gt                       | Greater than          | year gt 1990
ge                       | Greater than or equal | year ge 1990
lt                       | Less than             | year lt 1990
le                       | Less than or equal    | year le 1990
**Logical Operators**    |                       |
and                      | Logical and           | year ge 1980 and year lt 1990
or                       | Logical or            | year le 1960 or title eq 'Hyperion'
not                      | Logical negation      | not (year le 1960)
**Grouping Operators**   |                       |
( )                      | Precedence grouping   | (year ge 1970 or title eq 'Foundation') and pages gt 400

#### `$orderby`

The value of the `orderby` parameter is a comma-separated list of expressions used to sort the items. 

Each expression in the `orderby` parameter value may include the suffix `desc` to ask for a descending order, separated from the expression by one or more spaces.

For example: 

```
http://localhost:5000/api/author?$orderby=first_name desc, last_name
```

will return the list of authors sorted by `first_name` descending and then by `last_name` ascending.

#### `$first` and `$after`

The query parameter `$first` allows to limit the number of items returned. For example:

```
http://localhost:5000/api/book?$first=5
```

will return only the first `n` books. If ordering is not specified items will be ordered by the underlying primary key. `n` must be a positive integer value.

If the number of items available to the entity is bigger than the number specified in the `$first` parameter, the returned result will contain a `nextLink` item:

```json
{
    "value": [],
    "nextLink": ""
}
```

`nextLink` can be used to get the next set of items via the `$after` query parameter using the following format:

```
http://<dab-server>/api/book?$first=<n>&$after=<continuation-data>
```

## POST

Create a new iteam for the specified entity. For example:

```
POST http://localhost:5000/api/book

{
  "id": 2000,
  "title": "Do Androids Dream of Electric Sheep?"
}
```

Will create a new book. All the fields that cannot be nullable must be supplied. If successful the full entity object, including any null fields, will be returned:

```JSON
{
  "value": [
    {
      "id": 2000,
      "title": "Do Androids Dream of Electric Sheep?",
      "year": null,
      "pages": null
    }
  ]
}
```

## PUT

With PUT you can create or replace an item of the specified entity. The query pattern is:

```
http://<dab-server>/api/<entity>/<primary-key-column>/<primary-key-value>
```

for example:

```
PUT /api/book/id/2001

{  
  "title": "Stranger in a Strange Land",
  "pages": 525
}
```

If there is an item with the specified primary key `2001` that item will be *completely replaced* by the provided data. If instead an item with that primary key does not exists, a new item will be created.

In either case the result will be something like:

```json
{
  "value": [
    {
      "id": 2001,
      "title": "Stranger in a Strange Land",
      "year": null,
      "pages": 525
    }
  ]
}
```

## PATCH

With PATH you can update the item of the specified entity. Only the specified fields will be affected. All fields not specified in the request body will not be affected

The query pattern is:

```
http://<dab-server>/api/<entity>/<primary-key-column>/<primary-key-value>
```

for example:

```
PATCH /api/book/id/2001

{    
  "year": 1991
}
```

The result will be something like:

```json
{
  "value": [
    {
      "id": 2001,
      "title": "Stranger in a Strange Land",
      "year": 1991,
      "pages": 525
    }
  ]
}
```

## PUT


With PATH you can update the item of the specified entity. Only the specified fields will be affected. All fields not specified in the request body will not be affected

The query pattern is:

```
http://<dab-server>/api/<entity>/<primary-key-column>/<primary-key-value>
```

for example:

```
DELETE /api/book/id/2001
```

The result will be a empty response with status code 204 