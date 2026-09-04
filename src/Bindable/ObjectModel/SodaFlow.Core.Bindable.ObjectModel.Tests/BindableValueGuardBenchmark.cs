using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     What the binding-thread check costs on a <c>Value</c> access.
/// </summary>
/// <remarks>
///     <para>
///         Categorized rather than asserted on: it has no pass condition, and timing in a build
///         which gates on failure is a way to make the gate flaky. The Test task in build.cake
///         excludes the category, so this does not run in a normal build. Run it when the question
///         comes up:
///     </para>
///     <para>
///         <c>dotnet test src/SodaFlow.slnx --filter TestCategory=Benchmark -l "console;verbosity=detailed"</c>
///     </para>
///     <para>
///         Not <c>[Explicit]</c>, which would read better and does not work: the NUnit adapter
///         pinned here drops explicit tests during discovery, before any filter is applied, so
///         there is no command which runs one.
///     </para>
///     <para>
///         A stopwatch around a warmed loop, which is the harness this repository already uses in
///         SodaFlow.Tests.Performance. It is enough to answer whether the check is noise beside a
///         property access; it is not enough to compare two numbers a few percent apart. Reach for
///         BenchmarkDotNet if a question ever needs that.
///     </para>
/// </remarks>
[TestFixture]
[Category(BenchmarkCategory)]
public class BindableValueGuardBenchmark
{
    /// <summary>Excluded by the Test task in build.cake; name it to run these.</summary>
    private const string BenchmarkCategory = "Benchmark";

    private const int WarmupIterations = 1_000_000;
    private const int MeasuredIterations = 20_000_000;

    /// <summary>A property with nothing in front of it, to size everything else against.</summary>
    private sealed class PlainValue
    {
        internal PlainValue(int value) => this.Value = value;

        internal int Value { get; }
    }

    private static double NanosecondsPerOperation<TTarget>(TTarget target, Func<TTarget, int> operation)
    {
        for (int i = 0; i < WarmupIterations; i++)
        {
            _ = operation(target);
        }

        // Collect before timing rather than during, so a collection triggered by earlier work is
        // not charged to this measurement.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int sink = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < MeasuredIterations; i++)
        {
            // Accumulated so the reads cannot be optimized away as dead.
            sink += operation(target);
        }

        stopwatch.Stop();

        Assert.AreNotEqual(int.MinValue, sink, "keeps the loop alive");

        return stopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / MeasuredIterations;
    }

    private static void Report(string what, double nanoseconds) =>
        TestContext.WriteLine($"{what,-52} {nanoseconds,7:0.000} ns/op");

    [Test]
    public void TheCostOfReadingValue()
    {
        CellSink<int> cell = Cell.CreateSink(1);

        PlainValue plain = new(1);

        // No affinity: IsOnBindingThread is a constant, so this is the floor the guard can reach.
        using IOneWayBindableValue<int> unguarded = cell.ToOneWayImpl(scheduler: BindingScheduler.Immediate);

        // Affinity: a context comparison, and a thread-id comparison behind it.
        using IOneWayBindableValue<int> guarded =
            cell.ToOneWayImpl(scheduler: new SynchronizationContextBindingScheduler(new SynchronizationContext()));

        IBindingScheduler immediate = BindingScheduler.Immediate;
        IBindingScheduler affine = new SynchronizationContextBindingScheduler(new SynchronizationContext());

        Report(
            "plain property, no interface dispatch",
            NanosecondsPerOperation(plain, static p => p.Value));
        Report(
            "IsOnBindingThread, no affinity",
            NanosecondsPerOperation(immediate, static s => s.IsOnBindingThread ? 1 : 0));
        Report(
            "IsOnBindingThread, affine",
            NanosecondsPerOperation(affine, static s => s.IsOnBindingThread ? 1 : 0));
        Report(
            "Value through the interface, no affinity",
            NanosecondsPerOperation(unguarded, static b => b.Value));
        Report(
            "Value through the interface, affine",
            NanosecondsPerOperation(guarded, static b => b.Value));

        TestContext.WriteLine(string.Empty);
        TestContext.WriteLine(
            "The last two are what a binding engine pays per refresh. Compare them against each");
        TestContext.WriteLine(
            "other for the cost of the check, and against the first for how much of a property");
        TestContext.WriteLine(
            "access it is - before the reflection or compiled accessor a binding engine reaches");
        TestContext.WriteLine("this through, which neither number includes.");
    }
}
