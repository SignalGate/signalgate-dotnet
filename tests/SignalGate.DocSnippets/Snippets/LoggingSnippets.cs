using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SignalGate.DocSnippets.Snippets;

#region readme:logging-adapter
public sealed class SignalGateLoggerAdapter(ILogger<SignalGateClient> logger) : ISignalGateLogger
{
    public void Debug(string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Write(LogLevel.Debug, message, fields);

    public void Info(string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Write(LogLevel.Information, message, fields);

    public void Warn(string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Write(LogLevel.Warning, message, fields);

    public void Error(string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Write(LogLevel.Error, message, fields);

    private void Write(LogLevel level, string message, IReadOnlyDictionary<string, object?>? fields)
    {
        if (!logger.IsEnabled(level))
        {
            return;
        }

        // The fields travel as structured state; the text is the event name.
        var state = new List<KeyValuePair<string, object?>> { new("event", message) };
        if (fields is not null)
        {
            state.AddRange(fields);
        }

        logger.Log(level, default, state, null, (_, _) => message);
    }
}
#endregion

internal static class LoggingSnippets
{
    internal static void Register(WebApplicationBuilder builder)
    {
        #region readme:logging-register
        builder.Services.AddSingleton<SignalGateLoggerAdapter>();
        builder.Services.AddSingleton(services => new SignalGateClient(new SignalGateClientOptions
        {
            ApiKey = builder.Configuration["SignalGate:ApiKey"]!,
            Logger = services.GetRequiredService<SignalGateLoggerAdapter>(),
        }));
        #endregion
    }
}
