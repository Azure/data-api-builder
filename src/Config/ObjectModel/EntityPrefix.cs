// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    public record EntityPrefix
    {
        public string Path { get; }
        public string? ColumnName { get; }
        public string? Alias { get; }
        public string? EntityName { get; }

        public EntityPrefix(string Path, string? EntityName, string? ColumnName = null, string? Alias = null)
        {
            this.Path = Path;
            this.ColumnName = ColumnName;
            this.Alias = Alias;
            this.EntityName = EntityName;
        }
    }
}
