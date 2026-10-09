using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Redaction;

public sealed class DebuggerViewTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Assembly SdkAssembly = typeof(SignalGateClient).Assembly;

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Every_member_that_holds_the_key_is_hidden_from_the_debugger()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();
        await client.CheckAsync(StandardEvent.Create());

        var holders = new List<(string Path, bool Hidden)>();
        Walk(client, "client", new HashSet<object>(ReferenceEqualityComparer.Instance), holders);

        Assert.NotEmpty(holders);
        Assert.All(holders, holder => Assert.True(holder.Hidden, holder.Path + " holds the key and is visible in the debugger"));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Every_member_that_holds_the_key_stays_hidden_after_close()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();
        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync();

        var holders = new List<(string Path, bool Hidden)>();
        Walk(client, "client", new HashSet<object>(ReferenceEqualityComparer.Instance), holders);

        Assert.NotEmpty(holders);
        Assert.All(holders, holder => Assert.True(holder.Hidden, holder.Path + " holds the key and is visible in the debugger"));
    }

    // Visits every field and string property of SDK-defined objects reachable from value, and records each
    // member whose string value contains the key, with whether the debugger hides it.
    private static void Walk(object value, string path, HashSet<object> visited, List<(string Path, bool Hidden)> holders)
    {
        if (!visited.Add(value))
        {
            return;
        }

        for (Type? type = value.GetType(); type is not null && type.Assembly == SdkAssembly; type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(InstanceMembers))
            {
                object? fieldValue = field.GetValue(value);
                string fieldPath = path + "." + field.Name;
                if (fieldValue is string text)
                {
                    if (text.Contains(KeyProbe.Key, StringComparison.Ordinal))
                    {
                        holders.Add((fieldPath, IsHidden(field) || IsHiddenBackingField(type, field)));
                    }
                }
                else if (fieldValue is not null && fieldValue.GetType().Assembly == SdkAssembly)
                {
                    Walk(fieldValue, fieldPath, visited, holders);
                }
            }

            foreach (PropertyInfo property in type.GetProperties(InstanceMembers))
            {
                if (property.PropertyType != typeof(string) || property.GetIndexParameters().Length > 0 || property.GetMethod is null)
                {
                    continue;
                }

                if (property.GetValue(value) is string text && text.Contains(KeyProbe.Key, StringComparison.Ordinal))
                {
                    holders.Add((path + "." + property.Name, IsHidden(property)));
                }
            }
        }
    }

    private static bool IsHidden(MemberInfo member)
    {
        return member.GetCustomAttributes<DebuggerBrowsableAttribute>().Any(attribute => attribute.State == DebuggerBrowsableState.Never);
    }

    // A compiler-generated backing field is shown through its property, so it is hidden when the property is.
    private static bool IsHiddenBackingField(Type type, FieldInfo field)
    {
        const string Suffix = ">k__BackingField";
        if (!field.Name.StartsWith('<') || !field.Name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }

        string propertyName = field.Name[1..^Suffix.Length];
        PropertyInfo? property = type.GetProperty(propertyName, InstanceMembers);
        return property is not null && IsHidden(property);
    }
}
