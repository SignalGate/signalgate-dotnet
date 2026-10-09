using System;
using System.Collections.Generic;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests.Model;

public sealed class ExceptionTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Every_exception_type_derives_from_the_base_type()
    {
        Assert.True(typeof(SignalGateException).IsSubclassOf(typeof(Exception)));
        Assert.False(typeof(SignalGateException).IsSealed);

        foreach (Type type in new[]
        {
            typeof(SignalGateConfigException),
            typeof(SignalGateTimeoutException),
            typeof(SignalGateNetworkException),
            typeof(SignalGateServerException),
        })
        {
            Assert.True(type.IsSubclassOf(typeof(SignalGateException)), type.Name);
            Assert.True(type.IsSealed, type.Name);
            Assert.True(type.IsPublic, type.Name);
        }
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Standard_constructors_set_the_message_and_inner_exception()
    {
        var inner = new InvalidOperationException("inner");

        AssertStandardConstructors(new SignalGateException(), new SignalGateException("m"), new SignalGateException("m", inner), inner);
        AssertStandardConstructors(new SignalGateConfigException(), new SignalGateConfigException("m"), new SignalGateConfigException("m", inner), inner);
        AssertStandardConstructors(new SignalGateTimeoutException(), new SignalGateTimeoutException("m"), new SignalGateTimeoutException("m", inner), inner);
        AssertStandardConstructors(new SignalGateNetworkException(), new SignalGateNetworkException("m"), new SignalGateNetworkException("m", inner), inner);
        AssertStandardConstructors(new SignalGateServerException(), new SignalGateServerException("m"), new SignalGateServerException("m", inner), inner);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Server_exception_from_a_response_formats_its_message()
    {
        var error = new SignalGateServerException(401, "UNAUTHORIZED", "Invalid API key", "req_401");

        Assert.Equal(401, error.StatusCode);
        Assert.Equal("UNAUTHORIZED", error.Code);
        Assert.Equal("Invalid API key", error.ServerMessage);
        Assert.Equal("req_401", error.RequestId);
        Assert.Null(error.Details);
        Assert.Null(error.InnerException);
        Assert.Equal("[401] UNAUTHORIZED: Invalid API key", error.Message);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Server_exception_with_empty_fields_keeps_the_message_shape()
    {
        var error = new SignalGateServerException(502, "", "", "req_hdr");

        Assert.Equal("[502] : ", error.Message);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Server_exception_stores_null_strings_as_empty()
    {
        var error = new SignalGateServerException(500, null!, null!, null!);

        Assert.Equal("", error.Code);
        Assert.Equal("", error.ServerMessage);
        Assert.Equal("", error.RequestId);
        Assert.Equal("[500] : ", error.Message);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Server_exception_keeps_details_and_inner_exception()
    {
        var details = new Dictionary<string, string> { ["field"] = "user_id" };
        var inner = new InvalidOperationException("inner");

        var error = new SignalGateServerException(400, "BAD_REQUEST", "Invalid request format", "req_1", details, inner);

        Assert.NotNull(error.Details);
        Assert.Equal("user_id", error.Details["field"]);
        Assert.Same(inner, error.InnerException);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Server_exception_standard_constructors_leave_response_fields_empty()
    {
        var error = new SignalGateServerException("m");

        Assert.Equal(0, error.StatusCode);
        Assert.Equal("", error.Code);
        Assert.Equal("", error.ServerMessage);
        Assert.Equal("", error.RequestId);
        Assert.Null(error.Details);
    }

    private static void AssertStandardConstructors(Exception empty, Exception withMessage, Exception withInner, Exception inner)
    {
        Assert.False(string.IsNullOrEmpty(empty.Message));
        Assert.Null(empty.InnerException);
        Assert.Equal("m", withMessage.Message);
        Assert.Null(withMessage.InnerException);
        Assert.Equal("m", withInner.Message);
        Assert.Same(inner, withInner.InnerException);
    }
}
