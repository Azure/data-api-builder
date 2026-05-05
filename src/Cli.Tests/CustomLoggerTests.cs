// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Tests for <see cref="CustomLoggerProvider"/> covering both the standard CLI
/// path (writes to stdout/stderr with abbreviated labels) and the MCP stdio path
/// (suppressed by default, opt-in via either CLI <c>--LogLevel</c> or the
/// runtime config's <c>log-level</c>, always routed to stderr to keep the
/// JSON-RPC channel on stdout uncorrupted).
/// </summary>
[TestClass]
public class CustomLoggerTests
{
    /// <summary>
    /// The CustomConsoleLogger reads several static flags from <see cref="Cli.Utils"/>.
    /// Reset them around every test so cases cannot leak into each other and so the
    /// rest of the test suite continues to see the default (non-MCP) behavior.
    /// </summary>
    [TestInitialize]
    [TestCleanup]
    public void ResetMcpStaticState()
    {
        Cli.Utils.IsMcpStdioMode = false;
        Cli.Utils.IsLogLevelOverriddenByCli = false;
        Cli.Utils.IsLogLevelOverriddenByConfig = false;
        Cli.Utils.CliLogLevel = LogLevel.Information;
        Cli.Utils.ConfigLogLevel = LogLevel.Information;
    }

    /// <summary>
    /// Redirects Console.Out and Console.Error around <paramref name="action"/>
    /// and returns whatever was written to each. Restores the original writers
    /// on exit.
    /// </summary>
    private static (string Stdout, string Stderr) CaptureConsole(Action action)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        StringWriter stdout = new();
        StringWriter stderr = new();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            action();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        return (stdout.ToString(), stderr.ToString());
    }

    private static ILogger NewLogger() =>
        new CustomLoggerProvider().CreateLogger("TestCategory");

    /// <summary>
    /// Standard (non-MCP) path: each log level produces the correct abbreviated
    /// label matching ASP.NET Core's default console formatter and is routed to
    /// stdout for Information/Warning and stderr for Error/Critical.
    /// </summary>
    [DataTestMethod]
    [DataRow(LogLevel.Information, "info:", false)]
    [DataRow(LogLevel.Warning, "warn:", false)]
    [DataRow(LogLevel.Error, "fail:", true)]
    [DataRow(LogLevel.Critical, "crit:", true)]
    public void LogOutput_UsesAbbreviatedLogLevelLabels(LogLevel logLevel, string expectedPrefix, bool expectStderr)
    {
        const string Message = "test message";

        (string stdout, string stderr) = CaptureConsole(() => NewLogger().Log(logLevel, Message));

        string actual = expectStderr ? stderr : stdout;
        string other = expectStderr ? stdout : stderr;

        Assert.IsTrue(actual.StartsWith(expectedPrefix),
            $"Expected output to start with '{expectedPrefix}' but got: '{actual}'");
        StringAssert.Contains(actual, Message);
        Assert.AreEqual(string.Empty, other,
            $"Did not expect output on the other stream but got: '{other}'");
    }

    /// <summary>
    /// MCP stdio mode with no overrides (neither CLI <c>--LogLevel</c> nor
    /// config <c>log-level</c>): all output must be suppressed so the JSON-RPC
    /// channel stays clean.
    /// </summary>
    [TestMethod]
    public void Mcp_NoOverrides_SuppressesAllOutput()
    {
        Cli.Utils.IsMcpStdioMode = true;

        (string stdout, string stderr) = CaptureConsole(() =>
        {
            ILogger logger = NewLogger();
            logger.Log(LogLevel.Information, "info should not appear");
            logger.Log(LogLevel.Error, "error should not appear");
        });

        Assert.AreEqual(string.Empty, stdout, "MCP mode without overrides must not write to stdout.");
        Assert.AreEqual(string.Empty, stderr, "MCP mode without overrides must not write to stderr.");
    }

    /// <summary>
    /// MCP stdio mode with a CLI-supplied <c>--LogLevel</c>: logs must always
    /// go to stderr (never stdout) and the level threshold from
    /// <see cref="Cli.Utils.CliLogLevel"/> must be honored.
    /// </summary>
    [TestMethod]
    public void Mcp_CliOverride_WritesToStderrAndHonorsCliLevel()
    {
        Cli.Utils.IsMcpStdioMode = true;
        Cli.Utils.IsLogLevelOverriddenByCli = true;
        Cli.Utils.CliLogLevel = LogLevel.Warning;

        (string stdout, string stderr) = CaptureConsole(() =>
        {
            ILogger logger = NewLogger();
            logger.Log(LogLevel.Information, "filtered info");   // below threshold
            logger.Log(LogLevel.Warning, "visible warn");        // at threshold
            logger.Log(LogLevel.Error, "visible error");         // above threshold
        });

        Assert.AreEqual(string.Empty, stdout, "MCP mode must never write to stdout.");
        Assert.IsFalse(stderr.Contains("filtered info"), $"Below-threshold log should be filtered. Got: '{stderr}'");
        StringAssert.Contains(stderr, "warn: visible warn");
        StringAssert.Contains(stderr, "fail: visible error");
    }

    /// <summary>
    /// Bug fix: MCP stdio mode where only the runtime config (no CLI flag)
    /// supplied the log level. Previously suppressed; must now emit to stderr
    /// using <see cref="Cli.Utils.ConfigLogLevel"/>.
    /// </summary>
    [TestMethod]
    public void Mcp_ConfigOverride_WritesToStderrAndHonorsConfigLevel()
    {
        Cli.Utils.IsMcpStdioMode = true;
        Cli.Utils.IsLogLevelOverriddenByConfig = true;
        Cli.Utils.ConfigLogLevel = LogLevel.Information;

        (string stdout, string stderr) = CaptureConsole(() =>
        {
            ILogger logger = NewLogger();
            logger.Log(LogLevel.Debug, "filtered debug");      // below threshold
            logger.Log(LogLevel.Information, "visible info");  // at threshold
        });

        Assert.AreEqual(string.Empty, stdout, "MCP mode must never write to stdout.");
        Assert.IsFalse(stderr.Contains("filtered debug"), $"Below-threshold log should be filtered. Got: '{stderr}'");
        StringAssert.Contains(stderr, "info: visible info");
    }

    /// <summary>
    /// Precedence: when both CLI and config supply a log level, the CLI value
    /// wins (CLI &gt; Config &gt; None).
    /// </summary>
    [TestMethod]
    public void Mcp_CliOverridePrecedesConfigOverride()
    {
        Cli.Utils.IsMcpStdioMode = true;
        Cli.Utils.IsLogLevelOverriddenByCli = true;
        Cli.Utils.CliLogLevel = LogLevel.Warning;
        Cli.Utils.IsLogLevelOverriddenByConfig = true;
        Cli.Utils.ConfigLogLevel = LogLevel.Information;

        (_, string stderr) = CaptureConsole(() =>
        {
            ILogger logger = NewLogger();
            logger.Log(LogLevel.Information, "filtered by CLI Warning");
            logger.Log(LogLevel.Warning, "passes CLI Warning");
        });

        Assert.IsFalse(stderr.Contains("filtered by CLI Warning"),
            $"CLI level should override config and filter Information. Got: '{stderr}'");
        StringAssert.Contains(stderr, "warn: passes CLI Warning");
    }
}
