// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.DatabasePrimitives;

/// <summary>
/// Represents a DAX measure discovered from a semantic model via TMSCHEMA_MEASURES.
/// Measures are virtual, context-aware fields that can be evaluated on any entity —
/// the same measure produces different results depending on which entity's row context it runs in.
/// </summary>
/// <param name="Name">The measure name as defined in the model.</param>
/// <param name="Expression">The DAX expression (e.g., "SUM(sales[Amount])").</param>
/// <param name="SystemType">The .NET CLR type for the measure's return value.</param>
/// <param name="HomeTable">The table on which the measure is defined (metadata only, not a constraint).</param>
/// <param name="IsHidden">Whether the measure is marked as hidden in the model.</param>
public record MeasureDefinition(
    string Name,
    string Expression,
    Type SystemType,
    string HomeTable,
    bool IsHidden);
