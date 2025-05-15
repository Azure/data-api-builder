// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.DatabasePrimitives;

/// <summary>
/// Represents a database object - which could be a view, table, or stored procedure.
/// </summary>
public abstract class DatabaseObject
{
    public string SchemaName { get; set; } = null!;

    public string Name { get; set; } = null!;

    public EntitySourceType SourceType { get; set; } = EntitySourceType.Table;

    public DatabaseObject(string schemaName, string tableName)
    {
        SchemaName = schemaName;
        Name = tableName;
    }

    public DatabaseObject() { }

    public string FullName
    {
        get
        {
            return string.IsNullOrEmpty(SchemaName) ? Name : $"{SchemaName}.{Name}";
        }
    }

    public override bool Equals(object? other)
    {
        return Equals(other as DatabaseObject);
    }

    public bool Equals(DatabaseObject? other)
    {
        return other is not null &&
               SchemaName.Equals(other.SchemaName) &&
               Name.Equals(other.Name);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(SchemaName, Name);
    }

    /// <summary>
    /// Get the underlying SourceDefinition based on database object source type
    /// </summary>
    public virtual SourceDefinition SourceDefinition
    {
        get
        {
            return SourceType switch
            {
                EntitySourceType.Table => ((DatabaseTable)this).TableDefinition,
                EntitySourceType.View => ((DatabaseView)this).ViewDefinition,
                EntitySourceType.StoredProcedure => ((DatabaseStoredProcedure)this).StoredProcedureDefinition,
                _ => throw new Exception(
                        message: $"Unsupported EntitySourceType. It can either be Table,View, or Stored Procedure.")
            };
        }
    }
}

/// <summary>
/// Sub-class of DatabaseObject class, represents a table in the database.
/// </summary>
[DebuggerDisplay("Table: {FullName}")]
public class DatabaseTable : DatabaseObject
{
    public DatabaseTable(string schemaName, string tableName)
        : base(schemaName, tableName) { }

    public DatabaseTable() { }
    public SourceDefinition TableDefinition { get; set; } = null!;
}

/// <summary>
/// Sub-class of DatabaseObject class, represents a view in the database.
/// </summary>
public class DatabaseView : DatabaseObject
{
    public DatabaseView(string schemaName, string tableName)
        : base(schemaName, tableName) { }

    public DatabaseView() { }
    public ViewDefinition ViewDefinition { get; set; } = null!;
}

/// <summary>
/// Sub-class of DatabaseObject class, represents a stored procedure in the database.
/// </summary>
public class DatabaseStoredProcedure : DatabaseObject
{
    public DatabaseStoredProcedure(string schemaName, string tableName)
        : base(schemaName, tableName) { }

    public DatabaseStoredProcedure() { }
    public StoredProcedureDefinition StoredProcedureDefinition { get; set; } = null!;
}

public class StoredProcedureDefinition : SourceDefinition
{
    /// <summary>
    /// The list of input parameters
    /// Key: parameter name, Value: ParameterDefinition object
    /// </summary>
    public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new();

    /// <inheritdoc/>
    public override DbType? GetDbTypeForParam(string paramName)
    {
        if (Parameters.TryGetValue(paramName, out ParameterDefinition? paramDefinition))
        {
            return paramDefinition.DbType;
        }

        return null;
    }
}

public class ParameterDefinition
{
    public Type SystemType { get; set; } = null!;
    public DbType? DbType { get; set; }
    public SqlDbType? SqlDbType { get; set; }
    public bool HasConfigDefault { get; set; }
    public object? ConfigDefaultValue { get; set; }
}

/// <summary>
/// Class to store database table definition. It contains properties that are
/// common between a database table and a view.
/// </summary>
public class SourceDefinition
{
    /// <summary>
    /// The list of columns that together form the primary key of the source.
    /// </summary>
    public List<string> PrimaryKey { get; set; } = new();

    /// <summary>
    /// The list of columns in this source.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, ColumnDefinition> Columns { get; private set; } =
        new(StringComparer.InvariantCultureIgnoreCase);

    /// <summary>
    /// A dictionary mapping all the source entities to their relationship metadata.
    /// All these entities share this source definition
    /// as their underlying database object.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, RelationshipMetadata> SourceEntityRelationshipMap { get; private set; } =
        new(StringComparer.InvariantCultureIgnoreCase);

    /// <summary>
    /// Indicates whether an update trigger enabled on the table.
    /// The default value must be kept as false, meaning by default we assume no trigger is enabled.
    /// Based on whether trigger is enabled, we use either OUTPUT (when no trigger is enabled) / or SELECT (when a trigger is enabled),
    /// to return the data after a mutation operation.
    /// </summary>
    public bool IsUpdateDMLTriggerEnabled;

