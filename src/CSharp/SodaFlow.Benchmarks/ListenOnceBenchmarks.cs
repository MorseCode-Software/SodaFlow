using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace SodaFlow.Benchmarks;

/// <summary>
///     What it costs to attach a one-shot listener, and to attach one and fire it.
/// </summary>
/// <remarks>
///     <para>
///         ListenOnce opens a transaction and gives the listener its handle
///         before that transaction closes. It gives that transaction to Listen,
///         thus Listen does not open a second one. These benchmarks measure that
///         cost. A plain Listen is the baseline.
///     </para>
///     <para>
///         In an open transaction, ListenOnce uses that transaction. The attach
///         and the send is the full life of a one-shot listener.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net472)]
[SimpleJob(RuntimeMoniker.Net10_0)]
// Not sealed, and not private to this file, at each report from the inspections.
// BenchmarkDotNet generates a class that derives from this one and finds it by reflection.
// Thus, "has no inheritors" is not true, and "nothing here uses it" is not true. A seal fails at run
// time and not at build time, with "Declaring type must be unsealed", which is a poor
// procedure to find this.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class ListenOnceBenchmarks
{
    private static readonly Action<int> Handler = static _ =>
    {
    };

    private readonly StreamSink<int> sink = Stream.CreateSink<int>();

    /// <summary>
    ///     Attaches a plain listener and removes it.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Listen, attach and Unlisten")]
    public void Listen() => this.sink.Listen(Handler).Unlisten();

    /// <summary>
    ///     Attaches a one-shot listener and removes it before it fires.
    /// </summary>
    [Benchmark(Description = "ListenOnce, attach and Unlisten")]
    public void ListenOnce() => this.sink.ListenOnce(Handler).Unlisten();

    /// <summary>
    ///     Attaches a one-shot listener in an open transaction, which the listener
    ///     then uses.
    /// </summary>
    [Benchmark(Description = "ListenOnce in a transaction, attach and Unlisten")]
    public void ListenOnceInATransaction() =>
        Transaction.RunVoid(() => this.sink.ListenOnce(Handler).Unlisten());

    /// <summary>
    ///     Attaches a one-shot listener and sends one value, which removes it.
    /// </summary>
    [Benchmark(Description = "ListenOnce, attach and Send")]
    public void ListenOnceAndSend()
    {
        // The local keeps the listener until the send. Without it, a collection between the two
        // calls can remove the listener, and the send then measures a stream with no listener.
        IListener listener = this.sink.ListenOnce(Handler);
        this.sink.Send(1);
        GC.KeepAlive(listener);
    }
}
