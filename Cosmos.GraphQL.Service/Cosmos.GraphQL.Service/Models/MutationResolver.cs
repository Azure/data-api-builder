using System.Collections.Generic;

namespace Cosmos.GraphQL.Service.Models
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
        public string id { get; set; }

        // TODO: add enum support
        public string operationType { get; set; }

        public string databaseName { get; set; }
        public string containerName { get; set; }

        public string fields { get; set; }
    }

    public enum Operation
    {
        Upsert, Delete, Create
    }

    public class Fields
    {
        public string type { get; set; }
        public FieldTransformation transformation { get; set; }
    }

    public interface FieldTransformation
    {

    }

    public class CrossDataSourceFieldTransformation : FieldTransformation
    {
        public string databaseName;
        public string containerName;
        public Dictionary<string, string> referenceFieldMap;
    }
}
