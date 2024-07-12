// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class AttributeObject
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public override string ToString()
        {
            return $"{Name} : {Type}";
        }   
    }
}
