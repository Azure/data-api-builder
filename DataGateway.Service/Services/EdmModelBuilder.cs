using System;
using System.Collections.Generic;
using Azure.DataGateway.Config;
using Microsoft.OData.Edm;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// This class represents an EdmModelBuilder which can build the needed
    /// EdmModel from the schema provided, allowing for OData filter parsing
    /// </summary>
    public class EdmModelBuilder
    {
        private const string DEFAULT_NAMESPACE = "default_namespace";
        private const string DEFAULT_CONTAINER_NAME = "default_container";
        private readonly Dictionary<string, EdmEntityType> _entities = new();
        private readonly EdmModel _model = new();

#pragma warning disable CA1024 // EdmModelBuilders are recommended to have GetModel method
        public IEdmModel GetModel()
        {
            return _model;
        }

        /// <summary>
        /// Build the model from the provided schema.
        /// </summary>
        /// <param name="databaseEntities">All the exposed sql database entities
        /// with their table definitions.</param>
        /// <returns>An EdmModelBuilder that can be used to get a model.</returns>
        public EdmModelBuilder BuildModel(Dictionary<string, Entity> databaseEntities)
        {
            return BuildEntityTypes(databaseEntities)
                .BuildEntitySets(databaseEntities.Values);
        }

        /// <summary>
        /// Add the entity types found in the schema to the model
        /// </summary>
        /// <param name="databaseEntities">All the exposed sql database entities
        /// with their table definitions.</param>
        /// <returns>this model builder</returns>
        private EdmModelBuilder BuildEntityTypes(
            Dictionary<string, Entity> databaseEntities)
        {
            foreach (Entity entity in databaseEntities.Values)
            {
                SqlEntity sqlEntity = (SqlEntity)entity;
                string entitySourceName = sqlEntity.SourceName;
                TableDefinition tableDefinition = sqlEntity.TableDefinition;
                EdmEntityType newEntity = new(DEFAULT_NAMESPACE, entitySourceName);
                string newEntityKey = DEFAULT_NAMESPACE + entitySourceName;
                _entities.Add(newEntityKey, newEntity);

                // each column represents a property of the current entity we are adding
                foreach (string column in
                    tableDefinition.Columns.Keys)
                {
                    // need to convert our column system type to an Edm type
                    Type columnSystemType = tableDefinition.Columns[column].SystemType;
                    EdmPrimitiveTypeKind type = EdmPrimitiveTypeKind.None;
                    if (columnSystemType.IsArray)
                    {
                        columnSystemType = columnSystemType.GetElementType()!;
                    }

                    switch (Type.GetTypeCode(columnSystemType))
                    {
                        case TypeCode.String:
                            type = EdmPrimitiveTypeKind.String;
                            break;
                        case TypeCode.Int64:
                            type = EdmPrimitiveTypeKind.Int64;
                            break;
                        case TypeCode.Single:
                            type = EdmPrimitiveTypeKind.Single;
                            break;
                        case TypeCode.Double:
                            type = EdmPrimitiveTypeKind.Double;
                            break;
                        default:
                            throw new ArgumentException($"No resolver for column type" +
                                $" {columnSystemType.Name}");
                    }

                    // if column is in our list of keys we add as a key to entity
                    if (tableDefinition.PrimaryKey.Contains(column))
                    {
                        newEntity.AddKeys(newEntity.AddStructuralProperty(column, type, isNullable: false));
                    }
                    else
                    {
                        // not a key just add the property
                        newEntity.AddStructuralProperty(column, type, isNullable: true);
                    }
                }

                // add this entity to our model
                _model.AddElement(newEntity);

            }

            return this;
        }

        /// <summary>
        /// Add the entity sets contained within the schema to container.
        /// </summary>
        /// <param name="sqlEntities">All the sql entities with their table definitions.</param>
        /// <returns>this model builder</returns>
        private EdmModelBuilder BuildEntitySets(IEnumerable<Entity> entities)
        {
            EdmEntityContainer container = new(DEFAULT_NAMESPACE, DEFAULT_CONTAINER_NAME);
            _model.AddElement(container);

            // Entity set is a collection of the same entity, if we think of an entity as a row of data
            // that has a key, then an entity set can be thought of as a table made up of those rows
            foreach (Entity entity in entities)
            {
                SqlEntity sqlEntity = (SqlEntity)entity;
                string entityName = sqlEntity.SourceName;
                container.AddEntitySet(name: entityName, _entities[DEFAULT_NAMESPACE + entityName]);
            }

            return this;
        }
    }
}
