// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Core.Generator
{
    /// <summary>
    /// Represents information related to each attribute of an entity.
    /// This class encapsulates the details of an attribute such as its name, type, whether it is an array,
    /// and its presence in a collection of records or an array.
    /// </summary>
    internal class AttributeObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AttributeObject"/> class.
        /// </summary>
        /// <param name="name">The name of the attribute (e.g., "id", "name").</param>
        /// <param name="type">The data type of the attribute (e.g., "string", "int").</param>
        /// <param name="isArray">Indicates whether the attribute is an array.</param>
        /// <param name="value">The JSON value kind, used to track the attribute's presence.</param>
        /// <param name="arrayLength">The length of the parent array, if the attribute is within an array.</param>
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
        /// Gets the name of the attribute (e.g., "id", "name").
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the data type of the attribute (e.g., "string", "int").
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets a value indicating whether the attribute is an array.
        /// </summary>
        public bool IsArray { get; }

        /// <summary>
        /// Gets or sets the total number of objects present in the parent array.
        /// </summary>
        public int ParentArrayLength { get; set; }

        /// <summary>
        /// Gets or sets the count of occurrences of the attribute in the records.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Returns a formatted string representation of the attribute, indicating its name, type, array status, and nullability.
        /// </summary>
        /// <param name="totalCount">The total number of records considered for determining the attribute's presence and nullability.</param>
        /// <returns>
        /// A formatted string that includes:
        /// - The attribute's name and type.
        /// - An indication if the attribute is an array.
        /// - An exclamation mark (!) if the attribute is not nullable.
        /// 
        /// The format is:
        /// - "[Name] : [Type]!" : if the attribute is not nullable and is an array
        /// - "[Name] : [Type]" : if the attribute is nullable and is an array
        /// - "[Name] : Type!" : if the attribute is not nullable
        /// - "[Name] : Type" : if the attribute is nullable
        /// 
        /// Example outputs:
        /// - "id : string!" for a non-nullable string attribute
        /// - "tags : [string]" for a nullable array of strings attribute
        /// </returns>
        /// <remarks>
        /// The method first determines if the attribute is nullable based on its presence in the records.
        /// An attribute is considered nullable if it is not present in all records (`Count < totalCount`) 
        /// or if it is an array element and not present in all elements of the parent array (`Count < ParentArrayLength`).
        /// If the attribute is an array, the type is enclosed in square brackets.
        /// An exclamation mark (!) is appended if the attribute is determined to be non-nullable.
        /// </remarks>
        public string? GetString(int totalCount)
        {
            bool isNullable = false;
            string t = $"{Type}";

            // Check if the attribute is nullable
            if (totalCount > 1 && // If there is more than one record, consider potential nullability
                (Count < totalCount || // Attribute is nullable if not present in all records
                    Count < ParentArrayLength)) // Attribute is nullable if not present in all records of an array
            {
                isNullable = true;
            }

            if (IsArray)
            {
                t = $"[{t}]"; // Mark as array if applicable
            }

            if (!isNullable)
            {
                t += "!"; // Mark as non-nullable if applicable
            }

            return $"{Name}: {t}";
        }
    }
}
