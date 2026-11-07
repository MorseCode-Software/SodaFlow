using System;

namespace SodaFlow;

// The state that Collect, Accum, and Calm move between the firings of a stream, with the actions
// that the transaction runs on it.
//
// Before, FRP primitives made this. A looped stream moved the state back, a behavior held the
// state, a snapshot read it, and one map served each output. That is four streams for Collect
// and two for Accum, to move one value between firings. Then, a method with local variables and
// closures did the same with one output stream. It allocated a delegate in each transaction that
// fired, because its commit captured the locals. This object holds the state as fields and is
// its own sample action and last action. Thus, a transaction that fires allocates nothing for
// the protocol.
//
// The behavior supplied two fields, and the division between them is important. A snapshot reads
// a behavior with SampleNoTransaction. Thus, each firing in a transaction saw the state from the
// start of that transaction, and the behavior committed the result of the last firing. One field
// with an update without a copy lets a later firing in the same transaction see an earlier one.
// That is a different fold, and the caller function f can find it.
//
// Failure shows the deferral. A transaction that throws discards its last queue. Thus, a firing
// in that transaction does not commit, and the state stays as it was before. This is the one
// difference from a commit without a copy.
// CalmTests.AFailedTransactionDoesNotCommitTheRememberedValue holds this behavior. All other
// tests pass with one of the two methods.
//
// The emit flag lets Calm use this. A Maybe result with a filter costs a second stream and a
// node in the rank graph. Calm exists to prevent that cost. A bool in a tuple that is already a
// struct costs one branch, and the processor predicts that branch.
internal sealed class CarriedState<T, TState, TReturn>(
    in Stream<TReturn> @out,
    in LazySample<TState> initialState,
    in Func<T, TState, (bool Emit, TReturn ReturnValue, TState State)> f)
    : StreamAction<T>, ISampleAction, ILastAction
{
    private readonly Func<T, TState, (bool Emit, TReturn ReturnValue, TState State)> f = f;
    private readonly LazySample<TState> initialState = initialState;
    private readonly Stream<TReturn> @out = @out;

    // The state until the first commit is the initial sample, read at each firing. The sample
    // keeps its value after the transaction that forced it commits, and forgets it when that
    // transaction fails. Thus, a copy here needs no reset of its own on that failure. A failure
    // after the first commit cannot put the initial value back.
    private TState? committed;
    private bool committedIsSet;
    private bool hasPending;
    private TState? pending;

    /// <inheritdoc />
    public override void Fire(TransactionInternal trans, T a)
    {
        // ReSharper disable once NullableWarningSuppressionIsUsed - committed is set when committedIsSet is true.
        TState current = this.committedIsSet ? this.committed! : this.initialState.Force(trans);
        (bool emit, TReturn returnValue, TState state) = this.f(arg1: a, arg2: current);

        if (!this.hasPending)
        {
            this.hasPending = true;
            trans.Last(this);
        }

        this.pending = state;

        if (emit)
        {
            this.@out.Send(trans: trans, a: returnValue);
        }
    }

    // SodaFlow forces the initial state in the sample phase of the construction transaction. The
    // behavior that this code replaced forced its lazy initial value in that phase, also when
    // nothing fired.
    void ISampleAction.Sample(TransactionInternal trans)
    {
        if (!this.committedIsSet)
        {
            this.initialState.Force(trans);
        }
    }

    void ILastAction.Commit()
    {
        this.committed = this.pending;
        this.committedIsSet = true;
        this.hasPending = false;
    }

    // A transaction that fails does not commit its state, and it must also clear hasPending.
    // Otherwise, no subsequent transaction queues the commit.
    void ILastAction.Discard()
    {
        this.hasPending = false;
        this.pending = default;
    }
}
