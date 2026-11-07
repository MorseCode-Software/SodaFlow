using System;

namespace SodaFlow;

/// <summary>
///     The output of a merge of maybes, and its gate. A subclass for each arity
///     holds the captures of the inputs and the function.
/// </summary>
/// <remarks>
///     <para>
///         A merge of maybes uses the mechanism of
///         <see cref="LiftOutput{TResult}" />. Its inputs are streams, and not the
///         updates of behaviors. Each input captures its firing and calls the
///         gate. The gate queues its action at the output node, which ranks above
///         each input. Thus, the action runs after all the inputs that fire in the
///         transaction, and it makes one output firing from their captured values.
///         An input that does not fire gives an empty value. An input that fires
///         more than one time in a transaction gives the value of its last firing,
///         as a lifted input does.
///     </para>
///     <para>
///         This is less expensive than a merge of mapped streams. Each merge of two
///         streams makes a stream, a gate, and a node link, and each input must
///         have its own map. Also, the inputs have no left and no right, thus no
///         input has priority over the other inputs. Before, a function made the
///         links of the captures into an array, and a second function read the
///         captures and gave them to <c>f</c>. Each was a closure and a delegate.
///         The subclass gives the read as a method. A merge of two streams is
///         <see cref="MergeTwoMaybes{T1,T2,TResult}" />.
///     </para>
/// </remarks>
/// <typeparam name="TResult">The type of the values of the output stream.</typeparam>
// ReSharper disable once InheritdocConsiderUsage - The type documents itself.
internal abstract class MergeMaybesOutput<TResult> : OnceGate
{
    private protected MergeMaybesOutput(IKeepListenersAlive keepListenersAlive) =>
        this.Out = new Stream<TResult>(keepListenersAlive);

    internal override Node Node => this.Out.Node;

    private protected Stream<TResult> Out { get; }

    /// <summary>Links one input into the output through the gate.</summary>
    private protected void Capture<TInput>(
        Stream<TInput> input,
        TransactionInternal trans,
        InputCapture<TInput> capture) =>
        this.Out.UnsafeAttachListener(
            StreamInternal.Capture(input: input, gate: this, trans: trans, capture: capture));

    /// <summary>Reads the captures of the transaction, clears them, and gives the output value.</summary>
    private protected abstract TResult Combine(TransactionInternal trans);

    private protected override void Run(TransactionInternal trans) =>
        this.Out.Send(trans: trans, a: this.Combine(trans));
}

/// <inheritdoc />
internal sealed class MergeMaybesOutput<T1, T2, T3, TResult>(
    in Stream<T1> s1,
    in Stream<T2> s2,
    in Stream<T3> s3,
    in Func<MaybeInternal<T1>, MaybeInternal<T2>, MaybeInternal<T3>, TResult> f)
    : MergeMaybesOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            s1.KeepListenersAlive,
            s2.KeepListenersAlive,
            s3.KeepListenersAlive))
{
    private readonly Func<MaybeInternal<T1>, MaybeInternal<T2>, MaybeInternal<T3>, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T1> s1,
        Stream<T2> s2,
        Stream<T3> s3,
        Func<MaybeInternal<T1>, MaybeInternal<T2>, MaybeInternal<T3>, TResult> f)
    {
        MergeMaybesOutput<T1, T2, T3, TResult> merge = new(s1: s1, s2: s2, s3: s3, f: f);

        merge.Capture(input: s1, trans: trans, capture: merge.p1);
        merge.Capture(input: s2, trans: trans, capture: merge.p2);
        merge.Capture(input: s3, trans: trans, capture: merge.p3);

        return merge.Out;
    }

    private protected override TResult Combine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(trans),
            arg2: this.p2.Take(trans),
            arg3: this.p3.Take(trans));
}

