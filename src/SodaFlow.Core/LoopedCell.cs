using System;
using JetBrains.Annotations;

namespace SodaFlow;

/// <summary>
///     A value that changes with time in discrete steps, which can be in a loop that
///     is not closed. It has each operation of a <see cref="Cell{T}" /> except a
///     read of the current value.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <remarks>
///     <para>
///         A loop block gives one of these as the placeholder. An
///         operation that starts its result from the current value of a looped cell
///         also gives one, because that value is not available before the loop closes.
///         An operation that gives a stream, or a cell with a value from the caller,
///         gives the plain type. Thus, the type says which values a caller cannot
///         read now.
///     </para>
///     <para>
///         A <see cref="Cell{T}" /> is a looped cell that has a value now, and it adds
///         the read. The <see cref="LoopCloser" /> that a loop gives when it closes
///         gives the cell back, and <c>AsCell</c> asserts it before the close.
///     </para>
/// </remarks>
[PublicAPI]
public class LoopedCell<T>
{
    // SodaFlow builds this on first use and does not hold it in a Lazy. A Lazy and its
    // necessary closure are three objects for each cell. SodaFlow makes a cell for each Hold,
    // Map, and Lift, and a caller calls Updates or Calm on only a few of them.
    //
    // This field is volatile, because the fast path below reads it when there is no
    // transaction. Thus, that reader holds no lock. SodaFlow does the write while it holds the
    // transaction lock. A release without an acquire gives that reader no guarantee. The reader could see the reference from this
    // write and then read the fields of the Stream from a stale cache. This is the usual
    // double-checked locking problem. The x86 and x64 architectures prevent the sequence change
    // that shows it, but arm64 does not. Consumers of net6.0 and netstandard2.0 run on arm64.
    //
    // A stale null is not a defect, because the reader then takes the slow path and gets the
    // same answer. Volatile prevents the incomplete Stream.
    private volatile Stream<T>? updates;

    internal LoopedCell(LoopedBehavior<T> behavior) => this.BehaviorImpl = behavior;

    // SodaFlow creates this in the transaction, thus the test and the set are safe while they
    // hold no lock of their own. The library runs one transaction at a time across the process,
    // thus only one thread can be here. See the remarks on SodaFlow.Transaction.
    internal Stream<T> UpdatesImpl =>
        this.updates ?? TransactionInternal.Apply(code: static (trans, _, cell) => cell.Updates(trans), state: this);

    internal Stream<T> Updates(TransactionInternal trans) =>
        // ReSharper disable once NonAtomicCompoundOperator - This is fine since the updates field is only
        // set within a Transaction.
        this.updates ??= this.BehaviorImpl.Updates().Coalesce(trans1: trans, f: static (_, right) => right);

    internal Stream<T> ValuesImpl =>
        TransactionInternal.Apply(code: static (trans, _, cell) => cell.BehaviorImpl.Value(trans), state: this);

    internal LoopedBehavior<T> BehaviorImpl { get; }

    internal T SampleImpl() => this.BehaviorImpl.SampleImpl();

    internal LazySample<T> SampleLazyImpl() => this.BehaviorImpl.SampleLazyImpl();

    internal LazySample<TResult> SampleLazyMappedImpl<TResult>(Func<T, TResult> f) =>
        this.BehaviorImpl.SampleLazyMappedImpl(f);

