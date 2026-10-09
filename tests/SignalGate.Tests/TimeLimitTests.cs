using System;
using System.Linq;
using System.Reflection;
using SignalGate.Tests.Fakes;
using Xunit;
using Xunit.v3;

namespace SignalGate.Tests;

public sealed class TimeLimitTests
{
    // A test without a time limit that deadlocks, for example while closing a client, hangs the whole run instead
    // of failing on its own.
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Every_test_has_a_time_limit()
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var tests = typeof(TimeLimitTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(Declared))
            .Select(method => (Method: method, Attribute: method.GetCustomAttributes(inherit: true).OfType<IFactAttribute>().SingleOrDefault()))
            .Where(test => test.Attribute is not null)
            .ToList();

        string[] unbounded = tests
            .Where(test => test.Attribute!.Timeout <= 0)
            .Select(test => test.Method.DeclaringType!.FullName + "." + test.Method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(tests.Count > 100, $"only {tests.Count} tests were found");
        Assert.Empty(unbounded);
        Assert.All(tests, test => Assert.InRange(test.Attribute!.Timeout, 1, HangGuard.RepeatedTimeoutMs));
    }
}
