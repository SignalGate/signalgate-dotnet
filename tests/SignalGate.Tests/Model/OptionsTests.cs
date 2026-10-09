using System;
using System.Collections.Generic;
using System.Net.Http;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Model;

public sealed class OptionsTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Defaults_match_the_documented_values()
    {
        var options = new SignalGateClientOptions { ApiKey = StandardEvent.ApiKey };

        Assert.Equal(StandardEvent.ApiKey, options.ApiKey);
        Assert.Equal(3000, options.CheckTimeoutMs);
        Assert.Equal(1000, options.LogTimeoutMs);
        Assert.Equal(10_000, options.LogQueueCapacity);
        Assert.Equal(3, options.LogMaxRetries);
        Assert.Equal(200, options.LogRetryBaseMs);
        Assert.True(options.FailOpen);
        Assert.Null(options.Logger);
        Assert.Null(options.HttpHandler);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void ToString_lists_every_option_with_the_key_masked()
    {
        var options = new SignalGateClientOptions { ApiKey = StandardEvent.ApiKey };

        Assert.Equal(
            "SignalGateClientOptions { ApiKey = ***REDACTED***, CheckTimeoutMs = 3000, LogTimeoutMs = 1000, "
                + "LogQueueCapacity = 10000, LogMaxRetries = 3, LogRetryBaseMs = 200, FailOpen = True, "
                + "Logger = null, HttpHandler = null }",
            options.ToString());
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void ToString_names_the_logger_and_handler_types_and_masks_the_key()
    {
        using var handler = new HttpClientHandler();
        var options = new SignalGateClientOptions
        {
            ApiKey = StandardEvent.ApiKey,
            Logger = NoopSignalGateLogger.Instance,
            HttpHandler = handler,
            FailOpen = false,
        };

        string text = options.ToString();

        Assert.Contains("ApiKey = ***REDACTED***", text, StringComparison.Ordinal);
        Assert.Contains("Logger = NoopSignalGateLogger", text, StringComparison.Ordinal);
        Assert.Contains("HttpHandler = HttpClientHandler", text, StringComparison.Ordinal);
        Assert.Contains("FailOpen = False", text, StringComparison.Ordinal);
        Assert.DoesNotContain(StandardEvent.ApiKey, text, StringComparison.Ordinal);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(ConfigCases.Rejected), MemberType = typeof(ConfigCases))]
    public void Invalid_options_are_rejected_with_a_descriptive_message(string caseName, string expectedMessage)
    {
        SignalGateClientOptions options = ConfigCases.Build(caseName);

        SignalGateConfigException error = Assert.Throws<SignalGateConfigException>(() => ClientSettings.From(options));

        Assert.Equal(expectedMessage, error.Message);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(ConfigCases.Accepted), MemberType = typeof(ConfigCases))]
    public void Valid_options_are_accepted_and_copied(string caseName)
    {
        SignalGateClientOptions options = ConfigCases.Build(caseName);

        ClientSettings settings = ClientSettings.From(options);

        Assert.Equal(options.ApiKey, settings.ApiKey);
        Assert.Equal(options.CheckTimeoutMs, settings.CheckTimeoutMs);
        Assert.Equal(options.LogTimeoutMs, settings.LogTimeoutMs);
        Assert.Equal(options.LogQueueCapacity, settings.LogQueueCapacity);
        Assert.Equal(options.LogMaxRetries, settings.LogMaxRetries);
        Assert.Equal(options.LogRetryBaseMs, settings.LogRetryBaseMs);
        Assert.Equal(options.FailOpen, settings.FailOpen);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Key_is_kept_exactly_as_given()
    {
        ClientSettings spaced = ClientSettings.From(ConfigCases.Build("key with surrounding spaces"));
        ClientSettings tabbed = ClientSettings.From(ConfigCases.Build("key with tab"));

        Assert.Equal("  pk_test_unit_key  ", spaced.ApiKey);
        Assert.Equal("pk\tinner", tabbed.ApiKey);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Null_options_throw_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => ClientSettings.From(null!));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Changing_options_after_validation_does_not_change_the_settings()
    {
        var logger = new ListLogger();
        using var handler = new HttpClientHandler();
        var options = new SignalGateClientOptions
        {
            ApiKey = StandardEvent.ApiKey,
            CheckTimeoutMs = 50,
            LogTimeoutMs = 60,
            LogQueueCapacity = 70,
            LogMaxRetries = 1,
            LogRetryBaseMs = 2,
            FailOpen = false,
            Logger = logger,
            HttpHandler = handler,
        };

        ClientSettings settings = ClientSettings.From(options);
        options.ApiKey = "test-key";
        options.CheckTimeoutMs = 1;
        options.LogTimeoutMs = 1;
        options.LogQueueCapacity = 1;
        options.LogMaxRetries = 9;
        options.LogRetryBaseMs = 9;
        options.FailOpen = true;
        options.Logger = null;
        options.HttpHandler = null;

        Assert.Equal(StandardEvent.ApiKey, settings.ApiKey);
        Assert.Equal(50, settings.CheckTimeoutMs);
        Assert.Equal(60, settings.LogTimeoutMs);
        Assert.Equal(70, settings.LogQueueCapacity);
        Assert.Equal(1, settings.LogMaxRetries);
        Assert.Equal(2, settings.LogRetryBaseMs);
        Assert.False(settings.FailOpen);
        Assert.Same(logger, settings.Logger);
        Assert.Same(handler, settings.HttpHandler);
    }

    private sealed class ListLogger : ISignalGateLogger
    {
        public List<string> Messages { get; } = [];

        public void Debug(string message, IReadOnlyDictionary<string, object?>? fields = null) => Messages.Add(message);

        public void Info(string message, IReadOnlyDictionary<string, object?>? fields = null) => Messages.Add(message);

        public void Warn(string message, IReadOnlyDictionary<string, object?>? fields = null) => Messages.Add(message);

        public void Error(string message, IReadOnlyDictionary<string, object?>? fields = null) => Messages.Add(message);
    }
}
