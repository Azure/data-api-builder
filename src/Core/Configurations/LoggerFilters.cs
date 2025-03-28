// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Configurations
{
    public static class LoggerFilters
    {
        public static List<string> validFilters = new();

        public static void AddFilter(string? loggerFilter)
        {
            if (loggerFilter != null)
            {
                validFilters.Add(loggerFilter);
            }
        }
    }
}
