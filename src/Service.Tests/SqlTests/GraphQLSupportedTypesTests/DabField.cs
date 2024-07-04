// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests;

/// <summary>
/// Encapsulates field name metadata which is used when
/// creating database queries to validate test results
/// in the GraphQLSupportedTypesTestBase.
/// </summary>
public class DabField
{
    /// <summary>
    /// Mapped (aliased) column name defined in DAB runtime config.
    /// </summary>
    public string Alias { get; set; }

    /// <summary>
    /// Database column name.
    /// </summary>
    public string BackingColumnName { get; set; }

    /// <summary>
    /// Creates a new DabField instance with both alias and backing column name.
    /// </summary>
    /// <param name="alias">Mapped (aliased) column name defined in DAB runtime config.</param>
    /// <param name="backingColumnName">Database column name.</param>
    public DabField(string alias, string backingColumnName)
    {
        Alias = alias;
        BackingColumnName = backingColumnName;
    }

    /// <summary>
    /// Creates a new DabField instance with only the backing column name
    /// where the alias is the same as the backing column name.
    /// </summary>
    /// <param name="backingColumnName">Database column name.</param>
    public DabField(string backingColumnName)
    {
        Alias = backingColumnName;
        BackingColumnName = backingColumnName;
    }
}
