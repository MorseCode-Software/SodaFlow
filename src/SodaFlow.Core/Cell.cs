using System;
using JetBrains.Annotations;

namespace SodaFlow;

internal static class CellInternal
{
    internal static Cell<T> ConstantImpl<T>(T value) => new(value);

    internal static Cell<T> ConstantLazyImpl<T>(LazySample<T> value) =>
        TransactionInternal.Apply(
            code: static (trans, _, value) =>
                StreamInternal.NeverImpl<T>().HoldLazyCellInternal(trans: trans, initialValue: value),
            state: value);

    internal static CellSink<T> CreateSinkImpl<T>(T initialValue) => new(initialValue);

    internal static CellSink<T> CreateSinkImpl<T>(T initialValue, Func<T, T, T> coalesce) =>
        new(initialValue: initialValue, coalesce: coalesce);

    internal static CellStreamSink<T> CreateStreamSinkImpl<T>() => new();

    internal static CellStreamSink<T> CreateStreamSinkImpl<T>(Func<T, T, T> coalesce) => new(coalesce);
}

/// <summary>
///     A value that changes with time in discrete steps.
/// </summary>
/// <remarks>
///     A cell is also a <see cref="Behavior{T}" />. Thus, an operation that takes a
///     behavior also takes a cell.
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
public class Cell<T> : Behavior<T>
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

    internal Cell(T value)
        : base(value)
    {
    }

    internal Cell(Stream<T> stream, T initialValue)
        : base(stream: stream, initialValue: initialValue)
    {
    }

    internal Cell(TransactionInternal trans, Stream<T> stream, LazySample<T> lazyInitialValue)
        : base(trans: trans, stream: stream, lazyInitialValue: lazyInitialValue)
    {
    }

    // SodaFlow creates this in the transaction, thus the test and the set are safe while they
    // hold no lock of their own. The library runs one transaction at a time across the process,
    // thus only one thread can be here. See the remarks on SodaFlow.Transaction.
    internal Stream<T> UpdatesImpl =>
        this.updates
        ?? TransactionInternal.Apply(code: static (trans, _, cell) => cell.CoalescedUpdates(trans), state: this);

    // The updates of a cell, one for each transaction. Updates() gives the updates of the behavior,
    // which are not coalesced.
    internal Stream<T> CoalescedUpdates(TransactionInternal trans) =>
        // ReSharper disable once NonAtomicCompoundOperator - This is fine since the updates field is only
        // set within a Transaction.
        this.updates ??= this.Updates().Coalesce(trans1: trans, f: static (_, right) => right);

    internal Stream<T> ValuesImpl =>
        TransactionInternal.Apply(code: static (trans, _, cell) => cell.Value(trans), state: this);

    internal IStrongListener ListenStrongImpl(Action<T> handler) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.Value(trans).ListenStrongImpl(trans: trans, handler: state.Handler),
            state: (Self: this, Handler: handler));

    internal IWeakListenerInternal ListenImpl(Action<T> handler) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.Value(trans).ListenImpl(trans: trans, handler: state.Handler),
            state: (Self: this, Handler: handler));

    // This hides the map of the behavior, because the map of a cell is a cell.
    internal new Cell<TResult> MapImpl<TResult>(Func<T, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                state.Self.Updates()
                    .MapImpl(state.F)
                    .HoldLazyCellInternal(
                        trans: trans,
                        initialValue: state.Self.SampleLazyMapped(trans: trans, f: state.F)),
            state: (Self: this, F: f));

    internal Cell<TResult> LiftImpl<T2, TResult>(
        Cell<T2> b2,
        Func<T, T2, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                LiftOutput<T, T2, TResult>.Create(trans: trans, b1: state.Self, b2: state.B2, f: state.F)
                    .HoldCell(trans),
            state: (Self: this, B2: b2, F: f));

    internal Cell<TResult> LiftImpl<T2, T3, TResult>(
        Cell<T2> b2,
        Cell<T3> b3,
        Func<T, T2, T3, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                LiftOutput<T, T2, T3, TResult>.Create(trans: trans, b1: state.Self, b2: state.B2, b3: state.B3, f: state.F)
                    .HoldCell(trans),
            state: (Self: this, B2: b2, B3: b3, F: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, TResult>(
        Cell<T2> b2,
        Cell<T3> b3,
        Cell<T4> b4,
        Func<T, T2, T3, T4, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                LiftOutput<T, T2, T3, T4, TResult>.Create(trans: trans, b1: state.Self, b2: state.B2, b3: state.B3, b4: state.B4, f: state.F)
                    .HoldCell(trans),
            state: (Self: this, B2: b2, B3: b3, B4: b4, F: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, TResult>(
        Cell<T2> b2,
        Cell<T3> b3,
        Cell<T4> b4,
        Cell<T5> b5,
        Func<T, T2, T3, T4, T5, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                LiftOutput<T, T2, T3, T4, T5, TResult>.Create(trans: trans, b1: state.Self, b2: state.B2, b3: state.B3, b4: state.B4, b5: state.B5, f: state.F)
                    .HoldCell(trans),
            state: (Self: this, B2: b2, B3: b3, B4: b4, B5: b5, F: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, T6, TResult>(
        Cell<T2> b2,
        Cell<T3> b3,
        Cell<T4> b4,
        Cell<T5> b5,
        Cell<T6> b6,
        Func<T, T2, T3, T4, T5, T6, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                LiftOutput<T, T2, T3, T4, T5, T6, TResult>.Create(trans: trans, b1: state.Self, b2: state.B2, b3: state.B3, b4: state.B4, b5: state.B5, b6: state.B6, f: state.F)
                    .HoldCell(trans),
            state: (Self: this, B2: b2, B3: b3, B4: b4, B5: b5, B6: b6, F: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, T6, T7, TResult>(
        Cell<T2> b2,
        Cell<T3> b3,
        Cell<T4> b4,
        Cell<T5> b5,
        Cell<T6> b6,
        Cell<T7> b7,
        Func<T, T2, T3, T4, T5, T6, T7, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                LiftOutput<T, T2, T3, T4, T5, T6, T7, TResult>.Create(trans: trans, b1: state.Self, b2: state.B2, b3: state.B3, b4: state.B4, b5: state.B5, b6: state.B6, b7: state.B7, f: state.F)
                    .HoldCell(trans),
            state: (Self: this, B2: b2, B3: b3, B4: b4, B5: b5, B6: b6, B7: b7, F: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, T6, T7, T8, TResult>(
        Cell<T2> b2,
        Cell<T3> b3,
        Cell<T4> b4,
        Cell<T5> b5,
        Cell<T6> b6,
        Cell<T7> b7,
        Cell<T8> b8,
        Func<T, T2, T3, T4, T5, T6, T7, T8, TResult> f) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) =>
                LiftOutput<T, T2, T3, T4, T5, T6, T7, T8, TResult>.Create(trans: trans, b1: state.Self, b2: state.B2, b3: state.B3, b4: state.B4, b5: state.B5, b6: state.B6, b7: state.B7, b8: state.B8, f: state.F)
                    .HoldCell(trans),
            state: (Self: this, B2: b2, B3: b3, B4: b4, B5: b5, B6: b6, B7: b7, B8: b8, F: f));

    internal Cell<TResult> ApplyImpl<TResult>(Cell<Func<T, TResult>> bf) =>
        bf.LiftImpl(b2: this, f: static (function, value) => function(value));

    // One transaction for the two samples, the updates, the calm, and the hold. Before, each
    // step opened its own transaction when no transaction was open. One calm then cost four
    // lock acquisitions, four runs of the start hooks, and four transaction objects.
    internal Cell<T> CalmImpl(Func<T, T, bool> areEqual) =>
        TransactionInternal.Apply(
            code: static (trans, _, state) => state.Self.Calm(trans: trans, areEqual: state.AreEqual),
            state: (Self: this, AreEqual: areEqual));

    private Cell<T> Calm(TransactionInternal trans, Func<T, T, bool> areEqual)
    {
        LazySample<T> initA = this.SampleLazy(trans);
        LazySample<MaybeInternal<T>> mInitA = this.SampleLazyMapped(trans: trans, f: MaybeInternal.Some);

        return this.CoalescedUpdates(trans)
            .Calm(trans1: trans, init: mInitA, areEqual: areEqual)
            .HoldLazyCellInternal(trans: trans, initialValue: initA);
    }
}