    internal IStrongListener ListenStrongImpl(Action<T> handler) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.BehaviorImpl.Value(trans).ListenStrongImpl(trans: trans, handler: state.Handler),
            state: (Self: this, Handler: handler));

    internal IWeakListener ListenImpl(Action<T> handler) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.BehaviorImpl.Value(trans).ListenImpl(trans: trans, handler: state.Handler),
            state: (Self: this, Handler: handler));

    /// <summary>
    ///     Gives this cell as a <see cref="Cell{T}" />, and does not check that its
    ///     loops are closed.
    /// </summary>
    /// <remarks>
    ///     Each cell that an operation makes is a <see cref="Cell{T}" />, thus this
    ///     usually gives the same object. Only a placeholder is not one, and for it
    ///     this makes a cell over the same value.
    /// </remarks>
    internal Cell<T> AsCellImpl() => this as Cell<T> ?? new Cell<T>(this.BehaviorImpl.AsBehaviorImpl());

    internal Cell<TResult> MapImpl<TResult>(Func<T, TResult> f) => new(this.BehaviorImpl.MapImpl(f));

    internal Cell<TResult> LiftImpl<T2, TResult>(LoopedCell<T2> b2, Func<T, T2, TResult> f) =>
        new(this.BehaviorImpl.LiftImpl(b2: b2.BehaviorImpl, f: f));

    internal Cell<TResult> LiftImpl<T2, T3, TResult>(
        LoopedCell<T2> b2,
        LoopedCell<T3> b3,
        Func<T, T2, T3, TResult> f) =>
        new(this.BehaviorImpl.LiftImpl(b2: b2.BehaviorImpl, b3: b3.BehaviorImpl, f: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, TResult>(
        LoopedCell<T2> b2,
        LoopedCell<T3> b3,
        LoopedCell<T4> b4,
        Func<T, T2, T3, T4, TResult> f) =>
        new(this.BehaviorImpl.LiftImpl(b2: b2.BehaviorImpl, b3: b3.BehaviorImpl, b4: b4.BehaviorImpl, f: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, TResult>(
        LoopedCell<T2> b2,
        LoopedCell<T3> b3,
        LoopedCell<T4> b4,
        LoopedCell<T5> b5,
        Func<T, T2, T3, T4, T5, TResult> f) =>
        new(
            this.BehaviorImpl.LiftImpl(
                b2: b2.BehaviorImpl,
                b3: b3.BehaviorImpl,
                b4: b4.BehaviorImpl,
                b5: b5.BehaviorImpl,
                f: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, T6, TResult>(
        LoopedCell<T2> b2,
        LoopedCell<T3> b3,
        LoopedCell<T4> b4,
        LoopedCell<T5> b5,
        LoopedCell<T6> b6,
        Func<T, T2, T3, T4, T5, T6, TResult> f) =>
        new(
            this.BehaviorImpl.LiftImpl(
                b2: b2.BehaviorImpl,
                b3: b3.BehaviorImpl,
                b4: b4.BehaviorImpl,
                b5: b5.BehaviorImpl,
                b6: b6.BehaviorImpl,
                f: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, T6, T7, TResult>(
        LoopedCell<T2> b2,
        LoopedCell<T3> b3,
        LoopedCell<T4> b4,
        LoopedCell<T5> b5,
        LoopedCell<T6> b6,
        LoopedCell<T7> b7,
        Func<T, T2, T3, T4, T5, T6, T7, TResult> f) =>
        new(
            this.BehaviorImpl.LiftImpl(
                b2: b2.BehaviorImpl,
                b3: b3.BehaviorImpl,
                b4: b4.BehaviorImpl,
                b5: b5.BehaviorImpl,
                b6: b6.BehaviorImpl,
                b7: b7.BehaviorImpl,
                f: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, T6, T7, T8, TResult>(
        LoopedCell<T2> b2,
        LoopedCell<T3> b3,
        LoopedCell<T4> b4,
        LoopedCell<T5> b5,
        LoopedCell<T6> b6,
        LoopedCell<T7> b7,
        LoopedCell<T8> b8,
        Func<T, T2, T3, T4, T5, T6, T7, T8, TResult> f) =>
        new(
            this.BehaviorImpl.LiftImpl(
                b2: b2.BehaviorImpl,
                b3: b3.BehaviorImpl,
                b4: b4.BehaviorImpl,
                b5: b5.BehaviorImpl,
                b6: b6.BehaviorImpl,
                b7: b7.BehaviorImpl,
                b8: b8.BehaviorImpl,
                f: f));

    internal Cell<TResult> ApplyImpl<TResult>(LoopedCell<Func<T, TResult>> bf) =>
        new(this.BehaviorImpl.ApplyImpl(bf.BehaviorImpl));

    // One transaction for the two samples, the updates, the calm, and the hold. Before, each
    // step opened its own transaction when no transaction was open. One calm then cost four
    // lock acquisitions, four runs of the start hooks, and four transaction objects.
    internal Cell<T> CalmImpl(Func<T, T, bool> areEqual) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) => state.Self.Calm(trans: trans, areEqual: state.AreEqual),
            state: (Self: this, AreEqual: areEqual));

    private Cell<T> Calm(TransactionInternal trans, Func<T, T, bool> areEqual)
    {
        LazySample<T> initA = this.BehaviorImpl.SampleLazy(trans);

        LazySample<MaybeInternal<T>> mInitA =
            this.BehaviorImpl.SampleLazyMapped(trans: trans, f: MaybeInternal.Some);

        return new Cell<T>(
            this.Updates(trans)
                .Calm(trans1: trans, init: mInitA, areEqual: areEqual)
                .HoldLazyInternal(trans: trans, initialValue: initA));
    }
}
