using System;
using System.Collections.Generic;
using Azure.DataGateway.Config;
using Microsoft.OData.Edm;

namespace Azure.DataGateway.Service.Parsers
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
        /// <param name="entitiesToDatabaseObjects">Entities mapped to their database objects.</param>
        /// <param name="entityBackingColumnsToExposedNames">Entites mapped to their mappings of backing
        /// columns to exposed names.</param>
        /// <returns>An EdmModelBuilder that can be used to get a model.</returns>
        public EdmModelBuilder BuildModel(Dictionary<string, DatabaseObject> entitiesToDatabaseObjects,
                                          Dictionary<string, Dictionary<string, string>> entityBackingColumnsToExposedNames)
        {
            return BuildEntityTypes(entitiesToDatabaseObjects, entityBackingColumnsToExposedNames)
                .BuildEntitySets(entitiesToDatabaseObjects);
        }

        /// <summary>
        /// Add the entity types found in the schema to the model
        /// </summary>
        /// <param name="entitiesToDatabaseObjects">Entities mapped to their database objects.</param>
        /// <param name="entityBackingColumnsToExposedNames">Entites mapped to their mappings of backing
        /// columns to exposed names.</param>
        /// <returns>this model builder</returns>
        private EdmModelBuilder BuildEntityTypes(
            Dictionary<string, DatabaseObject> entitiesToDatabaseObjects,
            Dictionary<string, Dictionary<string, string>> entityBackingColumnsToExposedNames)
        {
            // since we allow for aliases to be used in place of the names of the actual
            // database object, we need to account for these potential aliases in our EDM Model.
            foreach (KeyValuePair<string, DatabaseObject> entityAndDbObject in entitiesToDatabaseObjects)
            {
                string entitySourceName = $"{entityAndDbObject.Value.FullName}";
                TableDefinition tableDefinition = entityAndDbObject.Value.TableDefinition;
                EdmEntityType newEntity = new(entityAndDbObject.Key, entitySourceName);
                string newEntityKey = $"{entityAndDbObject.Key}.{entitySourceName}";
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
                            throw new ArgumentException($"Column type" +
                                $" {columnSystemType.Name} not yet supported.");
                    }

                    // here we must use the correct aliasing for the column name
                    // which is on a per entity basis.
                    // if column is in our list of keys we add as a key to entity
                    if (tableDefinition.PrimaryKey.Contains(column))
                    {
                        newEntity.AddKeys(newEntity.AddStructuralProperty(name: entityBackingColumnsToExposedNames[entityAndDbObject.Key][column],
                                                                          type,
                                                                          isNullable: false));
                    }
                    else
                    {
                        // not a key just add the property
                        newEntity.AddStructuralProperty(name: entityBackingColumnsToExposedNames[entityAndDbObject.Key][column],
                                                        type,
                                                        isNullable: true);
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
        /// <param name="databaseObjects">A mapping of entities to their corresponding database object.</param>
        /// <returns>this model builder</returns>
        private EdmModelBuilder BuildEntitySets(Dictionary<string, DatabaseObject> databaseObjects)
        {
            EdmEntityContainer container = new(DEFAULT_NAMESPACE, DEFAULT_CONTAINER_NAME);
            _model.AddElement(container);

            // Entity set is a collection of the same entity, if we think of an entity as a row of data
            // that has a key, then an entity set can be thought of as a table made up of those rows.
            foreach (KeyValuePair<string, DatabaseObject> entityAndDbObject in databaseObjects)
            {
                string entityName = $"{entityAndDbObject.Value.FullName}";
                container.AddEntitySet(name: entityName, _entities[$"{entityAndDbObject.Key}.{entityName}"]);
            }

            return this;
        }
    }
}
