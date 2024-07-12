// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class EntityObject
    {
        public string Name { get; set; }

        public Dictionary<AttributeObject, int> Attributes { get; set; }
    }
}
