// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
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
                ConsoleFormatterShared.GetCurrentTimestamp(_formatterOptions));
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
            message = ConsoleFormatterShared.SanitizeControlCharacters(message)!;
            exception = ConsoleFormatterShared.SanitizeControlCharacters(exception);
            category = ConsoleFormatterShared.SanitizeControlCharacters(category)!;

            SimpleConsoleFormatterOptions formatterOptions = _formatterOptions;
            bool singleLine = formatterOptions.SingleLine;

            // The timestamp is rendered here (rather than through the formatter's TimestampFormat
            // option) so that the DAB supplied value is always UTC and always culture invariant.
            textWriter.Write(ConsoleFormatterShared.FormatTimestamp(stamp, formatterOptions));
            if (ConsoleFormatterShared.IsDabSuppliedTimestamp(formatterOptions))
            {
                // A deployment supplied format is written exactly as configured (the built-in
                // formatter expects any trailing separator to be part of that format), but the DAB
                // supplied timestamp needs a separator before the log level.
                textWriter.Write(' ');
            }

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
    /// Records whether the console logger provider was configured through the legacy
    /// <see cref="ConsoleLoggerOptions"/> members rather than through
    /// <see cref="ConsoleLoggerOptions.FormatterName"/>.
    /// </summary>
    /// <remarks>
    /// The console logger provider only copies the legacy members onto the selected formatter's
    /// options when <see cref="ConsoleLoggerOptions.FormatterName"/> is null. Selecting a DAB
    /// formatter sets that property, which would otherwise silently drop those settings, so the
    /// copy is performed here instead. A single flag is enough because the legacy members
    /// themselves are left untouched on <see cref="ConsoleLoggerOptions"/>.
    /// </remarks>
    internal sealed class LegacyConsoleLoggerOptionsMarker
    {
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Registration helpers for the DAB console formatters.
    /// </summary>
    public static class UtcTimestampConsoleFormatterExtensions
    {
        /// <summary>
        /// Registers the DAB console formatters and selects the one matching the requested console
        /// format, so that every console entry carries a culture invariant ISO 8601 UTC timestamp.
        /// This only registers formatters - the caller remains responsible for registering the console
        /// provider exactly once - so it can be applied to a pipeline which already has one (e.g. the
        /// provider added by <see cref="Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(string[])"/>)
        /// without emitting duplicate entries.
        /// </summary>
        /// <remarks>
        /// The record structure of the requested format is preserved, because structured log collectors
        /// and systemd severity extraction depend on it: "simple" maps to
        /// <see cref="UtcTimestampConsoleFormatter"/>, "json" to
        /// <see cref="UtcTimestampJsonConsoleFormatter"/> and "systemd" to
        /// <see cref="UtcTimestampSystemdConsoleFormatter"/>. A formatter name which is not one of the
        /// three built-ins identifies a formatter the deployment registered itself and is left alone.
        /// </remarks>
        public static ILoggingBuilder AddUtcTimestampConsoleFormatter(this ILoggingBuilder builder)
        {
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ConsoleFormatter, UtcTimestampConsoleFormatter>());
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ConsoleFormatter, UtcTimestampJsonConsoleFormatter>());
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ConsoleFormatter, UtcTimestampSystemdConsoleFormatter>());
            builder.Services.TryAddSingleton<LegacyConsoleLoggerOptionsMarker>();

            // PostConfigure runs after the "Logging:Console" configuration binding, so an explicitly
            // configured format - whether through FormatterName or through the legacy Format member -
            // is visible here.
            builder.Services.AddOptions<ConsoleLoggerOptions>()
                .PostConfigure<LegacyConsoleLoggerOptionsMarker>(SelectUtcTimestampFormatter);

            // Each built-in format binds a different options type, so the legacy members have to be
            // mapped onto all three. SimpleConsoleFormatterOptions and JsonConsoleFormatterOptions
            // derive from ConsoleFormatterOptions but are distinct options types, so configuring one
            // does not affect the others.
            builder.Services.AddOptions<SimpleConsoleFormatterOptions>()
                .PostConfigure<IOptionsMonitor<ConsoleLoggerOptions>, LegacyConsoleLoggerOptionsMarker>(ApplyLegacyConsoleLoggerOptions);
            builder.Services.AddOptions<JsonConsoleFormatterOptions>()
                .PostConfigure<IOptionsMonitor<ConsoleLoggerOptions>, LegacyConsoleLoggerOptionsMarker>(ApplyLegacyConsoleLoggerOptions);
            builder.Services.AddOptions<ConsoleFormatterOptions>()
                .PostConfigure<IOptionsMonitor<ConsoleLoggerOptions>, LegacyConsoleLoggerOptionsMarker>(ApplyLegacyConsoleLoggerOptions);

            return builder;
        }

        /// <summary>
        /// Replaces the requested built-in console format with the DAB formatter producing the same
        /// record structure.
        /// </summary>
        private static void SelectUtcTimestampFormatter(ConsoleLoggerOptions options, LegacyConsoleLoggerOptionsMarker legacy)
        {
            string requestedFormat;
            if (string.IsNullOrEmpty(options.FormatterName))
            {
#pragma warning disable CS0618 // Type or member is obsolete
                // A null FormatterName means the format is selected by the legacy Format member, and
                // that the remaining legacy members apply to the selected formatter.
                requestedFormat = options.Format == ConsoleLoggerFormat.Systemd
                    ? ConsoleFormatterNames.Systemd
                    : ConsoleFormatterNames.Simple;
#pragma warning restore CS0618
                legacy.IsActive = true;
            }
            else
            {
                requestedFormat = options.FormatterName;
            }

            if (string.Equals(requestedFormat, ConsoleFormatterNames.Simple, StringComparison.OrdinalIgnoreCase))
            {
                options.FormatterName = UtcTimestampConsoleFormatter.FORMATTER_NAME;
            }
            else if (string.Equals(requestedFormat, ConsoleFormatterNames.Json, StringComparison.OrdinalIgnoreCase))
            {
                options.FormatterName = UtcTimestampJsonConsoleFormatter.FORMATTER_NAME;
            }
            else if (string.Equals(requestedFormat, ConsoleFormatterNames.Systemd, StringComparison.OrdinalIgnoreCase))
            {
                options.FormatterName = UtcTimestampSystemdConsoleFormatter.FORMATTER_NAME;
            }
        }

        /// <summary>
        /// Copies the legacy <see cref="ConsoleLoggerOptions"/> members onto a formatter's options,
        /// reproducing what the console logger provider does when no formatter name is configured.
        /// </summary>
        private static void ApplyLegacyConsoleLoggerOptions(
            ConsoleFormatterOptions formatterOptions,
            IOptionsMonitor<ConsoleLoggerOptions> consoleLoggerOptionsMonitor,
            LegacyConsoleLoggerOptionsMarker legacy)
        {
            // Resolving the console logger options runs their configuration and post-configuration
            // chain - including SelectUtcTimestampFormatter - which is what populates the marker.
            ConsoleLoggerOptions consoleLoggerOptions = consoleLoggerOptionsMonitor.CurrentValue;
            if (!legacy.IsActive)
            {
                return;
            }

#pragma warning disable CS0618 // Type or member is obsolete
            formatterOptions.IncludeScopes = consoleLoggerOptions.IncludeScopes;
            formatterOptions.TimestampFormat = consoleLoggerOptions.TimestampFormat;
            formatterOptions.UseUtcTimestamp = consoleLoggerOptions.UseUtcTimestamp;

            if (formatterOptions is SimpleConsoleFormatterOptions simpleFormatterOptions)
            {
                simpleFormatterOptions.ColorBehavior = consoleLoggerOptions.DisableColors
                    ? LoggerColorBehavior.Disabled
                    : LoggerColorBehavior.Default;
            }
#pragma warning restore CS0618
        }
    }
}