    /// <summary>
    /// Indicates whether an insert trigger enabled on the table.
    /// The default value must be kept as false, meaning by default we assume no trigger is enabled.
    /// Based on whether trigger is enabled, we use either OUTPUT (when no trigger is enabled) / or SELECT (when a trigger is enabled),
    /// to return the data after a mutation operation.
    public bool IsInsertDMLTriggerEnabled;

    /// <summary>
    /// Given the list of column names to check, evaluates
    /// if any of them is a nullable column when matched with the columns in this source definition.
    /// </summary>
    /// <param name="columnsToCheck">List of column names.</param>
    /// <returns>True if any of the columns is null, false otherwise.</returns>
    public bool IsAnyColumnNullable(List<string> columnsToCheck)
    {
        // If any of the given columns are nullable, the relationship is nullable.
        return columnsToCheck.Select(column =>
                                     Columns.TryGetValue(column, out ColumnDefinition? definition) && definition.IsNullable)
                             .Where(isNullable => isNullable == true)
                             .Any();
    }

    /// <summary>
    /// Method to get the DbType for:
    /// 1. column for table/view,
    /// 2. parameter for stored procedure.
    /// </summary>
    /// <param name="paramName">The parameter whose DbType is to be determined.
    /// For table/view paramName refers to the backingColumnName if aliases are used.</param>
    /// <returns>DbType for the parameter.</returns>
    public virtual DbType? GetDbTypeForParam(string paramName)
    {
        if (Columns.TryGetValue(paramName, out ColumnDefinition? columnDefinition))
        {
            return columnDefinition.DbType;
        }

        return null;
    }

    /// <summary>
    /// Method to get the SqlDbType for:
    /// 1. column for table/view,
    /// 2. parameter for stored procedure.
    /// </summary>
    /// <param name="paramName">The parameter whose SqlDbType is to be determined.
    /// For table/view paramName refers to the backingColumnName if aliases are used.</param>
    /// <returns>SqlDbType for the parameter.</returns>
    public virtual SqlDbType? GetSqlDbTypeForParam(string paramName)
    {
        if (Columns.TryGetValue(paramName, out ColumnDefinition? columnDefinition))
        {
            return columnDefinition.SqlDbType;
        }

        return null;
    }
}

/// <summary>
/// Class to store the database view definition.
/// </summary>
public class ViewDefinition : SourceDefinition { }

/// <summary>
/// Class encapsulating foreign keys corresponding to target entities.
/// </summary>
public class RelationshipMetadata
{
    /// <summary>
    /// Dictionary tracking ForeignKeyDefinitions for a given target entity.
    /// key: {targetEntityName}
    /// value: [{ForeignKeyDefinition},{...}]
    /// </summary>
    [JsonInclude]
    public Dictionary<string, List<ForeignKeyDefinition>> TargetEntityToFkDefinitionMap { get; private set; }
        = new(StringComparer.InvariantCultureIgnoreCase);
}

public class ColumnDefinition
{
    /// <summary>
    /// The database type of this column mapped to the SystemType.
    /// </summary>
    public Type SystemType { get; set; } = typeof(object);
    public DbType? DbType { get; set; }
    public SqlDbType? SqlDbType { get; set; }
    public bool HasDefault { get; set; }
    public bool IsAutoGenerated { get; set; }
    public bool IsNullable { get; set; }
    public bool IsReadOnly { get; set; }
    public object? DefaultValue { get; set; }

    public ColumnDefinition() { }

    public ColumnDefinition(Type systemType)
    {
        SystemType = systemType;
    }
}

[DebuggerDisplay("Relationship: {RelationshipName} ReferencingDbTable = {Pair.ReferencingDbTable.FullName} (Count = {ReferencingColumns.Count}), ReferencedDbTable = {Pair.ReferencedDbTable.FullName} (Count = {ReferencedColumns.Count})")]
public class ForeignKeyDefinition
{
    public string SourceEntityName { get; set; } = string.Empty;

    /// <summary>
    /// Identifies whether the referencing entity is the target, source, or linking entity.
    /// Enables Resolve{Target/Source}Columns() functions to return the correct columns.
    /// </summary>
    public RelationshipRole ReferencingEntityRole { get; init; } = RelationshipRole.None;

    /// <summary>
    /// Identifies whether the referenced entity is the target, source, or linking entity.
    /// Enables Resolve{Target/Source}Columns() functions to return the correct columns.
    /// </summary>
    public RelationshipRole ReferencedEntityRole { get; init; } = RelationshipRole.None;

