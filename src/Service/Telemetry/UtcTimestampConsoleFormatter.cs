// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using Azure.DataApiBuilder.Product;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Console formatter which reproduces the layout of the built-in "simple" console formatter
    /// but prefixes every entry with an ISO 8601 UTC timestamp rendered with
    /// <see cref="CultureInfo.InvariantCulture"/>:
    /// <code>
    /// 2026-07-07T14:01:01.344Z info: Microsoft.AspNetCore.Hosting.Diagnostics[1]
    ///       Request starting HTTP/1.1 GET http://localhost:5000/graphql - - -
    /// </code>
    /// The built-in formatter cannot be used for this because it renders the timestamp with
    /// <c>DateTimeOffset.ToString(TimestampFormat)</c>, which resolves against
    /// <see cref="CultureInfo.CurrentCulture"/>. Its <c>UseUtcTimestamp</c> option only selects the
    /// time zone, not the calendar or the digits, so on a machine using a non-Gregorian culture
    /// (ar-SA, th-TH, fa-IR, ...) the built-in formatter emits e.g. <c>2569-08-29T05:29:44.113Z</c>
    /// instead of the required Gregorian <c>2026-08-29T05:29:44.113Z</c>.
    /// </summary>
    public sealed class UtcTimestampConsoleFormatter : ConsoleFormatter, IDisposable
    {
        /// <summary>
        /// Value to assign to <see cref="ConsoleLoggerOptions.FormatterName"/> to select this formatter.
        /// </summary>
        public const string FORMATTER_NAME = "dab-utc-simple";

        /// <summary>
        /// Separator written between the abbreviated log level and the category.
        /// </summary>
        private const string LOG_LEVEL_PADDING = ": ";

        /// <summary>
        /// Indentation of the message lines, aligning them past "info: ".
        /// </summary>
        private static readonly string _messagePadding = new(' ', 4 + LOG_LEVEL_PADDING.Length);

        private static readonly string _newLineWithMessagePadding = Environment.NewLine + _messagePadding;

        private readonly IDisposable? _optionsReloadToken;

        private SimpleConsoleFormatterOptions _formatterOptions;

        public UtcTimestampConsoleFormatter(IOptionsMonitor<SimpleConsoleFormatterOptions> options)
            : base(FORMATTER_NAME)
        {
            _formatterOptions = options.CurrentValue;
            _optionsReloadToken = options.OnChange(updatedOptions => _formatterOptions = updatedOptions);
        }

        public void Dispose() => _optionsReloadToken?.Dispose();

        /// <inheritdoc/>
        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
        {
            // Buffered entries are replayed later (ConsoleLogger.LogRecords passes a
            // LogEntry<BufferedLogRecord> whose Formatter and Exception are null), so the original
            // event's timestamp, message and exception must be read off the record itself rather
            // than recomputed at flush time.
            if (logEntry.State is BufferedLogRecord bufferedRecord)
            {
                WriteInternal(
                    scopeProvider: null,
                    textWriter,
                    bufferedRecord.FormattedMessage ?? string.Empty,
                    bufferedRecord.LogLevel,
                    bufferedRecord.EventId.Id,
                    bufferedRecord.Exception,
                    logEntry.Category,
                    bufferedRecord.Timestamp);
                return;
            }

            string? message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
            if (message is null && logEntry.Exception is null)
            {
                return;
            }

            WriteInternal(
                scopeProvider,
                textWriter,
                message ?? string.Empty,
                logEntry.LogLevel,
                logEntry.EventId.Id,
                logEntry.Exception?.ToString(),
                logEntry.Category,
                DateTimeOffset.UtcNow);
        }

        private void WriteInternal(
            IExternalScopeProvider? scopeProvider,
            TextWriter textWriter,
            string message,
            LogLevel logLevel,
            int eventId,
            string? exception,
            string category,
            DateTimeOffset stamp)
        {
            string? logLevelString = BootstrapLogger.GetAbbreviatedLogLevel(logLevel);
            if (logLevelString is null)
            {
                return;
            }

            // Untrusted values can reach the console through log messages, so neutralize the
            // control characters which would otherwise drive terminal escape sequences.
            message = SanitizeControlCharacters(message)!;
            exception = SanitizeControlCharacters(exception);
            category = SanitizeControlCharacters(category)!;

            SimpleConsoleFormatterOptions formatterOptions = _formatterOptions;
            bool singleLine = formatterOptions.SingleLine;

            // The timestamp is rendered here (rather than through the formatter's TimestampFormat
            // option) so that it is always UTC and always culture invariant.
            textWriter.Write(stamp.UtcDateTime.ToString(BootstrapLogger.UTC_TIMESTAMP_FORMAT, CultureInfo.InvariantCulture));
            textWriter.Write(' ');

            if (EmitAnsiColorCodes(formatterOptions.ColorBehavior))
            {
                WriteColoredLogLevel(textWriter, logLevel, logLevelString);
            }
            else
            {
                textWriter.Write(logLevelString);
            }

            // Category and event id, e.g. ": Microsoft.AspNetCore.Hosting.Diagnostics[1]".
            textWriter.Write(LOG_LEVEL_PADDING);
            textWriter.Write(category);
            textWriter.Write('[');
            textWriter.Write(eventId.ToString(CultureInfo.InvariantCulture));
            textWriter.Write(']');

            if (!singleLine)
            {
                textWriter.Write(Environment.NewLine);
            }

            WriteScopeInformation(textWriter, scopeProvider, formatterOptions.IncludeScopes, singleLine);
            WriteMessage(textWriter, message, singleLine);

            if (exception is not null)
            {
                WriteMessage(textWriter, exception, singleLine);
            }

            if (singleLine)
            {
                textWriter.Write(Environment.NewLine);
            }
        }

        /// <summary>
        /// Escapes the control characters which can drive terminal escape sequences when written to
        /// a console - the C0 range (U+0000-U+001F), DEL (U+007F) and the C1 range (U+0080-U+009F) -
        /// as \uXXXX. Tab, carriage return and line feed are preserved for log formatting.
        /// Mirrors the sanitization the built-in console formatter applies.
        /// </summary>
        private static string? SanitizeControlCharacters(string? value)
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

        private static void WriteMessage(TextWriter textWriter, string? message, bool singleLine)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (singleLine)
            {
                textWriter.Write(' ');
                textWriter.Write(message.Replace(Environment.NewLine, " "));
            }
            else
            {
                textWriter.Write(_messagePadding);
                textWriter.Write(message.Replace(Environment.NewLine, _newLineWithMessagePadding));
                textWriter.Write(Environment.NewLine);
            }
        }

        private static void WriteScopeInformation(TextWriter textWriter, IExternalScopeProvider? scopeProvider, bool includeScopes, bool singleLine)
        {
            if (!includeScopes || scopeProvider is null)
            {
                return;
            }

            bool firstScope = true;
            scopeProvider.ForEachScope((scope, state) =>
            {
                if (firstScope)
                {
                    state.Write(singleLine ? " => " : _messagePadding + "=> ");
                    firstScope = false;
                }
                else
                {
                    state.Write(" => ");
                }

                state.Write(scope);
            }, textWriter);

            if (!firstScope && !singleLine)
            {
                textWriter.Write(Environment.NewLine);
            }
        }

        /// <summary>
        /// Mirrors the built-in console formatter's decision on whether ANSI color codes may be
        /// emitted, honoring the NO_COLOR convention and output redirection.
        /// </summary>
        private static bool EmitAnsiColorCodes(LoggerColorBehavior colorBehavior)
        {
            if (colorBehavior == LoggerColorBehavior.Disabled)
            {
                return false;
            }

            if (colorBehavior == LoggerColorBehavior.Enabled)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
            {
                return false;
            }

            return !Console.IsOutputRedirected;
        }

        /// <summary>
        /// Writes the abbreviated log level using the same colors as the built-in console formatter.
        /// </summary>
        private static void WriteColoredLogLevel(TextWriter textWriter, LogLevel logLevel, string logLevelString)
        {
            const string RESET_FOREGROUND = "\u001b[39m\u001b[22m";
            const string RESET_BACKGROUND = "\u001b[49m";

            (string Foreground, string Background) colors = logLevel switch
            {
                // White on dark red.
                LogLevel.Critical => ("\u001b[1m\u001b[37m", "\u001b[41m"),
                // Black on dark red.
                LogLevel.Error => ("\u001b[30m", "\u001b[41m"),
                // Yellow on black.
                LogLevel.Warning => ("\u001b[1m\u001b[33m", "\u001b[40m"),
                // Dark green on black.
                LogLevel.Information => ("\u001b[32m", "\u001b[40m"),
                // Gray on black.
                _ => ("\u001b[37m", "\u001b[40m")
            };

            textWriter.Write(colors.Background);
            textWriter.Write(colors.Foreground);
            textWriter.Write(logLevelString);
            textWriter.Write(RESET_FOREGROUND);
            textWriter.Write(RESET_BACKGROUND);
        }
    }

    /// <summary>
    /// Registration helpers for <see cref="UtcTimestampConsoleFormatter"/>.
    /// </summary>
    public static class UtcTimestampConsoleFormatterExtensions
    {
        /// <summary>
        /// Registers <see cref="UtcTimestampConsoleFormatter"/> and selects it on the console logger
        /// provider so every console entry is prefixed with a culture invariant ISO 8601 UTC timestamp.
        /// This only registers a formatter - the caller remains responsible for registering the console
        /// provider exactly once - so it can be applied to a pipeline which already has one (e.g. the
        /// provider added by <see cref="Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(string[])"/>)
        /// without emitting duplicate entries.
        /// </summary>
        public static ILoggingBuilder AddUtcTimestampConsoleFormatter(this ILoggingBuilder builder)
        {
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ConsoleFormatter, UtcTimestampConsoleFormatter>());
            builder.Services.Configure<ConsoleLoggerOptions>(options =>
            {
                options.FormatterName = UtcTimestampConsoleFormatter.FORMATTER_NAME;
            });

            return builder;
        }
    }
}
