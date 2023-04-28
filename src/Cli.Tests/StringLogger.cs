// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;
internal class StringLogger : ILogger
{
    public List<string> Messages { get; } = new();

    public IDisposable BeginScope<TState>(TState state)
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

