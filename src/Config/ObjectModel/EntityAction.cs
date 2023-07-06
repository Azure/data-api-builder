// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record EntityAction(EntityActionOperation Action, EntityActionFields? Fields, EntityActionPolicy? Policy)
{
    public static readonly HashSet<EntityActionOperation> ValidPermissionOperations = new() { EntityActionOperation.Create, EntityActionOperation.Read, EntityActionOperation.Update, EntityActionOperation.Delete };
    public static readonly HashSet<EntityActionOperation> ValidStoredProcedurePermissionOperations = new() { EntityActionOperation.Execute };
}
