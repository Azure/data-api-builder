// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    public record EntityPrefix
    {
        public string Path { get; }
        public string? EntityName { get; }
        public string? Alias { get; }
        public bool? IsFilterAvailable { get; }

        public EntityPrefix(string Path, string? EntityName = null, string? Alias = null, bool? IsFilterAvailable = null)
        {
            this.Path = Path;
            this.EntityName = EntityName;
            this.Alias = Alias;
            this.IsFilterAvailable = IsFilterAvailable;
        }
    }
}
