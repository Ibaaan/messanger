using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Messanger.IntegrationTests.Support;

internal sealed class SecurityLogCollector : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CollectorLogger(this, categoryName);
    public void Dispose() { }
    private sealed class CollectorLogger(SecurityLogCollector owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => category == "Messanger.Security.SecurityAudit" || category.StartsWith("Messanger.Operations.", StringComparison.Ordinal);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Messages.Enqueue(formatter(state, exception));
        }
    }
}