/// <inheritdoc />
internal sealed class MergeMaybesOutput<T1, T2, T3, T4, TResult>(
    in Stream<T1> s1,
    in Stream<T2> s2,
    in Stream<T3> s3,
    in Stream<T4> s4,
    in Func<MaybeInternal<T1>, MaybeInternal<T2>, MaybeInternal<T3>, MaybeInternal<T4>, TResult> f)
    : MergeMaybesOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            s1.KeepListenersAlive,
            s2.KeepListenersAlive,
            s3.KeepListenersAlive,
            s4.KeepListenersAlive))
{
    private readonly Func<MaybeInternal<T1>, MaybeInternal<T2>, MaybeInternal<T3>, MaybeInternal<T4>, TResult> f = f;
    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T1> s1,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Func<MaybeInternal<T1>, MaybeInternal<T2>, MaybeInternal<T3>, MaybeInternal<T4>, TResult> f)
    {
        MergeMaybesOutput<T1, T2, T3, T4, TResult> merge = new(s1: s1, s2: s2, s3: s3, s4: s4, f: f);

        merge.Capture(input: s1, trans: trans, capture: merge.p1);
        merge.Capture(input: s2, trans: trans, capture: merge.p2);
        merge.Capture(input: s3, trans: trans, capture: merge.p3);
        merge.Capture(input: s4, trans: trans, capture: merge.p4);

        return merge.Out;
    }

    private protected override TResult Combine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(trans),
            arg2: this.p2.Take(trans),
            arg3: this.p3.Take(trans),
            arg4: this.p4.Take(trans));
}

/// <inheritdoc />
internal sealed class MergeMaybesOutput<T1, T2, T3, T4, T5, TResult>(
    in Stream<T1> s1,
    in Stream<T2> s2,
    in Stream<T3> s3,
    in Stream<T4> s4,
    in Stream<T5> s5,
    in Func<MaybeInternal<T1>, MaybeInternal<T2>, MaybeInternal<T3>, MaybeInternal<T4>, MaybeInternal<T5>, TResult> f)
    : MergeMaybesOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            s1.KeepListenersAlive,
            s2.KeepListenersAlive,
            s3.KeepListenersAlive,
            s4.KeepListenersAlive,
            s5.KeepListenersAlive))
{
    private readonly Func<
        MaybeInternal<T1>,
        MaybeInternal<T2>,
        MaybeInternal<T3>,
        MaybeInternal<T4>,
        MaybeInternal<T5>,
        TResult> f = f;

    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T1> s1,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Func<MaybeInternal<T1>, MaybeInternal<T2>, MaybeInternal<T3>, MaybeInternal<T4>, MaybeInternal<T5>, TResult> f)
    {
        MergeMaybesOutput<T1, T2, T3, T4, T5, TResult> merge = new(s1: s1, s2: s2, s3: s3, s4: s4, s5: s5, f: f);

        merge.Capture(input: s1, trans: trans, capture: merge.p1);
        merge.Capture(input: s2, trans: trans, capture: merge.p2);
        merge.Capture(input: s3, trans: trans, capture: merge.p3);
        merge.Capture(input: s4, trans: trans, capture: merge.p4);
        merge.Capture(input: s5, trans: trans, capture: merge.p5);

        return merge.Out;
    }

    private protected override TResult Combine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(trans),
            arg2: this.p2.Take(trans),
            arg3: this.p3.Take(trans),
            arg4: this.p4.Take(trans),
            arg5: this.p5.Take(trans));
}

