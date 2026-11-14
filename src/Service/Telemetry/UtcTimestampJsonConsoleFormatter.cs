// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Console formatter which reproduces the record structure of the built-in "json" formatter -
    /// one JSON object per entry carrying Timestamp, EventId, LogLevel, Category, Message, Exception,
    /// State and Scopes - but renders the DAB supplied timestamp as an invariant UTC ISO 8601 value.
    /// See <see cref="ConsoleFormatterShared.FormatTimestamp"/> for why the built-in formatter cannot
    /// be configured to do this.
    /// </summary>
    public sealed class UtcTimestampJsonConsoleFormatter : ConsoleFormatter, IDisposable
    {
        /// <summary>
        /// Value to assign to <see cref="ConsoleLoggerOptions.FormatterName"/> to select this formatter.
        /// </summary>
        public const string FORMATTER_NAME = "dab-utc-json";

        private readonly IDisposable? _optionsReloadToken;

        private JsonConsoleFormatterOptions _formatterOptions;

        public UtcTimestampJsonConsoleFormatter(IOptionsMonitor<JsonConsoleFormatterOptions> options)
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
            // event's timestamp, message, attributes and exception must be read off the record
            // itself rather than recomputed at flush time.
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
                    bufferedRecord.Attributes.Count > 0,
                    stateMessage: null,
                    bufferedRecord.Attributes,
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
                logEntry.State is not null,
                logEntry.State?.ToString(),
                logEntry.State as IReadOnlyList<KeyValuePair<string, object?>>,
                ConsoleFormatterShared.GetCurrentTimestamp(_formatterOptions));
        }

        private void WriteInternal(
            IExternalScopeProvider? scopeProvider,
            TextWriter textWriter,
            string? message,
            LogLevel logLevel,
            string category,
            int eventId,
            string? exception,
            bool hasState,
            string? stateMessage,
            IReadOnlyList<KeyValuePair<string, object?>>? stateProperties,
            DateTimeOffset stamp)
        {
            string? logLevelString = GetLogLevelString(logLevel);
            if (logLevelString is null)
            {
                return;
            }

            JsonConsoleFormatterOptions formatterOptions = _formatterOptions;

            ArrayBufferWriter<byte> output = new(initialCapacity: 1024);
            using (Utf8JsonWriter writer = new(output, formatterOptions.JsonWriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("Timestamp", ConsoleFormatterShared.FormatTimestamp(stamp, formatterOptions));
                writer.WriteNumber("EventId", eventId);
                writer.WriteString("LogLevel", logLevelString);
                writer.WriteString("Category", category);
                writer.WriteString("Message", message);

                if (exception is not null)
                {
                    writer.WriteString(nameof(Exception), exception);
                }

                if (hasState)
                {
                    writer.WriteStartObject("State");

                    // The message and the state message are usually identical, so the state message
                    // is only written when it differs - this keeps the record smaller.
                    if (!string.Equals(message, stateMessage, StringComparison.Ordinal))
                    {
                        writer.WriteString("Message", stateMessage);
                    }

                    if (stateProperties is not null)
                    {
                        foreach (KeyValuePair<string, object?> item in stateProperties)
                        {
                            WriteItem(writer, item);
                        }
                    }

                    writer.WriteEndObject();
                }

                WriteScopeInformation(writer, scopeProvider, formatterOptions.IncludeScopes);
                writer.WriteEndObject();
                writer.Flush();
            }

            // JSON string escaping already neutralizes the control characters which would otherwise
            // drive terminal escape sequences, so no additional sanitization is needed here.
            textWriter.Write(Encoding.UTF8.GetString(output.WrittenSpan));
            textWriter.Write(Environment.NewLine);
        }

        private static void WriteScopeInformation(Utf8JsonWriter writer, IExternalScopeProvider? scopeProvider, bool includeScopes)
        {
            if (!includeScopes || scopeProvider is null)
            {
                return;
            }

            writer.WriteStartArray("Scopes");
            scopeProvider.ForEachScope((scope, state) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> scopeItems)
                {
                    state.WriteStartObject();
                    state.WriteString("Message", scope.ToString());
                    foreach (KeyValuePair<string, object?> item in scopeItems)
                    {
                        WriteItem(state, item);
                    }

                    state.WriteEndObject();
                }
                else
                {
                    state.WriteStringValue(ToInvariantString(scope));
                }
            }, writer);
            writer.WriteEndArray();
        }

        private static void WriteItem(Utf8JsonWriter writer, KeyValuePair<string, object?> item)
        {
            string key = item.Key;
            switch (item.Value)
            {
                case bool boolValue:
                    writer.WriteBoolean(key, boolValue);
                    break;
                case byte byteValue:
                    writer.WriteNumber(key, byteValue);
                    break;
                case sbyte sbyteValue:
                    writer.WriteNumber(key, sbyteValue);
                    break;
                case char charValue:
                    writer.WriteString(key, charValue.ToString());
                    break;
                case decimal decimalValue:
                    writer.WriteNumber(key, decimalValue);
                    break;
                case double doubleValue:
                    writer.WriteNumber(key, doubleValue);
                    break;
                case float floatValue:
                    writer.WriteNumber(key, floatValue);
                    break;
                case int intValue:
                    writer.WriteNumber(key, intValue);
                    break;
                case uint uintValue:
                    writer.WriteNumber(key, uintValue);
                    break;
                case long longValue:
                    writer.WriteNumber(key, longValue);
                    break;
                case ulong ulongValue:
                    writer.WriteNumber(key, ulongValue);
                    break;
                case short shortValue:
                    writer.WriteNumber(key, shortValue);
                    break;
                case ushort ushortValue:
                    writer.WriteNumber(key, ushortValue);
                    break;
                case null:
                    writer.WriteNull(key);
                    break;
                default:
                    writer.WriteString(key, ToInvariantString(item.Value));
                    break;
            }
        }

        private static string? ToInvariantString(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture);

        private static string? GetLogLevelString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "Trace",
                LogLevel.Debug => "Debug",
                LogLevel.Information => "Information",
                LogLevel.Warning => "Warning",
                LogLevel.Error => "Error",
                LogLevel.Critical => "Critical",
                _ => null,
            };
        }
    }
}
