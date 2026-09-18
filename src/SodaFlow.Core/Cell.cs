using System;
using JetBrains.Annotations;

namespace SodaFlow;

internal static class CellInternal
{
    internal static Cell<T> ConstantImpl<T>(T value) => new(StreamInternal.NeverImpl<T>().HoldInternal(value));

    internal static Cell<T> ConstantLazyImpl<T>(Lazy<T> value) =>
        TransactionInternal.Apply((trans, _) =>
            new Cell<T>(StreamInternal.NeverImpl<T>().HoldLazyInternal(trans: trans, initialValue: value)));

    internal static CellSink<T> CreateSinkImpl<T>(T initialValue) => new(initialValue);

    internal static CellSink<T> CreateSinkImpl<T>(T initialValue, Func<T, T, T> coalesce) =>
        new(initialValue: initialValue, coalesce: coalesce);

    internal static CellStreamSink<T> CreateStreamSinkImpl<T>() => new();

    internal static CellStreamSink<T> CreateStreamSinkImpl<T>(Func<T, T, T> coalesce) => new(coalesce);
}

/// <summary>
///     A value that changes with time in discrete steps.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
[PublicAPI]
public class Cell<T>
{
    // SodaFlow builds this on first use and does not hold it in a Lazy. A Lazy and its
    // necessary closure are three objects for each cell. SodaFlow makes a cell for each Hold,
    // Map and Lift, and a caller calls Updates or Calm on only a few of them.
    //
    // This field is volatile, because the fast path below reads it when there is no
    // transaction. Thus that reader holds no lock. SodaFlow does the write while it holds the
    // transaction lock. A release without an acquire gives that reader no guarantee. The reader could see the reference from this
    // write and then read the fields of the Stream from a stale cache. This is the usual
    // double-checked locking problem. The x86 and x64 architectures prevent the sequence change
    // that shows it, but arm64 does not. Consumers of net60 and netstandard2.0 run on arm64.
    //
    // A stale null is not a defect, because the reader then takes the slow path and gets the
    // same answer. Volatile prevents the incomplete Stream.
    private volatile Stream<T>? updates;

    internal Cell(Behavior<T> behavior) => this.BehaviorImpl = behavior;

    internal Stream<T> UpdatesImpl
    {
        get
        {
            Stream<T>? existing = this.updates;

            if (existing != null)
            {
                return existing;
            }

            // SodaFlow creates this in the transaction, thus the test and the set are safe
            // while they hold no lock of their own. The library runs one transaction at a time across the
            // process, thus only one thread can be here. See the remarks on
            // SodaFlow.Transaction.
            return TransactionInternal.Apply((trans, _) =>
                // ReSharper disable once NonAtomicCompoundOperator - This is fine since the updates field is only
                // set within a Transaction.
                this.updates ??= this.BehaviorImpl.Updates().Coalesce(trans1: trans, f: static (_, right) => right));
        }
    }

    internal Stream<T> ValuesImpl => TransactionInternal.Apply((trans, _) => this.BehaviorImpl.Value(trans));

    internal Behavior<T> BehaviorImpl { get; }

    internal T SampleImpl() => this.BehaviorImpl.SampleImpl();

    internal Lazy<T> SampleLazyImpl() => this.BehaviorImpl.SampleLazyImpl();

    internal IStrongListener ListenStrongImpl(Action<T> handler) =>
        TransactionInternal.Apply((trans, _) => this.BehaviorImpl.Value(trans).ListenStrongImpl(handler));

    internal IWeakListener ListenImpl(Action<T> handler) =>
        TransactionInternal.Apply((trans, _) => this.BehaviorImpl.Value(trans).ListenImpl(handler));

    internal Cell<TResult> MapImpl<TResult>(Func<T, TResult> f) => new(this.BehaviorImpl.MapImpl(f));

    internal Cell<TResult> LiftImpl<T2, TResult>(Cell<T2> b2, Func<T, T2, TResult> f) =>
        new(this.BehaviorImpl.LiftImpl(b2: b2.BehaviorImpl, f: f));

    internal Cell<TResult> LiftImpl<T2, T3, TResult>(Cell<T2> b2, Cell<T3> b3, Func<T, T2, T3, TResult> f) =>
        new(this.BehaviorImpl.LiftImpl(b2: b2.BehaviorImpl, b3: b3.BehaviorImpl, f: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, TResult>(
        Cell<T2> b2,
        Cell<T3> b3,
        Cell<T4> b4,
        Func<T, T2, T3, T4, TResult> f) =>
        new(this.BehaviorImpl.LiftImpl(b2: b2.BehaviorImpl, b3: b3.BehaviorImpl, b4: b4.BehaviorImpl, f: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, TResult>(
        Cell<T2> b2,
        Cell<T3> b3,
        Cell<T4> b4,
        Cell<T5> b5,
        Func<T, T2, T3, T4, T5, TResult> f) =>
        new(
            this.BehaviorImpl.LiftImpl(
                b2: b2.BehaviorImpl,
                b3: b3.BehaviorImpl,
                b4: b4.BehaviorImpl,
                b5: b5.BehaviorImpl,
                f: f));

    internal Cell<TResult> LiftImpl<T2, T3, T4, T5, T6, TResult>(
        Cell<T2> b2,
        Cell<T3> b3,
        Cell<T4> b4,
        Cell<T5> b5,
        Cell<T6> b6,
        Func<T, T2, T3, T4, T5, T6, TResult> f) =>
        new(
            this.BehaviorImpl.LiftImpl(
                b2: b2.BehaviorImpl,
                b3: b3.BehaviorImpl,
                b4: b4.BehaviorImpl,
                b5: b5.BehaviorImpl,
                b6: b6.BehaviorImpl,
                f: f));

    internal Cell<TResult> ApplyImpl<TResult>(Cell<Func<T, TResult>> bf) =>
        new(this.BehaviorImpl.ApplyImpl(bf.BehaviorImpl));

    internal Cell<T> CalmImpl(Func<T, T, bool> areEqual)
    {
        Lazy<T> initA = this.BehaviorImpl.SampleLazyImpl();
        Lazy<MaybeInternal<T>> mInitA = initA.MapImpl(MaybeInternal.Some);
        return this.UpdatesImpl.Calm(init: mInitA, areEqual: areEqual).HoldLazyImpl(initA);
    }
}
