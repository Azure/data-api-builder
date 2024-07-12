// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;

namespace Azure.DataApiBuilder.Core.Extensions
{
    internal static class StringExtension
    {
        // Extension method to change the case of a string to PascalCase
        public static string ToPascalCase(this string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str).Replace("_", string.Empty).Replace("-", string.Empty);
        }
    }
}
