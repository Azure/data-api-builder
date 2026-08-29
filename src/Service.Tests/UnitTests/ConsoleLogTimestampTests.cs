// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
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
    }
}
