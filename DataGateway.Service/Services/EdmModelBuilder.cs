using System;
using System.Collections.Generic;
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
        private const string DEFAULT_NAMESPACE = "default_namespace";
        private const string DEFAULT_CONTAINER_NAME = "default_container";
        private readonly EdmModel _model = new();

#pragma warning disable CA1024 // EdmModelBuilders are recommended to have GetModel method
        public IEdmModel GetModel()
        {
            return _model;
        }

        /// <summary>
        /// Build the model from the provided schema.
        /// </summary>
        /// <param name="schema">DatabaseSchema that reresents the relevant schema.</param>
        /// <returns></returns>
        public EdmModelBuilder BuildModel(DatabaseSchema schema)
        {
            return BuildEntityTypes(schema).BuildEntitySets(schema);
        }

        /// <summary>
        /// Add the entity types found in the schema to the model
        /// </summary>
        /// <param name="schema">Schema represents the Database Schema</param>
        /// <returns>this model builder</returns>
        private EdmModelBuilder BuildEntityTypes(DatabaseSchema schema)
        {
            foreach (string entityName in schema.Tables.Keys)
            {
                EdmEntityType newEntity = new(DEFAULT_NAMESPACE, entityName);

                // each column represents a property of the current entity we are adding
                foreach (string column in schema.Tables[entityName].Columns.Keys)
                {
                    // need to convert our column type to an Edm type
                    ColumnType columnType = schema.Tables[entityName].Columns[column].Type;
                    System.Type systemType = ColumnDefinition.ResolveColumnTypeToSystemType(columnType);
                    EdmPrimitiveTypeKind type = EdmPrimitiveTypeKind.None;

                    if (systemType.GetType() == typeof(String))
                    {
                        type = EdmPrimitiveTypeKind.String;
                    }
                    else if (systemType.GetType() == typeof(Int64))
                    {
                        type = EdmPrimitiveTypeKind.Int64;
                    }
                    else
                    {
                        throw new ArgumentException($"No resolver for colum type {columnType}");
                    }

                    // if key is a single column we have type information and can add
                    if (column.Equals(GetPrimaryKeyFromList(schema.Tables[entityName].PrimaryKey)))
                    {
                        // entity is a structured type that needs a key, we add it here
                        newEntity.AddKeys(newEntity.AddStructuralProperty(column, type, isNullable: false));
                    }
                    else
                    {
                        // not a key just add the property
                        newEntity.AddStructuralProperty(column, type);
                    }
                }

                // if the key is not a single column we add it here as concatenation of columns making up key
                // need to change? what is type of composite key? For now adding as type None
                if (schema.Tables[entityName].PrimaryKey.Count > 1)
                {
                    newEntity.AddKeys(newEntity.AddStructuralProperty(GetPrimaryKeyFromList(schema.Tables[entityName].PrimaryKey), EdmPrimitiveTypeKind.None, isNullable: false));
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
        private EdmModelBuilder BuildEntitySets(DatabaseSchema schema)
        {
            EdmEntityContainer container = new(DEFAULT_NAMESPACE, DEFAULT_CONTAINER_NAME);
            _model.AddElement(container);

            // Entity set is a collection of the same entity, if we think of an entity as a row of data
            // that has a key, then an entity set can be thought of as a table made up of those rows
            foreach (string entityName in schema.Tables.Keys)
            {
                container.AddEntitySet(entityName, new EdmEntityType(DEFAULT_NAMESPACE, entityName));
            }

            return this;
        }

        private static string GetPrimaryKeyFromList(List<string> keyList)
        {
            string primaryKey = string.Empty;
            foreach (string keyPart in keyList)
            {
                primaryKey += keyPart;
            }

            return primaryKey;
        }
    }
}
