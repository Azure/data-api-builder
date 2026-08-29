// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.Utilities;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Verifies that every console log entry produced by the engine begins with an
    /// ISO 8601 UTC timestamp with millisecond precision. Covers the two logging
    /// factories built by <see cref="Program"/> (the startup logger factory and the
    /// web host's logging pipeline) as well as the direct diagnostic call sites that
    /// were migrated from <c>Console.WriteLine</c> to a logger.
    /// </summary>
    [TestClass]
    public class ConsoleLogTimestampTests
    {
        private const string LOG_MESSAGE = "timestamp probe message";

        /// <summary>
        /// Matches the timestamp prefix: exactly three fractional-second digits followed
        /// by a literal 'Z'. The trailing 'Z' immediately after the third digit is what
        /// rules out additional (e.g. microsecond) precision.
        /// </summary>
        private static readonly Regex _timestampPrefix =
            new(@"^(?<ts>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z) ", RegexOptions.Compiled);

        /// <summary>
        /// <see cref="Program.LogLevelProvider"/> is process-wide mutable state read by the
        /// logging configuration under test. Replace it per test and restore afterwards so
        /// the rest of the suite keeps observing the default instance.
        /// </summary>
        private DynamicLogLevelProvider? _originalLogLevelProvider;

        [TestInitialize]
        public void SetLogLevelProvider()
        {
            _originalLogLevelProvider = Program.LogLevelProvider;
            DynamicLogLevelProvider provider = new();
            provider.SetInitialLogLevel(LogLevel.Information);
            Program.LogLevelProvider = provider;
        }

        [TestCleanup]
        public void RestoreLogLevelProvider()
        {
            if (_originalLogLevelProvider is not null)
            {
                Program.LogLevelProvider = _originalLogLevelProvider;
            }

            BootstrapLogger.WriteAllOutputToStandardError = false;
        }

        /// <summary>
        /// Asserts that <paramref name="output"/> begins with a timestamp that:
        /// parses as UTC, ends in 'Z', carries exactly three fractional-second digits,
        /// and falls within the window captured around the logging call.
        /// </summary>
        private static void AssertStartsWithUtcTimestamp(string output, DateTime before, DateTime after)
        {
            Match match = _timestampPrefix.Match(output);
            Assert.IsTrue(match.Success,
                $"Expected output to start with an ISO 8601 UTC timestamp (yyyy-MM-ddTHH:mm:ss.fffZ) but got: '{output}'");

            string timestamp = match.Groups["ts"].Value;
            Assert.IsTrue(timestamp.EndsWith("Z", StringComparison.Ordinal),
                $"Timestamp '{timestamp}' must end with 'Z' to denote UTC.");
            Assert.AreEqual(3, timestamp.Split('.')[1].TrimEnd('Z').Length,
                $"Timestamp '{timestamp}' must carry exactly three fractional-second digits.");

            Assert.IsTrue(
                DateTime.TryParseExact(
                    timestamp,
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed),
                $"Timestamp '{timestamp}' could not be parsed as an invariant-culture UTC value.");
            Assert.AreEqual(DateTimeKind.Utc, parsed.Kind, "Parsed timestamp must be UTC.");

            // The emitted value is truncated to milliseconds, so compare against a
            // millisecond-truncated lower bound.
            DateTime lowerBound = before.AddTicks(-(before.Ticks % TimeSpan.TicksPerMillisecond));
            Assert.IsTrue(parsed >= lowerBound && parsed <= after,
                $"Timestamp '{timestamp}' is outside the window [{lowerBound:O}, {after:O}] captured around the log call.");
        }

        /// <summary>
        /// Asserts every log entry is timestamped. Continuation lines (the console
        /// formatter writes the message indented beneath its header line) are skipped
        /// since the timestamp belongs to the entry, not to each physical line.
        /// </summary>
        private static void AssertEveryEntryTimestamped(string output, DateTime before, DateTime after)
        {
            string[] entries = output
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => !string.IsNullOrWhiteSpace(line) && !char.IsWhiteSpace(line[0]))
                .ToArray();

            Assert.IsTrue(entries.Length > 0, "Expected at least one log entry.");
            foreach (string entry in entries)
            {
                AssertStartsWithUtcTimestamp(entry, before, after);
            }
        }

        /// <summary>
        /// Redirects Console.Out/Console.Error around <paramref name="action"/>. The console
        /// logger provider captures the current writers when it is constructed, so the
        /// factory must be created inside the action.
        /// </summary>
        private static (string Stdout, string Stderr, DateTime Before, DateTime After) CaptureConsole(Action action)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            StringWriter stdout = new();
            StringWriter stderr = new();
            DateTime before;
            DateTime after;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                before = DateTime.UtcNow;
                action();
                after = DateTime.UtcNow;
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            return (stdout.ToString(), stderr.ToString(), before, after);
        }

        /// <summary>
        /// The startup logger factory (non-stdio) writes timestamped entries to stdout.
        /// </summary>
        [TestMethod]
        public void GetLoggerFactoryForLogLevel_NormalMode_EmitsTimestampedEntry()
        {
            (string stdout, string stderr, DateTime before, DateTime after) = CaptureConsole(() =>
            {
                using ILoggerFactory factory = Program.GetLoggerFactoryForLogLevel(LogLevel.Information);
                factory.CreateLogger("TestCategory").LogInformation(LOG_MESSAGE);
            });

            AssertEveryEntryTimestamped(stdout, before, after);
            StringAssert.Contains(stdout, LOG_MESSAGE);
            StringAssert.Contains(stdout, "info:");
            Assert.AreEqual(string.Empty, stderr, $"Information must not be written to stderr but got: '{stderr}'");
        }

        /// <summary>
        /// The startup logger factory in stdio mode keeps stdout free for JSON-RPC while
        /// still timestamping the diagnostics it routes to stderr.
        /// </summary>
        [TestMethod]
        public void GetLoggerFactoryForLogLevel_StdioMode_EmitsTimestampedEntryToStandardErrorOnly()
        {
            (string stdout, string stderr, DateTime before, DateTime after) = CaptureConsole(() =>
            {
                using ILoggerFactory factory = Program.GetLoggerFactoryForLogLevel(LogLevel.Information, stdio: true);
                factory.CreateLogger("TestCategory").LogInformation(LOG_MESSAGE);
            });

            Assert.AreEqual(string.Empty, stdout, $"stdio mode must keep stdout clean but got: '{stdout}'");
            AssertEveryEntryTimestamped(stderr, before, after);
            StringAssert.Contains(stderr, LOG_MESSAGE);
            StringAssert.Contains(stderr, "info:");
        }

        /// <summary>
        /// The web host's logging configuration reuses the console provider registered by
        /// Host.CreateDefaultBuilder(): each event must appear exactly once (a second
        /// provider registration would duplicate every entry) and must be timestamped.
        /// </summary>
        [TestMethod]
        public void ConfigureHostLogging_NormalMode_EmitsEachEntryOnceWithTimestamp()
        {
            (string stdout, string stderr, DateTime before, DateTime after) = CaptureConsole(() =>
            {
                // AddConsole() mirrors the provider Host.CreateDefaultBuilder() registers
                // before ConfigureLogging runs.
                using ILoggerFactory factory = LoggerFactory.Create(builder =>
                {
                    builder.AddConsole();
                    Program.ConfigureHostLogging(builder, runMcpStdio: false);
                });

                factory.CreateLogger("TestCategory").LogInformation(LOG_MESSAGE);
            });

            Assert.AreEqual(1, Regex.Matches(stdout, Regex.Escape(LOG_MESSAGE)).Count,
                $"Expected the entry exactly once (no duplicate console provider) but got: '{stdout}'");
            AssertEveryEntryTimestamped(stdout, before, after);
            Assert.AreEqual(string.Empty, stderr, $"Information must not be written to stderr but got: '{stderr}'");
        }

        /// <summary>
        /// Only one console logger provider ends up registered for the web host.
        /// </summary>
        [TestMethod]
        public void ConfigureHostLogging_NormalMode_RegistersSingleConsoleProvider()
        {
            ServiceCollection services = new();
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                Program.ConfigureHostLogging(builder, runMcpStdio: false);
            });

            int consoleProviderCount = services.Count(descriptor =>
                descriptor.ServiceType == typeof(ILoggerProvider)
                && descriptor.ImplementationType == typeof(ConsoleLoggerProvider));

            Assert.AreEqual(1, consoleProviderCount,
                "Exactly one ConsoleLoggerProvider must be registered; a second one would duplicate every log entry.");
        }

        /// <summary>
        /// In stdio mode the console providers are cleared so nothing can corrupt the
        /// JSON-RPC channel on stdout.
        /// </summary>
        [TestMethod]
        public void ConfigureHostLogging_StdioMode_WritesNothingToConsole()
        {
            (string stdout, string stderr, _, _) = CaptureConsole(() =>
            {
                using ILoggerFactory factory = LoggerFactory.Create(builder =>
                {
                    builder.AddConsole();
                    Program.ConfigureHostLogging(builder, runMcpStdio: true);
                });

                factory.CreateLogger("TestCategory").LogInformation(LOG_MESSAGE);
            });

            Assert.AreEqual(string.Empty, stdout, $"stdio mode must keep stdout clean but got: '{stdout}'");
            Assert.AreEqual(string.Empty, stderr, $"stdio mode clears console providers but got: '{stderr}'");
        }

        /// <summary>
        /// Runs <paramref name="action"/> with the ambient culture set to
        /// <paramref name="cultureName"/> and restores the previous culture afterwards.
        /// The culture is only ambient state for the calling thread's execution context,
        /// so the process-wide default is never modified.
        /// </summary>
        private static void RunUnderCulture(string cultureName, Action action)
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo originalUICulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo culture = new(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUICulture;
            }
        }

        /// <summary>
        /// Guards against the regression tests below silently passing on a runtime built with
        /// globalization-invariant mode, where every culture behaves like the invariant culture.
        /// </summary>
        private static void AssertCultureIsNonGregorian(string cultureName)
        {
            DateTime probe = DateTime.UtcNow;
            string cultureRendering = string.Empty;
            RunUnderCulture(cultureName, () =>
                cultureRendering = probe.ToString(BootstrapLogger.UTC_TIMESTAMP_FORMAT, CultureInfo.CurrentCulture));

            Assert.AreNotEqual(
                probe.ToString(BootstrapLogger.UTC_TIMESTAMP_FORMAT, CultureInfo.InvariantCulture),
                cultureRendering,
                $"Culture '{cultureName}' is expected to use a non-Gregorian calendar; without that this test cannot " +
                "detect culture-sensitive timestamp formatting.");
        }

        /// <summary>
        /// The startup logger factory must emit the Gregorian, invariant-culture UTC prefix even
        /// when the ambient culture uses a different calendar. The built-in "simple" console
        /// formatter renders its timestamp with CultureInfo.CurrentCulture, so relying on its
        /// TimestampFormat option would produce e.g. '2569-08-29T...' under th-TH.
        /// </summary>
        [DataTestMethod]
        [DataRow("ar-SA", false, DisplayName = "ar-SA, normal mode")]
        [DataRow("ar-SA", true, DisplayName = "ar-SA, stdio mode")]
        [DataRow("th-TH", false, DisplayName = "th-TH, normal mode")]
        [DataRow("th-TH", true, DisplayName = "th-TH, stdio mode")]
        public void GetLoggerFactoryForLogLevel_NonGregorianCulture_EmitsInvariantUtcTimestamp(string cultureName, bool stdio)
        {
            AssertCultureIsNonGregorian(cultureName);

            (string stdout, string stderr, DateTime before, DateTime after) = CaptureConsole(() =>
                RunUnderCulture(cultureName, () =>
                {
                    using ILoggerFactory factory = Program.GetLoggerFactoryForLogLevel(LogLevel.Information, stdio: stdio);
                    factory.CreateLogger("TestCategory").LogInformation(LOG_MESSAGE);
                }));

            string output = stdio ? stderr : stdout;
            Assert.AreEqual(string.Empty, stdio ? stdout : stderr,
                "Log entries must only be written to the stream the mode designates.");
            AssertEveryEntryTimestamped(output, before, after);
            StringAssert.Contains(output, LOG_MESSAGE);
        }

        /// <summary>
        /// The web host's logging pipeline must likewise emit the Gregorian, invariant-culture
        /// UTC prefix under a non-Gregorian ambient culture, still exactly once per event.
        /// </summary>
        [DataTestMethod]
        [DataRow("ar-SA")]
        [DataRow("th-TH")]
        public void ConfigureHostLogging_NonGregorianCulture_EmitsInvariantUtcTimestamp(string cultureName)
        {
            AssertCultureIsNonGregorian(cultureName);

            (string stdout, string stderr, DateTime before, DateTime after) = CaptureConsole(() =>
                RunUnderCulture(cultureName, () =>
                {
                    using ILoggerFactory factory = LoggerFactory.Create(builder =>
                    {
                        builder.AddConsole();
                        Program.ConfigureHostLogging(builder, runMcpStdio: false);
                    });

                    factory.CreateLogger("TestCategory").LogInformation(LOG_MESSAGE);
                }));

            Assert.AreEqual(1, Regex.Matches(stdout, Regex.Escape(LOG_MESSAGE)).Count,
                $"Expected the entry exactly once (no duplicate console provider) but got: '{stdout}'");
            AssertEveryEntryTimestamped(stdout, before, after);
            Assert.AreEqual(string.Empty, stderr, $"Information must not be written to stderr but got: '{stderr}'");
        }

        /// <summary>
        /// The bootstrap logger used for pre-dependency-injection diagnostics is subject to the
        /// same requirement.
        /// </summary>
        [DataTestMethod]
        [DataRow("ar-SA")]
        [DataRow("th-TH")]
        public void BootstrapLogger_NonGregorianCulture_EmitsInvariantUtcTimestamp(string cultureName)
        {
            AssertCultureIsNonGregorian(cultureName);

            (string stdout, _, DateTime before, DateTime after) = CaptureConsole(() =>
                RunUnderCulture(cultureName, () => BootstrapLogger.Instance.LogInformation(LOG_MESSAGE)));

            AssertEveryEntryTimestamped(stdout, before, after);
            StringAssert.Contains(stdout, LOG_MESSAGE);
        }

        /// <summary>
        /// Migrated diagnostic: invalid X-Forwarded-* headers produce a timestamped warning
        /// instead of a bare Console.WriteLine.
        /// </summary>
        [DataTestMethod]
        [DataRow("X-Forwarded-Proto", "not a scheme", "X-Forwarded-Proto header", DisplayName = "Invalid forwarded scheme is timestamped")]
        [DataRow("X-Forwarded-Host", "in valid host", "X-Forwarded-Host header", DisplayName = "Invalid forwarded host is timestamped")]
        public void SqlPaginationUtil_InvalidForwardedHeader_LogsTimestampedWarning(string header, string value, string expectedText)
        {
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[header] = value;

            (string stdout, _, DateTime before, DateTime after) = CaptureConsole(() =>
            {
                if (header == "X-Forwarded-Proto")
                {
                    SqlPaginationUtil.ResolveRequestScheme(httpContext.Request);
                }
                else
                {
                    SqlPaginationUtil.ResolveRequestHost(httpContext.Request);
                }
            });

            AssertEveryEntryTimestamped(stdout, before, after);
            StringAssert.Contains(stdout, "warn:");
            StringAssert.Contains(stdout, expectedText);
        }

        /// <summary>
        /// Migrated diagnostic: the config file hash helper reports a missing file through
        /// the bootstrap logger, so the entry is timestamped.
        /// </summary>
        [TestMethod]
        public void FileUtilities_MissingFile_LogsTimestampedWarning()
        {
            string missingPath = Path.Combine(Path.GetTempPath(), $"dab-missing-{Guid.NewGuid():N}.json");
            FileSystem fileSystem = new();

            (string stdout, _, DateTime before, DateTime after) = CaptureConsole(() =>
            {
                Assert.ThrowsException<FileNotFoundException>(
                    () => FileUtilities.ComputeHash(fileSystem, missingPath));
            });

            AssertEveryEntryTimestamped(stdout, before, after);
            StringAssert.Contains(stdout, "warn:");
            StringAssert.Contains(stdout, missingPath);
        }

        /// <summary>
        /// Migrated diagnostic: startup/bootstrap failures are timestamped and, when the host
        /// reserves stdout for JSON-RPC, routed to stderr.
        /// </summary>
        [TestMethod]
        public void BootstrapLogger_StdErrRouting_EmitsTimestampedEntryOnStandardErrorOnly()
        {
            BootstrapLogger.WriteAllOutputToStandardError = true;

            (string stdout, string stderr, DateTime before, DateTime after) = CaptureConsole(
                () => BootstrapLogger.Instance.LogInformation(LOG_MESSAGE));

            Assert.AreEqual(string.Empty, stdout, $"stdout must stay clean but got: '{stdout}'");
            AssertEveryEntryTimestamped(stderr, before, after);
            StringAssert.Contains(stderr, LOG_MESSAGE);
        }

        /// <summary>
        /// Minimal <see cref="BufferedLogRecord"/> stand-in matching the shape
        /// <c>ConsoleLogger.LogRecords()</c> hands to the formatter when replaying buffered entries.
        /// </summary>
        private sealed class TestBufferedLogRecord : BufferedLogRecord
        {
            public override DateTimeOffset Timestamp { get; }

            public override LogLevel LogLevel { get; }

            public override EventId EventId { get; }

            public override string? Exception { get; }

            public override string? FormattedMessage { get; }

            public TestBufferedLogRecord(DateTimeOffset timestamp, LogLevel logLevel, EventId eventId, string? message, string? exception)
            {
                Timestamp = timestamp;
                LogLevel = logLevel;
                EventId = eventId;
                FormattedMessage = message;
                Exception = exception;
            }
        }

        /// <summary>
        /// Invokes the formatter exactly as <c>ConsoleLogger.LogRecords()</c> does for a buffered
        /// entry: the state is the <see cref="BufferedLogRecord"/> and both the formatter delegate
        /// and <c>LogEntry.Exception</c> are null.
        /// </summary>
        private static string FormatBufferedRecord(BufferedLogRecord record, string category)
        {
            ServiceCollection services = new();
            services.AddLogging(builder => builder.AddUtcTimestampConsoleFormatter());
            using ServiceProvider provider = services.BuildServiceProvider();

            ConsoleFormatter formatter = provider.GetRequiredService<IEnumerable<ConsoleFormatter>>()
                .Single(f => f.Name == UtcTimestampConsoleFormatter.FORMATTER_NAME);

            LogEntry<BufferedLogRecord> entry = new(
                record.LogLevel,
                category,
                record.EventId,
                record,
                exception: null,
                formatter: null!);

            StringWriter writer = new();
            formatter.Write(in entry, scopeProvider: null, writer);
            return writer.ToString();
        }

        /// <summary>
        /// A buffered entry must be stamped with the time the event originally occurred, not the
        /// time it was flushed, and must still carry the invariant Gregorian UTC prefix.
        /// </summary>
        [DataTestMethod]
        [DataRow("en-US")]
        [DataRow("th-TH")]
        public void Formatter_BufferedLogRecord_UsesOriginalTimestamp(string cultureName)
        {
            // A fixed instant well in the past, so a flush-time timestamp cannot coincide with it.
            DateTimeOffset recorded = new(2021, 3, 4, 5, 6, 7, 89, TimeSpan.Zero);
            TestBufferedLogRecord record = new(recorded, LogLevel.Warning, new EventId(42), LOG_MESSAGE, exception: null);

            string output = string.Empty;
            RunUnderCulture(cultureName, () => output = FormatBufferedRecord(record, "TestCategory"));

            StringAssert.StartsWith(output, "2021-03-04T05:06:07.089Z ",
                $"Buffered entry must be stamped with the record's own UTC timestamp but got: '{output}'");
            StringAssert.Contains(output, "warn:");
            StringAssert.Contains(output, "TestCategory[42]");
            StringAssert.Contains(output, LOG_MESSAGE);
        }

        /// <summary>
        /// A buffered entry stores its exception as a preformatted string on the record while
        /// LogEntry.Exception is null, so reading only the latter would silently drop it.
        /// </summary>
        [TestMethod]
        public void Formatter_BufferedLogRecord_WritesBufferedException()
        {
            const string EXCEPTION_TEXT = "System.InvalidOperationException: buffered boom";
            TestBufferedLogRecord record = new(
                DateTimeOffset.UtcNow, LogLevel.Error, new EventId(7), LOG_MESSAGE, EXCEPTION_TEXT);

            string output = FormatBufferedRecord(record, "TestCategory");

            StringAssert.Contains(output, EXCEPTION_TEXT,
                $"Buffered exception must not be dropped but got: '{output}'");
            StringAssert.Contains(output, LOG_MESSAGE);
            StringAssert.Contains(output, "fail:");
        }

        /// <summary>
        /// Log messages can carry untrusted values, so terminal control characters must be escaped
        /// rather than written through to the console (as the built-in formatter also does).
        /// Tab, carriage return and line feed remain intact for log formatting.
        /// </summary>
        [TestMethod]
        public void Formatter_ControlCharactersInMessage_AreEscaped()
        {
            TestBufferedLogRecord record = new(
                DateTimeOffset.UtcNow,
                LogLevel.Information,
                new EventId(0),
                "injected\u001b[31mred\u0007bell\tkept",
                exception: null);

            string output = FormatBufferedRecord(record, "TestCategory");

            Assert.IsFalse(output.Contains('\u001b'), $"ESC must be escaped but got: '{output}'");
            Assert.IsFalse(output.Contains('\u0007'), $"BEL must be escaped but got: '{output}'");
            StringAssert.Contains(output, "\\u001B");
            StringAssert.Contains(output, "\\u0007");
            StringAssert.Contains(output, "bell\tkept", "Tab must be preserved for log formatting.");
        }

        /// <summary>
        /// Matches a direct write to the console, e.g. <c>Console.WriteLine(</c>,
        /// <c>Console.Error.Write(</c> or <c>Console.Out.WriteLine(</c>.
        /// </summary>
        private static readonly Regex _directConsoleWrite =
            new(@"\bConsole\s*\.\s*(?:(?:Error|Out)\s*\.\s*)?Write(?:Line)?\s*\(", RegexOptions.Compiled);

        /// <summary>
        /// Production source files permitted to write to the console directly, with the reason.
        /// Everything else must log through <see cref="BootstrapLogger"/> or an injected
        /// <see cref="ILogger"/> so the entry carries the invariant UTC millisecond prefix.
        /// </summary>
        private static readonly Dictionary<string, string> _allowedDirectConsoleWriters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cli/CustomLoggerProvider.cs"] = "Is the CLI console logger implementation; it writes the timestamp itself.",
            ["Cli/Commands/AppNameOptions.cs"] = "Intentional command result (encoded/decoded app name), not a diagnostic.",
            ["Cli/ConfigGenerator.cs"] = "Intentional command result (auto-entities simulation table), not a diagnostic."
        };

        /// <summary>
        /// Guards the completeness of the direct-console inventory: every production source file
        /// must route log-like diagnostics through a logger rather than <c>Console.Write*</c>.
        /// This is what ties the Aspire AppHost (and any future call site) to the invariant UTC
        /// prefix - the prefix itself is asserted by the BootstrapLogger tests above, so proving a
        /// file has no bare console writes proves its diagnostics carry that prefix.
        /// Intentional command output is allow-listed with a justification.
        /// </summary>
        [TestMethod]
        public void ProductionSources_DoNotWriteDiagnosticsDirectlyToConsole()
        {
            DirectoryInfo sourceRoot = FindSourceRoot();
            string[] productionProjects =
            {
                "Aspire.AppHost", "Auth", "Azure.DataApiBuilder.Mcp", "Cli",
                "Config", "Core", "Service", "Service.GraphQLBuilder"
            };

            List<string> violations = new();
            foreach (string project in productionProjects)
            {
                string projectPath = Path.Combine(sourceRoot.FullName, project);
                Assert.IsTrue(Directory.Exists(projectPath), $"Expected production project directory '{projectPath}' to exist.");

                foreach (string file in Directory.EnumerateFiles(projectPath, "*.cs", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(sourceRoot.FullName, file).Replace('\\', '/');

                    // Generated and intermediate build output is not hand-written source.
                    if (relativePath.Contains("/obj/", StringComparison.Ordinal)
                        || relativePath.Contains("/bin/", StringComparison.Ordinal)
                        || _allowedDirectConsoleWriters.ContainsKey(relativePath))
                    {
                        continue;
                    }

                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // Skip comments, which legitimately mention Console.WriteLine in prose.
                        string trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("//", StringComparison.Ordinal)
                            || trimmed.StartsWith("*", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (_directConsoleWrite.IsMatch(lines[i]))
                        {
                            violations.Add($"{relativePath}({i + 1}): {trimmed}");
                        }
                    }
                }
            }

            Assert.AreEqual(0, violations.Count,
                "Log-like diagnostics must be emitted through a logger so they carry the invariant UTC timestamp prefix. "
                + "If a write is intentional command output, add it to _allowedDirectConsoleWriters with a justification. "
                + $"Found:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        /// <summary>
        /// Walks up from the test assembly location to the repository's 'src' directory,
        /// identified by the solution file it contains.
        /// </summary>
        private static DirectoryInfo FindSourceRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Azure.DataApiBuilder.sln")))
                {
                    return directory;
                }

                directory = directory.Parent;
            }

            throw new AssertFailedException(
                $"Could not locate the 'src' directory (containing Azure.DataApiBuilder.sln) from '{AppContext.BaseDirectory}'.");
        }
    }
}
