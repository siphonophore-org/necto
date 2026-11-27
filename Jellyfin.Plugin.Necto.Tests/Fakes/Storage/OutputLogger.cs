using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

/// <summary>
/// Writes a live test's log to its output, exceptions included, so a failure on a real machine shows why.
/// </summary>
/// <typeparam name="T">The class logging.</typeparam>
internal sealed class OutputLogger<T> : ILogger<T>
{
    private readonly ITestOutputHelper _output;

    public OutputLogger(ITestOutputHelper output) => _output = output;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _output.WriteLine(logLevel + ": " + formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
}
