// A faulted task that no code observes raises TaskScheduler.UnobservedTaskException when the garbage
// collector finalizes it. On .NET Framework with ThrowUnobservedTaskExceptions on, that ends the
// process. On .NET 5 and above, nothing more occurs. The libraries must never make such a task.
// Thus, each test project links this file and fails each test that makes one.
//
// The event fires only at finalization. Thus, after each test, this collects the garbage and waits
// for the finalizers. Without that, a run can end before the event fires. Tests run one at a time,
// as TestParallelism.cs gives. Thus, the test that ran last is the one that made the task.
//
// ThrowUnobservedTaskExceptions cannot do this work in a test process. TUnit and
// Microsoft.Testing.Platform each listen to the event before this file does, and each calls
// SetObserved. A test on .NET Framework with that value on showed this. The event came here with
// Observed true, and the process continued. Thus, this file is the check on the two runtimes.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using TUnit.Core;

namespace SodaFlow.Tests.Support;

[UsedImplicitly]
internal static class UnobservedTaskExceptions
{
    private static readonly ConcurrentQueue<Exception> Seen = new();

    // The name of the test that runs now. The finalizer thread reads it.
    private static volatile string? currentTest;

    static UnobservedTaskExceptions() => TaskScheduler.UnobservedTaskException += Record;

    [BeforeEvery(HookType.Test)]
    [UsedImplicitly]
    public static void Start(TestContext context) => currentTest = context.Metadata.DisplayName;

    [AfterEvery(HookType.Test)]
    [UsedImplicitly]
    public static void Check(TestContext context)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        List<Exception> found = [];

        while (Seen.TryDequeue(out Exception? error))
        {
            found.Add(error);
        }

        if (found.Count > 0)
        {
            throw new AggregateException(
                message: $"{context.Metadata.DisplayName} left a faulted task that no code observed.",
                innerExceptions: found);
        }
    }

    private static void Record(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Console.Error.WriteLine($"Unobserved task exception after {currentTest}: {e.Exception}");
        Seen.Enqueue(e.Exception);
    }
}
