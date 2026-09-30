// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Captures metadata about a child config loaded via data-source-files.
/// Used during validation to check each child independently with filename context.
/// </summary>
/// <param name="FileName">The file path of the child config.</param>
/// <param name="EntityNames">Names of manually defined entities in the child.</param>
/// <param name="AutoentityDefinitionNames">Names of autoentity definitions in the child.</param>
/// <param name="HasDataSource">Whether the child config defines a data source.</param>
public record ChildConfigMetadata(
    string FileName,
    IReadOnlySet<string> EntityNames,
    IReadOnlySet<string> AutoentityDefinitionNames,
    bool HasDataSource);
