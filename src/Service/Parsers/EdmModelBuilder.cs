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
        /// Add the entity types found in the schema to the model
        /// </summary>
        /// <param name="sqlMetadataProvider">The MetadataProvider holds the objects needed
        /// to build the correct model.</param>
        /// <returns>this model builder</returns>
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
                        Type columnSystemType = sourceDefinition.Columns[column].SystemType;
                        // need to convert our column system type to an Edm type
                        EdmPrimitiveTypeKind type = GetEdmPrimitiveTypeFromSystemType(columnSystemType);

                        // here we must use the correct aliasing for the column name
                        // which is on a per entity basis.
                        // if column is in our list of keys we add as a key to entity
                        string exposedColumnName;
                        if (sourceDefinition.PrimaryKey.Contains(column))
                        {
                            sqlMetadataProvider.TryGetExposedColumnName(entityAndDbObject.Key, column, out exposedColumnName!);
                            newEntity.AddKeys(newEntity.AddStructuralProperty(name: exposedColumnName,
                                                                                type,
                                                                                isNullable: false));
                        }
                        else
                        {
                            // not a key just add the property
                            sqlMetadataProvider.TryGetExposedColumnName(entityAndDbObject.Key, column, out exposedColumnName!);
                            newEntity.AddStructuralProperty(name: exposedColumnName,
                                                            type,
                                                            isNullable: true);
                        }
                    }

                    // add this entity to our model
                    _model.AddElement(newEntity);
                }
            }

            return this;
        }

        /// <summary>
        /// Given the system type, returns the corresponding primitive type kind.
        /// </summary>
        /// <param name="columnSystemType">Type of the column.</param>
        /// <returns>EdmPrimitiveTypeKind</returns>
        /// <exception cref="ArgumentException">Throws when the column</exception>
        private static EdmPrimitiveTypeKind GetEdmPrimitiveTypeFromSystemType(Type columnSystemType)
        {
            if (columnSystemType.IsArray)
            {
                columnSystemType = columnSystemType.GetElementType()!;
            }

            EdmPrimitiveTypeKind type = columnSystemType.Name switch
            {
                "String" => EdmPrimitiveTypeKind.String,
                "Guid" => EdmPrimitiveTypeKind.Guid,
                "Byte" => EdmPrimitiveTypeKind.Byte,
                "Int16" => EdmPrimitiveTypeKind.Int16,
                "Int32" => EdmPrimitiveTypeKind.Int32,
                "Int64" => EdmPrimitiveTypeKind.Int64,
                "Single" => EdmPrimitiveTypeKind.Single,
                "Double" => EdmPrimitiveTypeKind.Double,
                "Decimal" => EdmPrimitiveTypeKind.Decimal,
                "Boolean" => EdmPrimitiveTypeKind.Boolean,
                "DateTime" => EdmPrimitiveTypeKind.DateTimeOffset,
                "DateTimeOffset" => EdmPrimitiveTypeKind.DateTimeOffset,
                "Date" => EdmPrimitiveTypeKind.Date,
                "TimeSpan" => EdmPrimitiveTypeKind.TimeOfDay,
                _ => throw new ArgumentException($"Column type" +
                        $" {columnSystemType.Name} not yet supported.")
            };

            return type;
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
