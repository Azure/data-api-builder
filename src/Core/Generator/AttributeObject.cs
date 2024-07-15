// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class AttributeObject
    {
        public AttributeObject(string name, string type, string parent, bool isArray)
        {
            Name = name;
            Type = type;
            Parent = parent;
            IsArray = isArray;
        }

        public string Name { get; set; }

        public string Type { get; set; }

        public string Parent { get; set;}

        public bool IsArray { get; set; }

        public bool IsModel { get; set; }  

        public List<object> Values { get; set; } = new();

        public override string? ToString()
        {
            return $"{Name} : {Type} : {Parent} : {IsArray}";
        }

        public string? GetString(int totalCount)
        {
            string t = Type;
            if (totalCount > 1 && Values.Count < totalCount)
            {
                t = $"{Type}!";
            }

            if (IsArray)
            {
                return $"{Name} : [{t}]";
            }

            return $"{Name} : {t}";
        }

        public string? Print()
        {
            return $"{Name} : {Type} : {Parent} : {IsArray} : {Values.Count} ";
        }
    }
}
