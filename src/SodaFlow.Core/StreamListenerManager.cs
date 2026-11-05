using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace SodaFlow;

/// <summary>
///     Cleans up after the streams that are no longer in scope.
/// </summary>
/// <remarks>
///     <para>
///         A stream registers its node when a caller attaches the first listener
///         to it. The node holds a weak handle to that stream and the weak views
///         of the attached listeners. A background thread sweeps the registry. If the
///         garbage collector removed the stream of a node, the sweep stops those
///         listeners. This disconnects the node from the nodes above it. A stream
///         with no attached listener has nothing to stop, thus it does not
///         register.
///     </para>
///     <para>
///         The garbage collector sets the rate of the sweep, and a timer does not.
///         <see cref="GcSweepTrigger" /> is one object with a finalizer for the
///         complete process. It is not one object for each stream, which is the
///         cost that this method prevents. It starts the sweeper after each
///         collection. It only sends a signal, and the background thread does the
///         sweep. Thus, no code that takes a node lock runs on the finalizer
///         thread, where a block would stop finalization across the process.
///         Without this, the registry kept the data for dead streams until the
///         next cycle of the timer. That measured approximately 10MB after the
///         release of 22,000 streams.
///     </para>
///     <para>
///         The registry keeps its entries by generation, and the sweep after a
///         collection of generation N examines generations 0 to N only. See
///         <see cref="GenerationalRegistry" />. Thus, the sweep after a young
///         collection, which is the frequent one, does not walk the live graph.
///     </para>
///     <para>
///         The handle is a weak <see cref="GCHandle" /> and not a
///         <see cref="System.WeakReference" />. A WeakReference owns a handle that
///         it must release in a finalizer. Thus, one WeakReference for each stream
///         costs approximately the same as the <c>~Stream</c> finalizer that this
///         method replaced. Both measured approximately 150ns for each stream,
///         against 50ns for the handle. An object with a finalizer for each stream
///         removes this saving.
///     </para>
///     <para>
///         This sweep is not the primary cleanup path and can be slow.
///         <see cref="Stream{T}.Send" /> removes a target with a dead weak
///         reference as it reads the listener set. Thus, it disconnects a node
///         that links to a collected stream at the next firing. The sweep finds
///         the streams that do not fire again.
///     </para>
/// </remarks>
internal static class StreamListenerManager
{
    // This interval is long, because it is only a backstop. SodaFlow can remove an entry when
    // the garbage collector collects a stream, and each collection signals a sweep. Thus, in a
    // correct process this interval finds no work. It is for the condition when the signal
    // stops. Other code that blocks the finalizer thread is the most probable cause. The
    // sweeper thread is not affected and can continue.
    private const int TimedSweepIntervalInMilliseconds = 300000;

    private static readonly object RegistryLock = new();
    private static readonly GenerationalRegistry Registry = new(getGeneration: GC.GetGeneration);
    private static readonly AutoResetEvent SweepRequested = new(false);

    // The collection count of each generation at the last signaled sweep. Only the cleanup
    // thread reads and writes this.
    private static readonly int[] CollectionCounts = new int[GC.MaxGeneration + 1];

    static StreamListenerManager()
    {
        Thread cleanupThread = new(SodaFlowCleanup) { Name = "SodaFlow Cleanup Thread", IsBackground = true };

        cleanupThread.Start();

        // SodaFlow does not keep this object. It must be unreachable to let its finalizer
        // run.
        // ReSharper disable once ObjectCreationAsStatement
        new GcSweepTrigger();
    }

    /// <summary>
    ///     The number of streams in the registry. This is for tests. The number is
    ///     applicable only immediately after a <see cref="Sweep()" />.
    /// </summary>
    internal static int RegistryCount
    {
        get
        {
            lock (RegistryLock)
            {
                return Registry.Count;
            }
        }
    }

    internal static void Register(IEntry entry)
    {
        lock (RegistryLock)
        {
            Registry.Add(entry);
        }
    }

    private static void SodaFlowCleanup()
    {
        while (true)
        {
            // A collection starts this. The backstop interval starts it if no signal
            // arrives, and that sweep examines all generations.
            bool signaled = SweepRequested.WaitOne(TimedSweepIntervalInMilliseconds);

            Sweep(
                collectedGeneration: signaled
                    ? CollectedGeneration(lastCounts: CollectionCounts, collectionCount: GC.CollectionCount)
                    : GC.MaxGeneration);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    /// <summary>
    ///     Sweeps all generations. This member is internal and not private. Thus, a
    ///     test can start a sweep directly and does not wait for the background
    ///     thread.
    /// </summary>
    internal static void Sweep() => Sweep(collectedGeneration: GC.MaxGeneration);

    /// <summary>
    ///     Sweeps generations 0 to <paramref name="collectedGeneration" />, which is
    ///     the sweep after a collection of that generation.
    /// </summary>
    private static void Sweep(int collectedGeneration)
    {
        List<IEntry>? collected;

        lock (RegistryLock)
        {
            collected = Registry.Sweep(collectedGeneration);
        }

        // SodaFlow does this release while it holds no registry lock. A stop of a listener
        // gets node locks. A release of unknown listener code while SodaFlow holds the
        // registry lock can cause a lock sequence problem.
        if (collected != null)
        {
            foreach (IEntry entry in collected)
            {
                entry.Release();
            }
        }
    }

    /// <summary>
    ///     The highest generation that a collection reached since the last call, read
    ///     from the change in the collection counts. Writes the new counts to
    ///     <paramref name="lastCounts" />.
    /// </summary>
    internal static int CollectedGeneration(int[] lastCounts, Func<int, int> collectionCount)
    {
        int collected = 0;

        for (int generation = 1; generation < lastCounts.Length; generation++)
        {
            int count = collectionCount(generation);

            if (count != lastCounts[generation])
            {
                lastCounts[generation] = count;
                collected = generation;
            }
        }

        return collected;
    }

    /// <summary>
    ///     Asks the cleanup thread to sweep after each garbage collection. The garbage
    ///     collector collects this object, and the object then registers again. There
    ///     is one instance for the complete process.
    /// </summary>
    private sealed class GcSweepTrigger
    {
        ~GcSweepTrigger()
        {
            try
            {
                SweepRequested.Set();
            }
            catch
            {
                // A finalizer must not throw. If the signal fails, no correction is possible,
                // because the cycle of the timer does the work.
            }
            finally
            {
                if (!Environment.HasShutdownStarted && !AppDomain.CurrentDomain.IsFinalizingForUnload())
                {
                    // This makes a new instance and does not call
                    // GC.ReRegisterForFinalize(this). A second life for this object moves it to
                    // a previous generation. Then a young collection does not find it, and only a
                    // collection of generation 1 or higher starts the sweep. A new object
                    // starts in generation 0, thus each collection sets the rate.
                    // ReSharper disable once ObjectCreationAsStatement
                    new GcSweepTrigger();
                }
            }
        }
    }

    // The node of a stream implements this. See StreamNode<T>.
    internal interface IEntry
    {
        bool IsStreamAlive { get; }

        void Release();
    }
}