/// <inheritdoc />
internal sealed class MergeMaybesOutput<T1, T2, T3, T4, T5, T6, TResult>(
    in Stream<T1> s1,
    in Stream<T2> s2,
    in Stream<T3> s3,
    in Stream<T4> s4,
    in Stream<T5> s5,
    in Stream<T6> s6,
    in Func<
        MaybeInternal<T1>,
        MaybeInternal<T2>,
        MaybeInternal<T3>,
        MaybeInternal<T4>,
        MaybeInternal<T5>,
        MaybeInternal<T6>,
        TResult> f)
    : MergeMaybesOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            s1.KeepListenersAlive,
            s2.KeepListenersAlive,
            s3.KeepListenersAlive,
            s4.KeepListenersAlive,
            s5.KeepListenersAlive,
            s6.KeepListenersAlive))
{
    private readonly Func<
        MaybeInternal<T1>,
        MaybeInternal<T2>,
        MaybeInternal<T3>,
        MaybeInternal<T4>,
        MaybeInternal<T5>,
        MaybeInternal<T6>,
        TResult> f = f;

    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();
    private readonly InputCapture<T6> p6 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T1> s1,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Stream<T6> s6,
        Func<
            MaybeInternal<T1>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            MaybeInternal<T6>,
            TResult> f)
    {
        MergeMaybesOutput<T1, T2, T3, T4, T5, T6, TResult> merge =
            new(s1: s1, s2: s2, s3: s3, s4: s4, s5: s5, s6: s6, f: f);

        merge.Capture(input: s1, trans: trans, capture: merge.p1);
        merge.Capture(input: s2, trans: trans, capture: merge.p2);
        merge.Capture(input: s3, trans: trans, capture: merge.p3);
        merge.Capture(input: s4, trans: trans, capture: merge.p4);
        merge.Capture(input: s5, trans: trans, capture: merge.p5);
        merge.Capture(input: s6, trans: trans, capture: merge.p6);

        return merge.Out;
    }

    private protected override TResult Combine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(trans),
            arg2: this.p2.Take(trans),
            arg3: this.p3.Take(trans),
            arg4: this.p4.Take(trans),
            arg5: this.p5.Take(trans),
            arg6: this.p6.Take(trans));
}

/// <inheritdoc />
internal sealed class MergeMaybesOutput<T1, T2, T3, T4, T5, T6, T7, TResult>(
    in Stream<T1> s1,
    in Stream<T2> s2,
    in Stream<T3> s3,
    in Stream<T4> s4,
    in Stream<T5> s5,
    in Stream<T6> s6,
    in Stream<T7> s7,
    in Func<
        MaybeInternal<T1>,
        MaybeInternal<T2>,
        MaybeInternal<T3>,
        MaybeInternal<T4>,
        MaybeInternal<T5>,
        MaybeInternal<T6>,
        MaybeInternal<T7>,
        TResult> f)
    : MergeMaybesOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            s1.KeepListenersAlive,
            s2.KeepListenersAlive,
            s3.KeepListenersAlive,
            s4.KeepListenersAlive,
            s5.KeepListenersAlive,
            s6.KeepListenersAlive,
            s7.KeepListenersAlive))
{
    private readonly Func<
        MaybeInternal<T1>,
        MaybeInternal<T2>,
        MaybeInternal<T3>,
        MaybeInternal<T4>,
        MaybeInternal<T5>,
        MaybeInternal<T6>,
        MaybeInternal<T7>,
        TResult> f = f;

    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();
    private readonly InputCapture<T6> p6 = new();
    private readonly InputCapture<T7> p7 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T1> s1,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Stream<T6> s6,
        Stream<T7> s7,
        Func<
            MaybeInternal<T1>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            MaybeInternal<T6>,
            MaybeInternal<T7>,
            TResult> f)
    {
        MergeMaybesOutput<T1, T2, T3, T4, T5, T6, T7, TResult> merge =
            new(s1: s1, s2: s2, s3: s3, s4: s4, s5: s5, s6: s6, s7: s7, f: f);

        merge.Capture(input: s1, trans: trans, capture: merge.p1);
        merge.Capture(input: s2, trans: trans, capture: merge.p2);
        merge.Capture(input: s3, trans: trans, capture: merge.p3);
        merge.Capture(input: s4, trans: trans, capture: merge.p4);
        merge.Capture(input: s5, trans: trans, capture: merge.p5);
        merge.Capture(input: s6, trans: trans, capture: merge.p6);
        merge.Capture(input: s7, trans: trans, capture: merge.p7);

        return merge.Out;
    }

    private protected override TResult Combine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(trans),
            arg2: this.p2.Take(trans),
            arg3: this.p3.Take(trans),
            arg4: this.p4.Take(trans),
            arg5: this.p5.Take(trans),
            arg6: this.p6.Take(trans),
            arg7: this.p7.Take(trans));
}