    /// <summary>
    /// The relationship name defined for the relationship in the runtime config.
    /// </summary>
    public string RelationshipName { get; init; } = string.Empty;

    /// <summary>
    /// The referencing and referenced table pair.
    /// </summary>
    public RelationShipPair Pair { get; set; } = new();

    /// <summary>
    /// The list of columns referenced in the reference table.
    /// If this list is empty, the primary key columns of the referenced
    /// table are implicitly assumed to be the referenced columns.
    /// </summary>
    public List<string> ReferencedColumns { get; set; } = new();

    /// <summary>
    /// The list of columns of the table that make up the foreign key.
    /// If this list is empty, the primary key columns of the referencing
    /// table are implicitly assumed to be the foreign key columns.
    /// </summary>
    public List<string> ReferencingColumns { get; set; } = new();

    /// <summary>
    /// Resolves the target columns based on the role of the referencing and referenced entities.
    /// The role of the referencing and referenced entities is important because
    /// target entity can either be the "referenced" or "referencing" entity.
    /// </summary>
    /// <returns>List of the target entity's columns</returns>
    /// <exception cref="Exception">Raised when the ForeignKeyDefinition represents a relationship
    /// between a source (source fields) and linking object (linking source fields).</exception>
    public List<string> ResolveTargetColumns()
    {
        if (ReferencingEntityRole == RelationshipRole.Target)
        {
            return ReferencingColumns;
        }
        else if (ReferencedEntityRole == RelationshipRole.Target)
        {
            return ReferencedColumns;
        }
        else
        {
            throw new Exception(
                message: "Unable to resolve target columns because this ForeignKeyDefinition relates a target entity and linking object.");
        }
    }

    /// <summary>
    /// Resolves the source columns based on the role of the referencing and referenced entities.
    /// The role of the referencing and referenced entities is important because
    /// source entity can either be the "referenced" or "referencing" entity.
    /// </summary>
    /// <returns>List of the source entity's columns</returns>
    /// <exception cref="Exception">Raised when the ForeignKeyDefinition represents a relationship
    /// between a target (target columns) and linking object (linking target fields).</exception>
    public List<string> ResolveSourceColumns()
    {
        if (ReferencingEntityRole == RelationshipRole.Source)
        {
            return ReferencingColumns;
        }
        else if (ReferencedEntityRole == RelationshipRole.Source)
        {
            return ReferencedColumns;
        }
        else
        {
            throw new Exception(
                message: "Unable to resolve source columns because this ForeignKeyDefinition relates a source entity and linking object.");
        }
    }

    public override bool Equals(object? other)
    {
        return Equals(other as ForeignKeyDefinition);
    }

    public bool Equals(ForeignKeyDefinition? other)
    {
        return other != null &&
               Pair.Equals(other.Pair) &&
               ReferencedColumns.SequenceEqual(other.ReferencedColumns) &&
               ReferencingColumns.SequenceEqual(other.ReferencingColumns);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
                Pair, ReferencedColumns, ReferencingColumns);
    }
}

[DebuggerDisplay("ReferencingDbTable = {ReferencingDbTable.FullName}, ReferencedDbTable = {ReferencedDbTable.FullName}")]
public class RelationShipPair
{
    /// <summary>
    /// Relationship name specified in the runtime config.
    /// </summary>
    public string RelationshipName { get; set; } = string.Empty;

    public RelationShipPair() { }

    public RelationShipPair(
    DatabaseTable referencingDbObject,
    DatabaseTable referencedDbObject)
    {
        ReferencingDbTable = referencingDbObject;
        ReferencedDbTable = referencedDbObject;
    }

    public RelationShipPair(
        string relationshipName,
        DatabaseTable referencingDbObject,
        DatabaseTable referencedDbObject)
    {
        RelationshipName = relationshipName;
        ReferencingDbTable = referencingDbObject;
        ReferencedDbTable = referencedDbObject;
    }

    public DatabaseTable ReferencingDbTable { get; set; } = new();

    public DatabaseTable ReferencedDbTable { get; set; } = new();

    public override bool Equals(object? other)
    {
        return Equals(other as RelationShipPair);
    }

    public bool Equals(RelationShipPair? other)
    {
        return other != null &&
               ReferencedDbTable.Equals(other.ReferencedDbTable) &&
               ReferencingDbTable.Equals(other.ReferencingDbTable);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
                ReferencedDbTable, ReferencingDbTable);
    }
}
