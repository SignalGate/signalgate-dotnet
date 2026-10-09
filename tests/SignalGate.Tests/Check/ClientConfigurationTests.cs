using System;
using System.Threading.Tasks;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

public sealed class ClientConfigurationTests
{
    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(ConfigCases.Rejected), MemberType = typeof(ConfigCases))]
    public void Invalid_options_are_rejected_by_the_constructor(string caseName, string expectedMessage)
    {
        SignalGateClientOptions options = ConfigCases.Build(caseName);

        SignalGateConfigException error = Assert.Throws<SignalGateConfigException>(() => new SignalGateClient(options));

        Assert.Equal(expectedMessage, error.Message);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(ConfigCases.Accepted), MemberType = typeof(ConfigCases))]
    public async Task Valid_options_are_accepted_by_the_constructor(string caseName)
    {
        var client = new SignalGateClient(ConfigCases.Build(caseName));
        await using ClosingScope closing = client.ClosesAtEnd();

        Assert.NotNull(client.OwnedHandler);
        Assert.Empty(client.Metrics.SnapshotFlat());
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pk\ninner")]
    public void Invalid_key_strings_are_rejected_by_the_key_constructor(string apiKey)
    {
        Assert.Throws<SignalGateConfigException>(() => new SignalGateClient(apiKey));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Key_constructor_accepts_a_key_without_prefix()
    {
        var client = new SignalGateClient("test-key");
        await using ClosingScope closing = client.ClosesAtEnd();

        Assert.NotNull(client.OwnedHandler);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Null_options_throw_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => new SignalGateClient((SignalGateClientOptions)null!));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Null_key_is_a_configuration_error()
    {
        SignalGateConfigException error = Assert.Throws<SignalGateConfigException>(() => new SignalGateClient((string)null!));

        Assert.False(string.IsNullOrEmpty(error.Message));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Changing_options_after_construction_changes_nothing()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(502));
        using var otherHandler = new FakeHttpHandler();
        var logger = new CapturingLogger();
        var otherLogger = new CapturingLogger();
        var options = new SignalGateClientOptions
        {
            ApiKey = StandardEvent.ApiKey,
            HttpHandler = handler,
            Logger = logger,
        };
        var client = new SignalGateClient(options);
        await using ClosingScope closing = client.ClosesAtEnd();

        options.FailOpen = false;
        options.ApiKey = "test-key";
        options.CheckTimeoutMs = 1;
        options.HttpHandler = otherHandler;
        options.Logger = otherLogger;
        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        TestClients.AssertFailedOpen(result);
        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer " + StandardEvent.ApiKey, request.Headers["Authorization"]);
        Assert.Equal(0, otherHandler.CallCount);
        Assert.Single(logger.WithMessage("signalgate.check.failed_open"));
        Assert.Empty(otherLogger.Entries);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task ToString_masks_the_key()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        string text = client.ToString();

        Assert.Equal("SignalGateClient { ApiKey = ***REDACTED*** }", text);
        Assert.DoesNotContain(StandardEvent.ApiKey, text, StringComparison.Ordinal);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Sdk_name_and_version_are_the_ones_sent_in_the_user_agent()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());

        Assert.Equal("signalgate-backend-sdk", SignalGateClient.SdkName);
        Assert.Equal(UserAgent.SdkVersion, SignalGateClient.SdkVersion);
        string userAgent = Assert.Single(handler.Requests).Headers["User-Agent"];
        Assert.StartsWith(SignalGateClient.SdkName + "/" + SignalGateClient.SdkVersion + " (dotnet/", userAgent, StringComparison.Ordinal);
    }
}