/// <inheritdoc />
internal sealed class MergeMaybesOutput<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(
    in Stream<T1> s1,
    in Stream<T2> s2,
    in Stream<T3> s3,
    in Stream<T4> s4,
    in Stream<T5> s5,
    in Stream<T6> s6,
    in Stream<T7> s7,
    in Stream<T8> s8,
    in Func<
        MaybeInternal<T1>,
        MaybeInternal<T2>,
        MaybeInternal<T3>,
        MaybeInternal<T4>,
        MaybeInternal<T5>,
        MaybeInternal<T6>,
        MaybeInternal<T7>,
        MaybeInternal<T8>,
        TResult> f)
    : MergeMaybesOutput<TResult>(
        KeepListenersAliveInternal.Combine(
            s1.KeepListenersAlive,
            s2.KeepListenersAlive,
            s3.KeepListenersAlive,
            s4.KeepListenersAlive,
            s5.KeepListenersAlive,
            s6.KeepListenersAlive,
            s7.KeepListenersAlive,
            s8.KeepListenersAlive))
{
    private readonly Func<
        MaybeInternal<T1>,
        MaybeInternal<T2>,
        MaybeInternal<T3>,
        MaybeInternal<T4>,
        MaybeInternal<T5>,
        MaybeInternal<T6>,
        MaybeInternal<T7>,
        MaybeInternal<T8>,
        TResult> f = f;

    private readonly InputCapture<T1> p1 = new();
    private readonly InputCapture<T2> p2 = new();
    private readonly InputCapture<T3> p3 = new();
    private readonly InputCapture<T4> p4 = new();
    private readonly InputCapture<T5> p5 = new();
    private readonly InputCapture<T6> p6 = new();
    private readonly InputCapture<T7> p7 = new();
    private readonly InputCapture<T8> p8 = new();

    internal static Stream<TResult> Create(
        TransactionInternal trans,
        Stream<T1> s1,
        Stream<T2> s2,
        Stream<T3> s3,
        Stream<T4> s4,
        Stream<T5> s5,
        Stream<T6> s6,
        Stream<T7> s7,
        Stream<T8> s8,
        Func<
            MaybeInternal<T1>,
            MaybeInternal<T2>,
            MaybeInternal<T3>,
            MaybeInternal<T4>,
            MaybeInternal<T5>,
            MaybeInternal<T6>,
            MaybeInternal<T7>,
            MaybeInternal<T8>,
            TResult> f)
    {
        MergeMaybesOutput<T1, T2, T3, T4, T5, T6, T7, T8, TResult> merge =
            new(s1: s1, s2: s2, s3: s3, s4: s4, s5: s5, s6: s6, s7: s7, s8: s8, f: f);

        merge.Capture(input: s1, trans: trans, capture: merge.p1);
        merge.Capture(input: s2, trans: trans, capture: merge.p2);
        merge.Capture(input: s3, trans: trans, capture: merge.p3);
        merge.Capture(input: s4, trans: trans, capture: merge.p4);
        merge.Capture(input: s5, trans: trans, capture: merge.p5);
        merge.Capture(input: s6, trans: trans, capture: merge.p6);
        merge.Capture(input: s7, trans: trans, capture: merge.p7);
        merge.Capture(input: s8, trans: trans, capture: merge.p8);

        return merge.Out;
    }

    private protected override TResult Combine(TransactionInternal trans) =>
        this.f(
            arg1: this.p1.Take(trans),
            arg2: this.p2.Take(trans),
            arg3: this.p3.Take(trans),
            arg4: this.p4.Take(trans),
            arg5: this.p5.Take(trans),
            arg6: this.p6.Take(trans),
            arg7: this.p7.Take(trans),
            arg8: this.p8.Take(trans));
}
