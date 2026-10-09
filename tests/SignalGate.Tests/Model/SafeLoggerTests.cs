using System;
using System.Collections.Generic;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests.Model;

public sealed class SafeLoggerTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void A_logger_that_always_throws_never_escapes()
    {
        var logger = new SafeLogger(new ThrowingLogger());
        var fields = new Dictionary<string, object?> { ["k"] = "v" };

        Assert.True(logger.IsEnabled);
        logger.Debug("signalgate.http.request", fields);
        logger.Info("signalgate.test");
        logger.Warn("signalgate.check.failed_open", fields);
        logger.Error("signalgate.log.queue_full");
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Calls_are_forwarded_with_their_level_message_and_fields()
    {
        var inner = new RecordingLogger();
        var logger = new SafeLogger(inner);
        var fields = new Dictionary<string, object?> { ["status"] = 200 };

        logger.Debug("d", fields);
        logger.Info("i");
        logger.Warn("w", fields);
        logger.Error("e");

        (string Level, string Message, object? Fields)[] expected =
        [
            ("debug", "d", fields),
            ("info", "i", null),
            ("warn", "w", fields),
            ("error", "e", null),
        ];
        Assert.Equal(expected, inner.Entries);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Is_disabled_without_a_logger_or_with_the_no_op_logger()
    {
        Assert.False(new SafeLogger(null).IsEnabled);
        Assert.False(new SafeLogger(NoopSignalGateLogger.Instance).IsEnabled);
        Assert.True(new SafeLogger(new RecordingLogger()).IsEnabled);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Disabled_logger_ignores_calls()
    {
        var logger = new SafeLogger(null);

        logger.Debug("d");
        logger.Info("i");
        logger.Warn("w");
        logger.Error("e");

        Assert.False(logger.IsEnabled);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void No_op_logger_is_a_single_shared_instance_that_accepts_every_call()
    {
        NoopSignalGateLogger logger = NoopSignalGateLogger.Instance;

        logger.Debug("d");
        logger.Info("i", new Dictionary<string, object?>());
        logger.Warn("w");
        logger.Error("e");

        Assert.Same(logger, NoopSignalGateLogger.Instance);
    }

    private sealed class ThrowingLogger : ISignalGateLogger
    {
        public void Debug(string message, IReadOnlyDictionary<string, object?>? fields = null) => throw new InvalidOperationException("debug");

        public void Info(string message, IReadOnlyDictionary<string, object?>? fields = null) => throw new InvalidOperationException("info");

        public void Warn(string message, IReadOnlyDictionary<string, object?>? fields = null) => throw new InvalidOperationException("warn");

        public void Error(string message, IReadOnlyDictionary<string, object?>? fields = null) => throw new InvalidOperationException("error");
    }

    private sealed class RecordingLogger : ISignalGateLogger
    {
        public List<(string Level, string Message, object? Fields)> Entries { get; } = [];

        public void Debug(string message, IReadOnlyDictionary<string, object?>? fields = null) => Entries.Add(("debug", message, fields));

        public void Info(string message, IReadOnlyDictionary<string, object?>? fields = null) => Entries.Add(("info", message, fields));

        public void Warn(string message, IReadOnlyDictionary<string, object?>? fields = null) => Entries.Add(("warn", message, fields));

        public void Error(string message, IReadOnlyDictionary<string, object?>? fields = null) => Entries.Add(("error", message, fields));
    }
}
