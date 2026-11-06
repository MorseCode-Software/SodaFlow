using System;
using JetBrains.Annotations;

namespace SodaFlow;

internal static class LazySampleInternal
{
    internal static LazySample<T> DeferImpl<T>(Func<T> f) =>
        f == null ? throw new ArgumentNullException(nameof(f)) : new LazySample<T>(_ => f());

    internal static LazySample<TResult> LiftImpl<T1, T2, TResult>(
        LazySample<T1> sample1,
        LazySample<T2> sample2,
        Func<T1, T2, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans => f(arg1: sample1.Force(trans), arg2: sample2.Force(trans)));

    internal static LazySample<TResult> LiftImpl<T1, T2, T3, TResult>(
        LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        Func<T1, T2, T3, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans =>
                f(
                    arg1: sample1.Force(trans),
                    arg2: sample2.Force(trans),
                    arg3: sample3.Force(trans)));

    internal static LazySample<TResult> LiftImpl<T1, T2, T3, T4, TResult>(
        LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        Func<T1, T2, T3, T4, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans =>
                f(
                    arg1: sample1.Force(trans),
                    arg2: sample2.Force(trans),
                    arg3: sample3.Force(trans),
                    arg4: sample4.Force(trans)));

    internal static LazySample<TResult> LiftImpl<T1, T2, T3, T4, T5, TResult>(
        LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        LazySample<T5> sample5,
        Func<T1, T2, T3, T4, T5, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans =>
                f(
                    arg1: sample1.Force(trans),
                    arg2: sample2.Force(trans),
                    arg3: sample3.Force(trans),
                    arg4: sample4.Force(trans),
                    arg5: sample5.Force(trans)));

    internal static LazySample<TResult> LiftImpl<T1, T2, T3, T4, T5, T6, TResult>(
        LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        LazySample<T5> sample5,
        LazySample<T6> sample6,
        Func<T1, T2, T3, T4, T5, T6, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans =>
                f(
                    arg1: sample1.Force(trans),
                    arg2: sample2.Force(trans),
                    arg3: sample3.Force(trans),
                    arg4: sample4.Force(trans),
                    arg5: sample5.Force(trans),
                    arg6: sample6.Force(trans)));

    internal static LazySample<TResult> LiftImpl<T1, T2, T3, T4, T5, T6, T7, TResult>(
        LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        LazySample<T5> sample5,
        LazySample<T6> sample6,
        LazySample<T7> sample7,
        Func<T1, T2, T3, T4, T5, T6, T7, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans =>
                f(
                    arg1: sample1.Force(trans),
                    arg2: sample2.Force(trans),
                    arg3: sample3.Force(trans),
                    arg4: sample4.Force(trans),
                    arg5: sample5.Force(trans),
                    arg6: sample6.Force(trans),
                    arg7: sample7.Force(trans)));

    internal static LazySample<TResult> LiftImpl<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
        LazySample<T1> sample1,
        LazySample<T2> sample2,
        LazySample<T3> sample3,
        LazySample<T4> sample4,
        LazySample<T5> sample5,
        LazySample<T6> sample6,
        LazySample<T7> sample7,
        LazySample<T8> sample8,
        Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> f) =>
        f == null
            ? throw new ArgumentNullException(nameof(f))
            : new LazySample<TResult>(trans =>
                f(
                    arg1: sample1.Force(trans),
                    arg2: sample2.Force(trans),
                    arg3: sample3.Force(trans),
                    arg4: sample4.Force(trans),
                    arg5: sample5.Force(trans),
                    arg6: sample6.Force(trans),
                    arg7: sample7.Force(trans),
                    arg8: sample8.Force(trans)));
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
public sealed class LazySample<T> : ILastAction, ISampleAction
{
    private const string SelfReference =
        "This lazy sample forces itself. The loop that it samples is not closed, or the initial value of a cell reads "
        + "that cell.";

    // One of the two is set. A sample of a behavior reads through the source, which is the
    // behavior itself or one small object for a map. A sample from a function, a map on a
    // sample, or a lift of samples runs the function. The transaction is the one that forces the
    // sample. A sample made from another sample forces that sample in it. Thus, no level of a
    // chain of samples has to find the transaction again.
    private readonly Func<TransactionInternal, T>? compute;
    private readonly ISampleSource<T>? source;

    private bool forcing;

    private bool resolved;

    // ReSharper disable once NullableWarningSuppressionIsUsed - Read only while resolved is true, and Capture writes it before it sets resolved.
    private T value = default!;

    internal LazySample(ISampleSource<T> source) => this.source = source;

    internal LazySample(Func<TransactionInternal, T> compute) => this.compute = compute;

    /// <summary>
    ///     Gives the value, in a transaction of its own or in the one that is open on
    ///     this thread.
    /// </summary>
    internal T Force() =>
        TransactionInternal.Apply(code: static (trans, _, sample) => sample.Force(trans), state: this);

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
            T computed = this.Read(trans);

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

        trans.Last(this);
    }

    /// <inheritdoc />
    // The sample phase of the transaction that asked for this sample. The source gives the value
    // of the behavior at the end of that transaction. The function of a mapped source runs here,
    // and a transaction must not change the internal parts of SodaFlow from it. Thus, a send
    // from the function throws, as it did when the function ran in a force.
    void ISampleAction.Sample(TransactionInternal trans)
    {
        trans.InCallback++;

        try
        {
            this.Capture(trans: trans, captured: this.Read(trans));
        }
        finally
        {
            trans.InCallback--;
        }
    }

    private T Read(TransactionInternal trans) =>
        // ReSharper disable once NullableWarningSuppressionIsUsed - The constructor that sets no source sets compute.
        this.source != null ? this.source.Read() : this.compute!(trans);

    /// <inheritdoc />
    // The value stays. A commit changes nothing, and the entry exists for the discard.
    void ILastAction.Commit()
    {
    }

    /// <inheritdoc />
    // The transaction that gave the value failed, thus the next Force reads the committed state.
    void ILastAction.Discard()
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

/// <summary>
///     What a lazy sample of a behavior reads: the behavior, or the behavior through
///     a function. The read sees the pending update of the transaction.
/// </summary>
internal interface ISampleSource<out T>
{
    T Read();
}

// A sample of a behavior through a function, as one object. Before, this was a sample of the
// behavior and a map on it: two samples, each with its own closure and delegate.
internal sealed class MappedSampleSource<TSource, T>(in Behavior<TSource> behavior, in Func<TSource, T> f)
    : ISampleSource<T>
{
    private readonly Behavior<TSource> behavior = behavior;
    private readonly Func<TSource, T> f = f;

    public T Read() => this.f(this.behavior.PendingOrCurrent());
}
