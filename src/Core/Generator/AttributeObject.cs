// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class AttributeObject
    {
        public AttributeObject(string name,
            string type,
            string parent,
            bool isArray,
            JsonValueKind? value = null,
            int arrayLength = 0)
        {
            this.Name = name;
            this.Type = type;
            this.Parent = parent;
            this.IsArray = isArray;
            this.ParentArrayLength = arrayLength;

            if (value is not null && value is not JsonValueKind.Null)
            {
                this.Count++;
            }
        }

        public string Name { get; }

        public string Type { get; }

        public string Parent { get; }

        public bool IsArray { get; }

        public int ParentArrayLength { get; set; }

        public int Count { get; set; }

        /// <summary>
        /// Returns following string format:
        /// [Name] : [Type]! : if the attribute is not nullable and is an array
        /// [Name] : [Type] : if the attribute is nullable and is an array
        /// [Name] : Type! : if the attribute is not nullable
        /// [Name] : Type : if the attribute is nullable
        /// </summary>
        /// <param name="totalCount"></param>
        /// <returns></returns>
        public string? GetString(int totalCount)
        {
            bool isNullable = false;
            string t = $"{Type}";

            if (totalCount > 1 &&
                (Count < totalCount ||
                    Count < ParentArrayLength))
            {
                isNullable = true;
            }

            if (IsArray)
            {
                t = $"[{t}]";
            }

            if (!isNullable)
            {
                t += "!";
            }

            return $"{Name} : {t}";
        }
    }
}
