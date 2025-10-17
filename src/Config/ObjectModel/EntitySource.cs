// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Describes the type, name, parameters, and key fields for a
/// database object source.
/// </summary>
/// <param name="Object"> The name of the database object. </param>
/// <param name="Type"> Type of the database object.
/// Should be one of [table, view, stored-procedure]. </param>
/// <param name="Parameters"> If Type is SourceType.StoredProcedure,
/// Parameters to be passed as defaults to the procedure call </param>
/// <param name="KeyFields"> The field(s) to be used as primary keys.
public record EntitySource(string Object, EntitySourceType? Type, List<ParameterMetadata>? Parameters, string[]? KeyFields);
