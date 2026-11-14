// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Console formatter which reproduces the record structure of the built-in "systemd" formatter -
    /// the syslog priority prefix journald uses for severity extraction, followed by the timestamp,
    /// category, event id, scopes, message and exception on a single line:
    /// <code>
    /// &lt;6&gt;2026-07-07T14:01:01.344Z Azure.DataApiBuilder.Service.Startup[0] Now listening on: http://localhost:5000
    /// </code>
    /// but renders the DAB supplied timestamp as an invariant UTC ISO 8601 value.
    /// See <see cref="ConsoleFormatterShared.FormatTimestamp"/> for why the built-in formatter cannot
    /// be configured to do this.
    /// </summary>
    public sealed class UtcTimestampSystemdConsoleFormatter : ConsoleFormatter, IDisposable
    {
        /// <summary>
        /// Value to assign to <see cref="ConsoleLoggerOptions.FormatterName"/> to select this formatter.
        /// </summary>
        public const string FORMATTER_NAME = "dab-utc-systemd";

        private readonly IDisposable? _optionsReloadToken;

        private ConsoleFormatterOptions _formatterOptions;

        public UtcTimestampSystemdConsoleFormatter(IOptionsMonitor<ConsoleFormatterOptions> options)
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
                    logEntry.Category,
                    bufferedRecord.EventId.Id,
                    bufferedRecord.Exception,
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
                logEntry.Category,
                logEntry.EventId.Id,
                logEntry.Exception?.ToString(),
                ConsoleFormatterShared.GetCurrentTimestamp(_formatterOptions));
        }

        private void WriteInternal(
            IExternalScopeProvider? scopeProvider,
            TextWriter textWriter,
            string message,
            LogLevel logLevel,
            string category,
            int eventId,
            string? exception,
            DateTimeOffset stamp)
        {
            string? logLevelString = GetSyslogSeverityString(logLevel);
            if (logLevelString is null)
            {
                return;
            }

            ConsoleFormatterOptions formatterOptions = _formatterOptions;

            // systemd reads messages line-by-line, so newlines are replaced before the remaining
            // control characters are escaped.
            message = ConsoleFormatterShared.SanitizeControlCharacters(ReplaceNewLines(message))!;
            exception = ConsoleFormatterShared.SanitizeControlCharacters(ReplaceNewLines(exception));
            category = ConsoleFormatterShared.SanitizeControlCharacters(category)!;

            textWriter.Write(logLevelString);
            textWriter.Write(ConsoleFormatterShared.FormatTimestamp(stamp, formatterOptions));
            if (ConsoleFormatterShared.IsDabSuppliedTimestamp(formatterOptions))
            {
                // A deployment supplied format is written exactly as configured (the built-in
                // formatter expects any trailing separator to be part of that format), but the DAB
                // supplied timestamp needs a separator before the category.
                textWriter.Write(' ');
            }

            textWriter.Write(category);
            textWriter.Write('[');
            textWriter.Write(eventId.ToString(CultureInfo.InvariantCulture));
            textWriter.Write(']');

            WriteScopeInformation(textWriter, scopeProvider, formatterOptions.IncludeScopes);

            if (!string.IsNullOrEmpty(message))
            {
                textWriter.Write(' ');
                textWriter.Write(message);
            }

            if (!string.IsNullOrEmpty(exception))
            {
                textWriter.Write(' ');
                textWriter.Write(exception);
            }

            textWriter.Write(Environment.NewLine);
        }

        private static string? ReplaceNewLines(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Replace(Environment.NewLine, " ").Replace("\n", " ").Replace("\r", " ");
        }

        private static void WriteScopeInformation(TextWriter textWriter, IExternalScopeProvider? scopeProvider, bool includeScopes)
        {
            if (!includeScopes || scopeProvider is null)
            {
                return;
            }

            scopeProvider.ForEachScope((scope, state) =>
            {
                state.Write(" => ");
                state.Write(ConsoleFormatterShared.SanitizeControlCharacters(scope?.ToString()));
            }, textWriter);
        }

        /// <summary>
        /// Maps a log level onto the syslog severity from RFC 5424 that journald reads.
        /// </summary>
        private static string? GetSyslogSeverityString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "<7>",
                LogLevel.Debug => "<7>",
                LogLevel.Information => "<6>",
                LogLevel.Warning => "<4>",
                LogLevel.Error => "<3>",
                LogLevel.Critical => "<2>",
                _ => null,
            };
        }
    }
}
