# REST in Data API builder

Entities configured to be available via REST will be available at the path

```
http://localhost:5000/api/<entity>
```

Using the [Getting Started](./getting-started/getting-started.md) example, where there are the `books` and the `authors` entity configured for REST access, the path would, for example:

```
http://localhost:5000/api/book
```

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

## Limit and paginate the returned result

The query parameter `$first` allows to limit the number of items returned. For example:

```
http://localhost:5000/api/book?$first=5
```

will return only the first 5 books. If ordering is not specified items will be ordered by the underlying primary key. 

If the number of items in the entity is bigger than the number specified in the `$first` parameter, the returned result will contain a `nextLink` item:

```json
{
    "value": [],
    "nextLink": ""
}
```

`nextLink` can be used to get the next set of items via the `$after` query parameter:

```
http://localhost:5000/api/book?$first=<n>&$after=<continuation-data>
```

## Specify what fields will be returned

The query parameter `$select` allow to specify which fields must be returned. For example:

```
http://localhost:5000/api/author?$select=first_name,last_name
```

will only return `first_name` and `last_name` fields:

```json
{
  "value": [
    {
      "first_name": "Isaac",
      "last_name": "Asimov"
    },
    {
      "first_name": "Robert",
      "last_name": "Heinlein"
    },
    {
      "first_name": "Robert",
      "last_name": "Silvenberg"
    }
  ]
}
```

If the requested field does not exist or it is not accessible due to configured permissions, a `400 - Bad Request` will be returned.


## Filtering results

With the query parameter `$filter` you can define what items must be returned:

```
http://localhost:5000/api/author?$first=
```