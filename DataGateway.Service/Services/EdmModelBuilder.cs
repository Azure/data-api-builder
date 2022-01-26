using System;
using Azure.DataGateway.Service.Models;
using Microsoft.OData.Edm;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// This class represents an EdmModelBuilder which can builder the needed
    /// EdmModel from the schema provided, allowing for OData filter parsing
    /// </summary>
    public class EdmModelBuilder
    {
        private readonly EdmModel _model = new();
        private EdmEntityContainer _defaultContainer;

        public EdmModelBuilder()
        {
            _defaultContainer = new EdmEntityContainer("default_namespace", "default_container");
            _model.AddElement(_defaultContainer);

        }

#pragma warning disable CA1024 // EdmModelBuilders are recommended to have GetModel method
        public IEdmModel GetModel()
        {
            return _model;
        }

        /// <summary>
        /// Add the entity types found in the schema to the model
        /// </summary>
        /// <param name="schema">Schema represents the Database Schema</param>
        /// <param name="nullable">Represents if a type is nullable</param>
        /// <returns>this model builder</returns>
        public EdmModelBuilder BuildEntityTypes(
            DatabaseSchema schema,
            bool nullable)
        {
            foreach (string entityName in schema.Tables.Keys)
            {
                EdmEntityType newEntity = new("default", entityName);
                string primaryKey = string.Empty;
                EdmPrimitiveTypeKind keyType = EdmPrimitiveTypeKind.None;

                // each column represents a property of the current entity we are adding
                foreach (string column in schema.Tables[entityName].Columns.Keys)
                {
                    // need to convert our column type to an Edm type
                    ColumnType columnType = schema.Tables[entityName].Columns[column].Type;
                    System.Type systemType = ColumnDefinition.ResolveColumnTypeToSystemType(columnType);
                    EdmPrimitiveTypeKind type = systemType == System.Type.GetType(nameof(Int64)) ? EdmPrimitiveTypeKind.Int64 :
                        systemType == System.Type.GetType(nameof(String)) ? EdmPrimitiveTypeKind.String :
                        throw new ArgumentException($"No resolver for colum type {columnType}");
                    newEntity.AddStructuralProperty(column, type);
                    // save the information on the key
                    if (column.Equals(schema.Tables[column].PrimaryKey.ToString()))
                    {
                        primaryKey = column;
                        keyType = type;
                    }
                }

                // entity is a structured type that needs a key, we add it here
                newEntity.AddKeys(newEntity.AddStructuralProperty(primaryKey, keyType, isNullable: false));

                // add this entity to our model
                _model.AddElement(newEntity);
            }

            return this;
        }

        /// <summary>
        /// Add the entity sets contained within the schema to container
        /// </summary>
        /// <param name="schema">Schema represents the Database Schema</param>
        /// <returns>this model builder</returns>
        public EdmModelBuilder BuildEntitySets(DatabaseSchema schema)
        {

            // Entity set is a collection of the same entity, if we think of an entity as a row of data
            // that has a key, then an entity set can be thought of as a table made up of those rows
            foreach (string entityName in schema.Tables.Keys)
            {
                _defaultContainer.AddEntitySet(entityName, new EdmEntityType("default", entityName));
            }

            return this;
        }
    }
}
