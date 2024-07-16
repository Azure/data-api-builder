// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class AttributeObject
    {
        public AttributeObject(string name, string type, string parent, bool isArray, object? value = null, int arrayLength = 0)
        {
            this.Name = name;
            this.Type = type;
            this.Parent = parent;
            this.IsArray = isArray;
            this.ParentArrayLength = arrayLength;

            if (value is not null)
            {
                this.Count++;
            }
        }

        public string Name { get; set; }

        public string Type { get; set; }

        public string Parent { get; set;}

        public bool IsArray { get; set; }

        public int ParentArrayLength { get; set; }

        public int Count { get; set; }

        public override string? ToString()
        {
            return $"{Name} : {Type} : {Parent} : {IsArray} : {ParentArrayLength}";
        }

        public string? GetString(int totalCount)
        {
            string t = Type;
            if (totalCount > 1 &&
                (Count < totalCount ||
                    Count < ParentArrayLength))
            {
                t = $"{Type}!";
            }

            if (IsArray)
            {
                return $"{Name} : [{t}]";
            }

            return $"{Name} : {t}";
        }
    }
}
