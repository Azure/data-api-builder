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
        private const string DEFAULT_NAME = "default";
        private readonly EdmModel _model = new();
        private EdmEntityContainer _defaultContainer;

        public EdmModelBuilder()
        {
            _defaultContainer = new EdmEntityContainer(DEFAULT_NAME, DEFAULT_NAME);
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
        /// <returns>this model builder</returns>
        public EdmModelBuilder BuildEntityTypes(DatabaseSchema schema)
        {
            foreach (string entityName in schema.Tables.Keys)
            {
                EdmEntityType newEntity = new(DEFAULT_NAME, entityName);

                // each column represents a property of the current entity we are adding
                foreach (string column in schema.Tables[entityName].Columns.Keys)
                {
                    // need to convert our column type to an Edm type
                    ColumnType columnType = schema.Tables[entityName].Columns[column].Type;
                    System.Type systemType = ColumnDefinition.ResolveColumnTypeToSystemType(columnType);
                    EdmPrimitiveTypeKind type = EdmPrimitiveTypeKind.None;

                    if (systemType == System.Type.GetType(nameof(String)))
                    {
                        type = EdmPrimitiveTypeKind.String;
                    }
                    else if (systemType == System.Type.GetType(nameof(Int64)))
                    {
                        type = EdmPrimitiveTypeKind.Int64;
                    }
                    else
                    {
                        throw new ArgumentException($"No resolver for colum type {columnType}");
                    }

                    newEntity.AddStructuralProperty(column, type);

                    if (column.Equals(schema.Tables[column].PrimaryKey.ToString()))
                    {
                        // entity is a structured type that needs a key, we add it here
                        newEntity.AddKeys(newEntity.AddStructuralProperty(column, type, isNullable: false));
                    }
                }

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
                _defaultContainer.AddEntitySet(entityName, new EdmEntityType(DEFAULT_NAME, entityName));
            }

            return this;
        }
    }
}
