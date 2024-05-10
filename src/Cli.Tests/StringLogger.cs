// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Creates a logger that can be used in test methods to verify logging behavior
/// by capturing the messages and making them available for verification.
/// </summary>
class StringLogger : ILogger
{
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return new Mock<IDisposable>().Object;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception);
        Messages.Add(message);
    }

    public string GetLog()
    {
        return string.Join(Environment.NewLine, Messages);
    }
}

