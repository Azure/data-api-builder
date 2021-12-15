using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models
{
    public class MutationResolver
    {
        /**
         * {
    "name": "addBook",
    "database": "db1",
    "container": "container1",
    "operationType": "UPSERT"
    "partitionKeyField": "", //(only for option 2)
    "fields":[
    "author": {
        "type":"CROSS_DATASOURCE",
        transformation : {
            "databaseName": "",
            "containerName": "",
            "parentReferenceField": "authorId"
            "referenceFieldMap": "[{"parentField1","referenceFIeld1"},
                            {"parentField2","referenceFIeld2"}],
            "partitionKeyField": "" //(only for option 2)
        }
    },
    ]
// for option-1 partition key value is same as ID.
#TODO: How to handle atomicity of nested types referring to different container
}
         */
        public string Id { get; set; }

        // TODO: add enum support
        public string OperationType { get; set; }
        public string DatabaseName { get; set; }
        public string ContainerName { get; set; }
        public string Fields { get; set; }
        public string Table { get; set; }

        ///<summary>
        /// Maps the parameter names of the update query to table names
        ///</summary>
        ///<remarks>
        /// Because Update queries are configured to find the row to update by using the primary key,
        /// in order to edit primary key columns (if this is desirable is up for debate)
        /// a mapping is required.
        /// e.g. <code> changeReviewBook(id: Int!, book_id: Int!, new_book_id: Int): Review </code>
        /// Note that in the example above an alias is needed to give the new value of book_id
        ///</remarks>
        public Dictionary<string, string> UpdateFieldToColumnMappings { get; set; }
    }

    public enum Operation
    {
        Upsert, Delete, Create
    }
}
