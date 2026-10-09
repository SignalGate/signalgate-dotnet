using System;
using System.Collections.Generic;
using System.Linq;

namespace SignalGate.Tests.Fakes;

public sealed record LogEntry(string Level, string Message, IReadOnlyDictionary<string, object?>? Fields);

// Records every call thread-safely. The optional hook runs after each call is recorded, on the calling thread,
// and may throw, block or call back into the client.
public sealed class CapturingLogger : ISignalGateLogger
{
    public const string DebugLevel = "debug";
    public const string InfoLevel = "info";
    public const string WarnLevel = "warn";
    public const string ErrorLevel = "error";

    private readonly object _sync = new();
    private readonly List<LogEntry> _entries = [];
    private volatile Action<LogEntry>? _hook;

    public Action<LogEntry>? Hook
    {
        get => _hook;
        set => _hook = value;
    }

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    public static CapturingLogger Throwing()
    {
        return new CapturingLogger { Hook = _ => throw new InvalidOperationException("logger failure") };
    }

    public IReadOnlyList<LogEntry> WithMessage(string message)
    {
        return Entries.Where(entry => entry.Message == message).ToArray();
    }

    public void Debug(string message, IReadOnlyDictionary<string, object?>? fields = null) => Add(DebugLevel, message, fields);

    public void Info(string message, IReadOnlyDictionary<string, object?>? fields = null) => Add(InfoLevel, message, fields);

    public void Warn(string message, IReadOnlyDictionary<string, object?>? fields = null) => Add(WarnLevel, message, fields);

    public void Error(string message, IReadOnlyDictionary<string, object?>? fields = null) => Add(ErrorLevel, message, fields);

    private void Add(string level, string message, IReadOnlyDictionary<string, object?>? fields)
    {
        var entry = new LogEntry(level, message, fields);
        lock (_sync)
        {
            _entries.Add(entry);
        }

        _hook?.Invoke(entry);
    }
}
