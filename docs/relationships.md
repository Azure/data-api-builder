# Relationships

GraphQL queries can traverse related objects and their fields, so that with just one query you can write something like:

```graphql
{
  books
  {
    items {
      id
      title    
      authors {
        items {
          first_name
          last_name
        }
      }
    }
  }
}
```

to retrieve books and their authors.

To allow this ability to work, Data API Builder needs to know how the two objects are related to each other. The `relationships` section in the configuration file provide the necessary metadata for making this ability working correctly and efficiently.

## Configuring a Relationship

No matter what database you are using with Data API Builder, you have to explicitly tell Data API Builder that an object is related to another one even if, using Foreign Key metadata when available, it could infer it automatically. This is done to allow you to have full control on what is exposed via GraphQL and what not.

There are three types of relatioship that can be established between two entities:

- [One-To-Many Relationship](#one-to-many-relationship)
- [Many-To-One Relationship](#many-to-one-relationship)
- [Many-To-Many Relationship](#many-to-many-relationship)

### One-To-Many Relationship

A one-to-many relationship allows an object to access a list of related objects. For example a books series can allow access to all the books in that series:

```graphql
{
  series {
    items {
      name
      books {
        items {
          title
        }
      }
    }
  }
}
```

If there are Foreign Key supporting the relationship between the two underlying database objects, you only need to tell Data API Builder, that you want to expose such relationship. With DAB CLI:

```bash
dab update series --relationship books --target.entity book --cardinality many 
```

which will update the `series` entity - used in the example - to look like the following:

```json
"series": {
  "source": "dbo.series",
  ...
  "relationships": {
    "books": {
      "target.entity": "book",
      "cardinality": "many"    
    }
  }
  ...
}
```

A new key has been added under the `relationships` element: `books`. The element defines the name that will be used for GraphQL field to navigate from the series object to the object defined in the `target.entity`, `book` in this case. This means that there must be an entity called `book` in configuration file.

`cardinality` property tells Data API Builder that there can be many books in each series, so the created GraphQL field will return a list of items.

That's all you need. At startup Data API Builder will automatically detect the database fields that needs to be used to sustain the defined relationship.

If you don't have a Foreign Key constraint sustaining the database relationship, Data API Builder cannot figure out automatically what fields will be used to relate the two entities, so you have to provide it manually. You can do it with DAB CLI:

```bash
dab update series --relationship books --target.entity book --cardinality many  --relationship.fields "id:series_id"
```

The option `relationship.fields` allows you to define which fields will be used from the entity being updated, `series` in the sample, and which fields will be used from the target entity, `book` in the sample, to connect the data from one entity to the other. 

In the above sample, the `id` database field of the `series` entity will be matched with the database field `series_id` of the `book` entity.

The configuration will also contain that information:

```json
"series": {
  "source": "dbo.series",
  ...
  "relationships": {
    "books": {
      "cardinality": "many",
      "target.entity": "book",
      "source.fields": ["id"],
      "target.fields": ["series_id"]
    }    
  }
  ...
}
```

### Many-To-One Relationship

A many to one relationship is similar to the One-To-Many relationship with two major differences:

- the `cardinality` is set to `one`
- the created GraphQL field will return a scalar not a list

Following the Book Series samples used before, a book can be in just one series, so the relationship will be created using the following DAB CLI command:

```bash
dab update book --relationship series --target.entity series --cardinality one
```

which will generate the following configuration:

```json
"book": {
  "source": "dbo.books",
  ...
  "relationships": {       
    "series": {
      "target.entity": "series",
      "cardinality": "one"
    }
  }
}
```

which, in turn, will allow a GraphQL query like the following:

```graphql
{
  books {
    items {
      id
      title    
      series {
        name
      }
    }
  }
}
```

where each book will return also the series it belongs to.

### Many-To-Many Relationship

Many to many relationships can be seen as a pair of One-to-Many and Many-to-One relationships working together.
