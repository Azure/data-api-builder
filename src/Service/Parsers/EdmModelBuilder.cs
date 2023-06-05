// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.OData.Edm;

namespace Azure.DataApiBuilder.Service.Parsers
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
        /// <param name="sqlMetadataProvider">The MetadataProvider holds the objects needed
        /// to build the correct model.</param>
        /// <returns>An EdmModelBuilder that can be used to get a model.</returns>
        public EdmModelBuilder BuildModel(ISqlMetadataProvider sqlMetadataProvider)
        {
            return BuildEntityTypes(sqlMetadataProvider)
                .BuildEntitySets(sqlMetadataProvider);
        }

        /// <summary>
        /// Build EdmEntityType objects for runtime config defined entities and add the created objects to the EdmModel.
        /// </summary>
        /// <param name="sqlMetadataProvider">Reference to entity names and associated database object metadata.</param>
        /// <returns>A reference to EdmModelBuilder after the operation has completed.</returns>
        private EdmModelBuilder BuildEntityTypes(ISqlMetadataProvider sqlMetadataProvider)
        {
            // since we allow for aliases to be used in place of the names of the actual
            // columns of the database object (such as table's columns), we need to
            // account for these potential aliases in our EDM Model.
            foreach (KeyValuePair<string, DatabaseObject> entityAndDbObject in sqlMetadataProvider.GetEntityNamesAndDbObjects())
            {
                // Do not add stored procedures, which do not have table definitions or conventional columns, to edm model
                // As of now, no ODataFilterParsing will be supported for stored procedure result sets
                if (entityAndDbObject.Value.SourceType is not SourceType.StoredProcedure)
                {
                    // given an entity Publisher with schema.table of dbo.publishers
                    // entitySourceName = dbo.publishers
                    // newEntityKey = Publisher.dbo.publishers
                    string entitySourceName = $"{entityAndDbObject.Value.FullName}";
                    string newEntityKey = $"{entityAndDbObject.Key}.{entitySourceName}";
                    EdmEntityType newEntity = new(DEFAULT_NAMESPACE, newEntityKey);
                    _entities.Add(newEntityKey, newEntity);

                    SourceDefinition sourceDefinition
                        = sqlMetadataProvider.GetSourceDefinition(entityAndDbObject.Key);

                    // each column represents a property of the current entity we are adding
                    foreach (string column in sourceDefinition.Columns.Keys)
                    {
                        // need to convert our column system type to an Edm type
                        Type columnSystemType = sourceDefinition.Columns[column].SystemType;
                        EdmPrimitiveTypeKind type = EdmPrimitiveTypeKind.None;
                        if (columnSystemType.IsArray)
                        {
                            columnSystemType = columnSystemType.GetElementType()!;
                        }

                        switch (columnSystemType.Name)
                        {
                            case "String":
                                type = EdmPrimitiveTypeKind.String;
                                break;
                            case "Guid":
                                type = EdmPrimitiveTypeKind.Guid;
                                break;
                            case "Byte":
                                type = EdmPrimitiveTypeKind.Byte;
                                break;
                            case "Int16":
                                type = EdmPrimitiveTypeKind.Int16;
                                break;
                            case "Int32":
                                type = EdmPrimitiveTypeKind.Int32;
                                break;
                            case "Int64":
                                type = EdmPrimitiveTypeKind.Int64;
                                break;
                            case "Single":
                                type = EdmPrimitiveTypeKind.Single;
                                break;
                            case "Double":
                                type = EdmPrimitiveTypeKind.Double;
                                break;
                            case "Decimal":
                                type = EdmPrimitiveTypeKind.Decimal;
                                break;
                            case "Boolean":
                                type = EdmPrimitiveTypeKind.Boolean;
                                break;
                            case "DateTime":
                            case "DateTimeOffset":
                                type = EdmPrimitiveTypeKind.DateTimeOffset;
                                break;
                            case "Date":
                                type = EdmPrimitiveTypeKind.Date;
                                break;
                            default:
                                throw new ArgumentException($"Column type" +
                                    $" {columnSystemType.Name} not yet supported.");
                        }

                        // The mapped (aliased) field name defined in the runtime config is used to create a representative
                        // OData StructuralProperty. The created property is then added to the EdmEntityType.
                        // StructuralProperty objects representing database primary keys are added as a 'keyProperties' to the EdmEntityType.
                        // Otherwise, the StructuralProperty object is added as a generic StructuralProperty of the EdmEntityType.
                        string exposedColumnName;
                        if (sourceDefinition.PrimaryKey.Contains(column))
                        {
                            sqlMetadataProvider.TryGetExposedColumnName(entityAndDbObject.Key, column, out exposedColumnName!);
                            newEntity.AddKeys(newEntity.AddStructuralProperty(name: exposedColumnName, type, isNullable: false));
                        }
                        else
                        {
                            sqlMetadataProvider.TryGetExposedColumnName(entityAndDbObject.Key, column, out exposedColumnName!);
                            newEntity.AddStructuralProperty(name: exposedColumnName, type, isNullable: true);
                        }
                    }

                    // Add the created EdmEntityType to the EdmModel
                    _model.AddElement(newEntity);
                }
            }

            return this;
        }

        /// <summary>
        /// Add the entity sets contained within the schema to container.
        /// </summary>
        /// <param name="sqlMetadataProvider">The MetadataProvider holds the objects needed
        /// to build the correct model.</param>
        /// <returns>this model builder</returns>
        private EdmModelBuilder BuildEntitySets(ISqlMetadataProvider sqlMetadataProvider)
        {
            EdmEntityContainer container = new(DEFAULT_NAMESPACE, DEFAULT_CONTAINER_NAME);
            _model.AddElement(container);

            // Entity set is a collection of the same entity, if we think of an entity as a row of data
            // that has a key, then an entity set can be thought of as a table made up of those rows.
            foreach (KeyValuePair<string, DatabaseObject> entityAndDbObject in sqlMetadataProvider.GetEntityNamesAndDbObjects())
            {
                if (entityAndDbObject.Value.SourceType != SourceType.StoredProcedure)
                {
                    string entityName = $"{entityAndDbObject.Value.FullName}";
                    container.AddEntitySet(name: $"{entityAndDbObject.Key}.{entityName}", _entities[$"{entityAndDbObject.Key}.{entityName}"]);
                }
            }

            return this;
        }
    }
}
