// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Text;
using Azure.DataApiBuilder.Product;
using Microsoft.Extensions.Logging.Console;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Helpers shared by the DAB console formatters so that the timestamp contract and the
    /// console hardening are implemented exactly once, regardless of the selected record format.
    /// </summary>
    internal static class ConsoleFormatterShared
    {
        /// <summary>
        /// Renders the timestamp DAB prefixes onto a console entry.
        /// </summary>
        /// <remarks>
        /// When the deployment did not configure a timestamp of its own, DAB supplies the value and
        /// it is rendered as an ISO 8601 UTC timestamp with millisecond precision using
        /// <see cref="CultureInfo.InvariantCulture"/>. The built-in formatters instead render
        /// <see cref="ConsoleFormatterOptions.TimestampFormat"/> through
        /// <c>DateTimeOffset.ToString(format)</c>, which resolves against
        /// <see cref="CultureInfo.CurrentCulture"/> - that yields a non-Gregorian year under cultures
        /// such as th-TH (2569) or ar-SA (1448), and a culture specific time separator under cultures
        /// such as fi-FI (08.04.02 rather than 08:04:02).
        /// An explicitly configured <see cref="ConsoleFormatterOptions.TimestampFormat"/> is an
        /// intentional override, so it keeps the built-in semantics (current culture, and the time
        /// zone selected by <see cref="ConsoleFormatterOptions.UseUtcTimestamp"/>).
        /// </remarks>
        public static string FormatTimestamp(DateTimeOffset stamp, ConsoleFormatterOptions options)
        {
            string? configuredFormat = options.TimestampFormat;
            if (string.IsNullOrEmpty(configuredFormat))
            {
                return stamp.UtcDateTime.ToString(BootstrapLogger.UTC_TIMESTAMP_FORMAT, CultureInfo.InvariantCulture);
            }

            return stamp.ToString(configuredFormat);
        }

        /// <summary>
        /// Whether DAB - rather than the deployment - supplies the timestamp for an entry.
        /// </summary>
        public static bool IsDabSuppliedTimestamp(ConsoleFormatterOptions options)
            => string.IsNullOrEmpty(options.TimestampFormat);

        /// <summary>
        /// Returns the instant to stamp a live (non-buffered) entry with. DAB always supplies a UTC
        /// value; only an explicitly configured timestamp format may opt into local time.
        /// </summary>
        public static DateTimeOffset GetCurrentTimestamp(ConsoleFormatterOptions options)
        {
            bool useLocalTime = !string.IsNullOrEmpty(options.TimestampFormat) && !options.UseUtcTimestamp;
            return useLocalTime ? DateTimeOffset.Now : DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Escapes the control characters which can drive terminal escape sequences when written to
        /// a console - the C0 range (U+0000-U+001F), DEL (U+007F) and the C1 range (U+0080-U+009F) -
        /// as \uXXXX. Tab, carriage return and line feed are preserved for log formatting.
        /// Log entries carry untrusted values (request headers, entity names, configuration paths),
        /// so they must not be able to emit raw escape sequences to the operator's terminal.
        /// </summary>
        public static string? SanitizeControlCharacters(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            int firstIndex = -1;
            for (int i = 0; i < value.Length; i++)
            {
                if (ShouldEscape(value[i]))
                {
                    firstIndex = i;
                    break;
                }
            }

            if (firstIndex < 0)
            {
                return value;
            }

            StringBuilder sanitized = new(value.Length + 8);
            sanitized.Append(value, 0, firstIndex);
            for (int i = firstIndex; i < value.Length; i++)
            {
                char c = value[i];
                if (ShouldEscape(c))
                {
                    sanitized.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sanitized.Append(c);
                }
            }

            return sanitized.ToString();

            static bool ShouldEscape(char c)
                => c is not '\t' and not '\n' and not '\r'
                    && (c <= '\u001F' || (c >= '\u007F' && c <= '\u009F'));
        }
    }
}
