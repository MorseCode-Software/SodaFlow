using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Time;

/// <summary>
///     A timer system built on an <see cref="ITimerSystemImplementation{T}" />,
///     which supplies the clock and the wait mechanism. This class gives the FRP.
/// </summary>
/// <typeparam name="T">The type used to express a point in time.</typeparam>
/// <remarks>
///     Alarms get to the graph through a <c>Transaction.OnStart</c> handler
///     installed by the constructor. When a transaction starts, the handler reads
///     the current time, runs any timer that has come due, and sends the alarms.
///     The handler sends events that became due at the same time delivered
///     together, and events at different times in different transactions.
///     Use <see cref="SystemClockTimerSystem" /> or
///     <see cref="SecondsTimerSystem" /> unless a different clock is
///     necessary, and then derive from
///     <see cref="TimerSystemImplementationBase{T}" /> and give it here.
/// </remarks>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class TimerSystem<T> : ITimerSystem<T>
    where T : IComparable<T>
{
    // The timer system itself is in Core, and the F# package uses the same one. This class gives
    // its public surface, with Maybe as the optional type.
    private readonly TimerSystemInternal<T, ITimer> timerSystem;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TimerSystem{T}" /> class over
    ///     the given implementation, starting it immediately.
    /// </summary>
    /// <param name="implementation">The clock and waiting mechanism to build on.</param>
    /// <param name="handleException">
    ///     Called with each exception from a wait for a timer, and from a timer that
    ///     fires.
    /// </param>
    /// <remarks>
    ///     Construction starts the implementation and installs a transaction handler.
    ///     The handler runs at the start of each transaction in the process until
    ///     <see cref="Dispose" /> removes it. Thus, make one timer system for the
    ///     process, or dispose each one when its work is complete.
    /// </remarks>
    public TimerSystem(ITimerSystemImplementation<T> implementation, Action<Exception> handleException) =>
        this.timerSystem =
            new TimerSystemInternal<T, ITimer>(
                implementation: new Implementation(implementation),
                comparer: Comparer<T>.Default,
                handleException: handleException);

    /// <inheritdoc />
    public Behavior<T> Time => this.timerSystem.Time;

    /// <summary>
    ///     Removes the transaction handler, and stops the implementation. After
    ///     this, <see cref="Time" /> does not move, and no alarm fires.
    /// </summary>
    /// <remarks>
    ///     This drops an event that is due and not sent. A second call does nothing.
    ///     A call from the listener of an alarm is safe, because the implementation
    ///     does not wait for its own thread.
    /// </remarks>
    // ReSharper disable once InheritdocConsiderUsage
    public void Dispose()
    {
        this.timerSystem.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public Stream<T> At(Cell<Maybe<T>> t) =>
        this.timerSystem.At(
            deadline: t,
            read: static m => m.Match(onSome: MaybeInternal.Some, onNone: static () => MaybeInternal<T>.None));

    // Passes each member that the Core timer system uses to the public implementation.
    private sealed class Implementation(in ITimerSystemImplementation<T> implementation)
        : ITimerSystemImplementationInternal<T, ITimer>
    {
        private readonly ITimerSystemImplementation<T> implementation = implementation;

        public T Now => this.implementation.Now;

        public void Start(Action<Exception> handleException) => this.implementation.Start(handleException);

        public void Stop() => this.implementation.Stop();

        public ITimer SetTimer(T time, Action callback) =>
            this.implementation.SetTimer(time: time, callback: callback);

        public void Cancel(ITimer timer) => timer.Cancel();

        public void RunTimersTo(T time) => this.implementation.RunTimersTo(time);
    }
}
