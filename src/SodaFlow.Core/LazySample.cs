using System;
using JetBrains.Annotations;

namespace SodaFlow;

internal static class LazySampleInternal
{
    internal static LazySample<T> DeferImpl<T>(Func<T> f) =>
        f == null ? throw new ArgumentNullException(nameof(f)) : new LazySample<T>(_ => f());

    internal static LazySample<TResult> LiftImpl<T1, T2, TResult>(
        LazySample<T1> a,
        LazySample<T2> b,
        Func<T1, T2, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans => f(arg1: a.Force(trans), arg2: b.Force(trans)));

    internal static LazySample<TResult> LiftImpl<T1, T2, T3, TResult>(
        LazySample<T1> a,
        LazySample<T2> b,
        LazySample<T3> c,
        Func<T1, T2, T3, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans => f(arg1: a.Force(trans), arg2: b.Force(trans), arg3: c.Force(trans)));

    internal static LazySample<TResult> LiftImpl<T1, T2, T3, T4, TResult>(
        LazySample<T1> a,
        LazySample<T2> b,
        LazySample<T3> c,
        LazySample<T4> d,
        Func<T1, T2, T3, T4, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans =>
                f(arg1: a.Force(trans), arg2: b.Force(trans), arg3: c.Force(trans), arg4: d.Force(trans)));
}

/// <summary>
///     A value that SodaFlow reads at the end of the transaction that asked for
///     it. <c>SampleLazy</c> gives one, and <c>HoldLazy</c>, <c>AccumLazy</c>,
///     <c>CollectLazy</c>, and <c>ConstantLazy</c> take one.
/// </summary>
/// <remarks>
///     <para>
///         Only SodaFlow forces a lazy sample, and only in a transaction. Thus, a
///         transaction that fails can forget the value that the sample took. A cell
///         that starts from the sample then reads the committed value. A
///         <see cref="Lazy{T}" /> keeps the first value that it gave, which is why
///         this type exists.
///     </para>
///     <para>
///         A function given to <c>LazySample.Defer</c>, <c>Map</c>, or <c>Lift</c>
///         can run more than one time: in a transaction that fails, and again after
///         it. Thus, the function must be pure, and it must not send into the graph.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class LazySample<T> : IDiscardable
{
    private const string SelfReference =
        "This lazy sample forces itself. The loop that it samples is not closed, or the initial value of a cell reads "
        + "that cell.";

    // The transaction is the one that forces the sample. A sample of a behavior reads the
    // behavior in it, and a sample made from another sample forces that sample in it. Thus, no
    // level of a chain of samples has to find the transaction again.
    private readonly Func<TransactionInternal, T> compute;

    private bool forcing;

    private bool resolved;

    // ReSharper disable once NullableWarningSuppressionIsUsed - Read only while resolved is true, and Capture writes it before it sets resolved.
    private T value = default!;

    internal LazySample(Behavior<T> behavior)
        : this(_ => behavior.SampleNoTransaction())
    {
    }

    internal LazySample(Func<TransactionInternal, T> compute) => this.compute = compute;

    /// <summary>
    ///     Gives the value, in a transaction of its own or in the one that is open on
    ///     this thread.
    /// </summary>
    internal T Force() => TransactionInternal.Apply((trans, _) => this.Force(trans));

    /// <summary>
    ///     Gives the value. Only SodaFlow calls this, in a transaction, which is what
    ///     lets a failed transaction forget the value.
    /// </summary>
    /// <remarks>
    ///     A sample that has a value gives it. A sample that has none computes it: a
    ///     sample of a behavior reads the behavior, and a sample from a function runs
    ///     the function. The sample then keeps the value, thus the function runs one
    ///     time for each value, however many cells start from the sample. The
    ///     transaction that computed the value can fail after this. Then, the sample
    ///     forgets the value and computes it again at the next call.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The sample forces itself.</exception>
    internal T Force(TransactionInternal trans)
    {
        if (this.resolved)
        {
            return this.value;
        }

        if (this.forcing)
        {
            throw new InvalidOperationException(SelfReference);
        }

        this.forcing = true;

        try
        {
            T computed = this.compute(trans);

            this.Capture(trans: trans, captured: computed);

            return computed;
        }
        finally
        {
            this.forcing = false;
        }
    }

    /// <summary>
    ///     Keeps a value until the transaction commits, or forgets it when the
    ///     transaction fails. The sample phase captures the pending value of a
    ///     behavior through this, and <see cref="Force(TransactionInternal)" /> keeps
    ///     what it computed.
    /// </summary>
    internal void Capture(TransactionInternal trans, T captured)
    {
        this.value = captured;
        this.resolved = true;

        trans.Discard(this);
    }

    /// <inheritdoc />
    // The transaction that gave the value failed, thus the next Force reads the committed state.
    void IDiscardable.Discard()
    {
        // ReSharper disable once NullableWarningSuppressionIsUsed - Read only while resolved is true, which the next line clears.
        this.value = default!;
        this.resolved = false;
    }

    internal LazySample<TResult> MapImpl<TResult>(Func<T, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans => f(this.Force(trans)));
}
