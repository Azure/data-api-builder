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
    }

    public enum Operation
    {
        Upsert, Delete, Create
    }

    public class Fields
    {
        public string Type { get; set; }
        public IFieldTransformation Transformation { get; set; }
    }

    public interface IFieldTransformation
    {

    }

    public class CrossDataSourceFieldTransformation : IFieldTransformation
    {
        private string DatabaseName { get; set; }
        private string ContainerName { get; set; }
        private Dictionary<string, string> ReferenceFieldMap { get; set; }
    }
}
