// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Core.Generator
{
    /// <summary>
    /// It holds the information related to each attribute of an entity
    /// </summary>
    internal class AttributeObject
    {
        public AttributeObject(string name,
            string type,
            bool isArray,
            JsonValueKind? value = null,
            int arrayLength = 0)
        {
            this.Name = name;
            this.Type = type;
            this.IsArray = isArray;
            this.ParentArrayLength = arrayLength;

            if (value is not null && value is not JsonValueKind.Null)
            {
                this.Count++;
            }
        }

        /// <summary>
        /// Attribute name e.g id, name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Attribute Type e.g string, int
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Indicates if the attribute is an array
        /// </summary>
        public bool IsArray { get; }

        /// <summary>
        /// It contains the total number of objects present in the parent array
        /// </summary>
        public int ParentArrayLength { get; set; }

        /// <summary>
        /// Count of the attribute present in the records
        /// </summary>
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

            // Check if the attribute is nullable
            if (totalCount > 1 && // If there are more than one record, if sampler returns only one record, then consider it as non-nullable
                (Count < totalCount || // if attribute is not present in all the records, it means it is nullable
                    Count < ParentArrayLength)) // if attribute is not present in all the records of an array, it means it is nullable
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
