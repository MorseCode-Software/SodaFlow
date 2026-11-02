using System;
using System.Threading;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     An object that runs cleanup code when the garbage collector removes it.
/// </summary>
[PublicAPI]
public sealed class Cleanup
{
    // The action, behind a guard that runs it one time. The registry's listener and this object
    // share the guard. The guard does not reference this object or the stream. Thus, the
    // registry keeps only the guard alive, and the stream becomes unreachable with this object,
    // which is what lets the sweep run the action.
    private readonly OnceAction cleanup;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    // ReSharper disable once NotAccessedField.Local
    private Stream<UnitInternal>? stream;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Cleanup" /> class which runs
    ///     <paramref name="cleanup" /> when this object is garbage collected.
    /// </summary>
    /// <param name="cleanup">The action to run when this object becomes unreachable.</param>
    /// <remarks>
    ///     This is finalization and not disposal. The action runs at a time that the
    ///     garbage collector selects. To run it at a known time, call
    ///     <c>CleanupNow</c>. For an object that must be released immediately, use
    ///     <see cref="System.IDisposable" />.
    /// </remarks>
    public Cleanup(Action cleanup)
    {
        this.cleanup = new OnceAction(cleanup);

        Stream<UnitInternal> s = StreamInternal.NeverImpl<UnitInternal>();
        s.AttachListenerInternal(ListenerInternal.CreateFromAction(this.cleanup.Run));

        this.stream = s;
    }

    // The action runs here, one time, and the sweep finds the guard spent. The reference to the
    // stream goes too, thus the registry releases its entry at the next collection.
    internal void CleanupNowImpl()
    {
        this.stream = null;
        this.cleanup.Run();
    }

    // Runs its action one time, on the first of the threads that call it. The sweep thread and a
    // caller of CleanupNow can race, thus the test is atomic.
    private sealed class OnceAction(in Action action)
    {
        private Action? action = action;

        internal void Run() => Interlocked.Exchange(location1: ref this.action, value: null)?.Invoke();
    }
}
