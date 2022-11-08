# Nested Filtering

## Scope
This doc describes the design and high level implementation details of nested filtering for SQL databases.

## What does nested filtering mean?
When two entities are related to each other, the capability to filter rows of one entity based on the values of the related entity is termed as nested filtering.

For an example configuration:
```json
    "Comic": {
      "source": "comics",
    ...
      "relationships": {
        "myseries": {
          "cardinality": "one",
          "target.entity": "series"
        }
      }
    },
    "series": {
      "source": "series",
    ...
      "relationships": {
        "comics": {
          "cardinality": "many",
          "target.entity": "Comic"
        }
      }
```

the following is a nested filter query that tries to obtain those comics that have the name of their `series` set to `Foundation`:
```graphql
{
    comics (filter: { myseries: { name: { eq: "Foundation" }}} ){
    items {
      id
      title
    }
  }
}
```

## Why do we need nested filtering?
A GraphQL client application needs to retrieve more data and then apply a filter in the client side in order to get the same results as the nested filtering query returns. 
For the above example, without nested filtering, the client has to query all the comics with myseries first, only to discard the rows where myseries.name = `Foundation`
```graphql
{
    comics {
    items {
      id
      title
      myseries {
        name
      }
    }
  }
}
```

Nested Filtering is useful to minimize the amount of data that is transferred as response from the backend.

## How to achieve nested filtering? 
1. Have a GraphQL Schema that allows filtering arguments which represent filter inputs of related entities.
    We already fulfill this requirement by providing such arguments for each of the related entities.
    For the above example, we already create the `ComicFilterInput` like:

    ```graphql
        """
    Filter input for Comic GraphQL type
    """
    input ComicFilterInput {
    id: IntFilterInput
    title: StringFilterInput
    volume: IntFilterInput
    categoryName: StringFilterInput
    series_id: IntFilterInput

    """
    Filter options for myseries
    """
    myseries: seriesFilterInput

    and: [ComicFilterInput]
    or: [ComicFilterInput]
    }
    ```

2. Generate the correct SQL query for the GraphQL nested filter.
    - MsSql
    For Azure SQL, the corresponding SQL query for the above example is:

    Option 1:
    ```sql
    SELECT 
    TOP 100 [table0].[id] AS [id], 
    [table0].[title] AS [title]
    FROM 
    [dbo].[comics] AS [table0]
    WHERE 
    1 = 1
    AND EXISTS (
        SELECT 1
        FROM [dbo].[series] AS [table1]
        WHERE [table1].[name] = 'Foundation'
        AND [table0].[series_id] = [table1].[id]
    )
    ORDER BY 
    [table0].[id] ASC FOR JSON PATH, 
    INCLUDE_NULL_VALUES
    ```

    Notice, it has the `EXISTS` clause with a subquery in it(that represents the related entity whose values are to be used to do the filtering)
    where we do a join with the outer query(that represents the parent entity whose rows are to be filtered) so that the filtering happens only on the related rows. For example, above, we add an `EXISTS` clause for the `series` subquery joined with the `comics` table from the outer query.
    All additional predicates related to `series` are moved into the subquery.

    The plan seen for this query with `EXISTS` clause looks like:
    ![Exists_subquery_plan](./nested-filter-exists-subquery-plan.png)

    Option 2:
    ```sql
    SELECT 
    TOP 100 [table0].[id] AS [id], 
    [table0].[title] AS [title]
    FROM 
    [dbo].[comics] AS [table0]
    INNER JOIN 
      [dbo].[series] AS [table1] ON 
      [table0].[series_id] = [table1].[id]
      AND [table1].[name] = 'Foundation'
    WHERE 
    1 = 1
    ORDER BY 
    [table0].[id] ASC FOR JSON PATH, 
    INCLUDE_NULL_VALUES
    ```

    In this query, we perform an INNER JOIN between the parent entity and each of the entities representing the nested filter with any additional predicates applied to the join itself. For example, `series` is inner joined with `comics` and the predicate on `series` is added to the join clause.


    The plan seen for this inner join query looks like:
    ![Inner_join_query_plan](./nested-filter-inner-join-plan.png)

As you can see, there are two scans involved in option 1 with `EXISTS` subquery whereas option 2 with `INNER JOIN` involves one scan and one seek. Since scans are costlier than seeks, we choose option 2 with inner joins for generating the equivalent SQL query for such a GraphQL request involing nested filters.

## Implementation Details

- When we parse the GraphQL filter arguments, we can identify if it is a nested filter object when the type of filter input is not a scalar i.e. NOT any of String, Boolean, Integer or Id filter input. 
- Once the nested filtering scenario is identified, we need to identify if it is a relational database(SQL) scenario or non-relational. If the source definition of the entity that is being filtered has non-zero primary key count, it is a SQL scenario. 
- Create a `SqlJoinStructure` for the nested filter object e.g. `series` so as to join with the parent entity - `comics`. The join predicate will be equality of primary keys of the nested and parent entities.
- Add this `SqlJoinStructure` to the `Joins` list property of the `SqlQueryStructure` representing the parent (`comics`).
- Recursively parse and obtain the predicates on the nested filter object while passing the original `Joins` property to subsequent recursive calls.
- All additional scalar predicates obtained from recursively parsing the nested filter object are added to the `SqlJoinStructure` corresponding to that nested filter.
- Create `SqlJoinStructure` for each subsequent recursive nested filter with join predicates between the caller and the called entities. For example, if `series` were to be further filtered based on its related `author` entity, we would create the `SqlJoinStructure` for `author` with have join predicates between primary key of `series` and `author`. However, we add this `SqlJoinStructure` to the same `Joins` property of the original parent entity that is passed down. This flattens all the inner joins and all the additional scalar predicates are appropriately added to the correct `SqlJoinStructure`.
- Continue with rest of the filters on the parent entity and eventually return a chain of predicates from the filter parser. If there were only nested filters, we wouldn't see any additional `WHERE` clause predicates - only joins.
- Build the join clause while building the original query structure by an `INNER JOIN`s for each of the `SqlJoinStructure` in the `Joins` list property.
