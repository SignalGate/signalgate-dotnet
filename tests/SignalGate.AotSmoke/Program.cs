using System;
using System.Collections.Generic;
using SignalGate.AotSmoke;

// Native AOT smoke test. Publishing compiles the whole library ahead of time; running the binary sends one
// check and one log event through the public API to an in-process handler and verifies the results.
IReadOnlyList<string> failures;
try
{
    failures = await SmokeRun.RunAsync();
}
#pragma warning disable CA1031 // Any failure must end in the failure line and exit code 1.
catch (Exception ex)
#pragma warning restore CA1031
{
    failures = ["unexpected " + ex.GetType().Name + ": " + ex.Message];
}

if (failures.Count > 0)
{
    foreach (string failure in failures)
    {
        Console.WriteLine("aot smoke failed: " + failure);
    }

    return 1;
}

Console.WriteLine("aot smoke ok");
return 0;
