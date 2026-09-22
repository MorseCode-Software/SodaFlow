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
///         Each stream registers a <see cref="StreamListeners" /> that holds a weak handle to
///         that stream. A background thread sweeps the registry. If the garbage collector
///         removed the stream of an entry, the sweep stops the attached listeners. This
///         disconnects the node of that stream from the nodes above it.
///     </para>
///     <para>
///         The garbage collector sets the rate of the sweep, and a timer does not.
///         <see cref="GcSweepTrigger" /> is one object with a finalizer for the complete process. It
///         is not one object for each stream, which is the cost that this method prevents. It
///         starts the sweeper after each collection. It only sends a signal, and the background
///         thread does the sweep. Thus, no code that takes a node lock runs on the finalizer
///         thread, where a block would stop finalization across the process. Without this, the
///         registry kept the data for dead streams until the next cycle of the timer. That
///         measured approximately 10MB after the release of 22,000 streams.
///     </para>
///     <para>
///         The handle is a weak <see cref="GCHandle" /> and not a
///         <see cref="System.WeakReference" />. A WeakReference owns a handle that it must
///         release in a finalizer. Thus, one WeakReference for each stream costs approximately
///         the same as the <c>~Stream</c> finalizer that this method replaced. Both measured
///         approximately 150ns for each stream, against 50ns for the handle. An object with a
///         finalizer for each stream removes this saving.
///     </para>
///     <para>
///         This sweep is not the primary cleanup path and can be slow.
///         <see cref="Stream{T}.Send" /> removes a target with a dead weak reference as it reads
///         the listener set. Thus, it disconnects a node that links to a collected stream at the
///         next firing. The sweep finds the streams that do not fire again.
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
    private static readonly List<StreamListeners> Registry = [];
    private static readonly AutoResetEvent SweepRequested = new(false);

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
    ///     The number of streams in the registry. This is for tests. The number is applicable only
    ///     immediately after a <see cref="Sweep" />.
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

    private static void SodaFlowCleanup()
    {
        while (true)
        {
            // A collection starts this. The backstop interval starts it if no signal
            // arrives.
            SweepRequested.WaitOne(TimedSweepIntervalInMilliseconds);
            Sweep();
        }
        // ReSharper disable once FunctionNeverReturns
    }

    /// <summary>
    ///     This member is internal and not private, thus a test can start a sweep directly and
    ///     does not wait for the interval of the background thread.
    /// </summary>
    internal static void Sweep()
    {
        List<StreamListeners>? collected = null;

        lock (RegistryLock)
        {
            // This moves backwards and puts the last entry into each empty position. Each entry
            // that it moves comes from a position that it read first. Thus, it reads each entry
            // one time.
            for (int i = Registry.Count - 1; i >= 0; i--)
            {
                StreamListeners entry = Registry[i];

                if (entry.IsStreamAlive)
                {
                    continue;
                }

                int last = Registry.Count - 1;

                if (i != last)
                {
                    Registry[i] = Registry[last];
                }

                Registry.RemoveAt(last);

                collected ??= [];
                collected.Add(entry);
            }

            // A List keeps its backing array after a removal. Release that array after a large
            // number of entries becomes small again.
            if (Registry.Capacity > 100 && Registry.Count < Registry.Capacity / 2)
            {
                Registry.TrimExcess();
            }
        }

        // SodaFlow does this release while it holds no registry lock. A stop of a listener
        // gets node locks. A release of unknown listener code while SodaFlow holds the
        // registry lock can cause a lock sequence problem.
        if (collected != null)
        {
            foreach (StreamListeners entry in collected)
            {
                entry.Release();
            }
        }
    }

    /// <summary>
    ///     Asks the cleanup thread to sweep after each garbage collection. The garbage collector
    ///     collects this object, and the object then registers again. There is one instance for
    ///     the complete process.
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

    internal sealed class StreamListeners
    {
        private readonly List<IListenerWithWeakReference> listeners = [];

        // This handle is weak, thus the registry does not keep a stream alive. Release frees
        // it.
        private GCHandle streamHandle;

        public StreamListeners(object stream)
        {
            this.streamHandle = GCHandle.Alloc(value: stream, type: GCHandleType.Weak);

            lock (RegistryLock)
            {
                Registry.Add(this);
            }
        }

        internal bool IsStreamAlive => this.streamHandle is { IsAllocated: true, Target: not null };

        internal void AddListener(IListenerWithWeakReference listener) => this.listeners.Add(listener);

        internal void Release()
        {
            foreach (IListenerWithWeakReference l in this.listeners)
            {
                l.Unlisten();
            }

            this.listeners.Clear();

            if (this.streamHandle.IsAllocated)
            {
                this.streamHandle.Free();
            }
        }
    }
}
