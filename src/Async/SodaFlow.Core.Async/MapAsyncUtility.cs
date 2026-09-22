using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace SodaFlow.Async;

/// <summary>
///     The work that a MapAsync pipeline does for one value from the source stream. It gives a
///     <see cref="ResultConstructor{TResult}" /> and not a result, because the two answers are not
///     the same: <see cref="ResultFactory{TResult}.FromValue" /> carries a value that the
///     operation has, and <see cref="ResultFactory{TResult}.Construct" /> carries a function that
///     the pipeline calls in the transaction that sends the result. Use the second one when the
///     result contains a cell, a stream, or an other part of a SodaFlow graph, because such a
///     part must come into existence in that transaction.
/// </summary>
/// <typeparam name="TInput">The type in the source stream.</typeparam>
/// <typeparam name="TResult">The type that the pipeline publishes.</typeparam>
/// <param name="input">The value from the source stream, as the pipeline admitted it.</param>
/// <param name="resultFactory">Makes the two kinds of answer. It has no state.</param>
/// <param name="token">
///     Cancels this operation. It combines the cancellation of this item with the cancellation of
///     the strategy. An operation that does not monitor it runs to its end, and the pipeline then
///     does not publish its result.
/// </param>
/// <returns>A Task with the value, or with the function that makes the value.</returns>
[PublicAPI]
public delegate Task<ResultConstructor<TResult>> MapAsyncOperation<in TInput, TResult>(
    TInput input,
    ResultFactory<TResult> resultFactory,
    CancellationToken token);

/// <summary>The two states of a tracked item: a wait for a slot, or execution.</summary>
[PublicAPI]
public enum AsyncItemStatus
{
    /// <summary>The pipeline admits and tracks the item, and no strategy promoted it to
    /// Running.</summary>
    Queued,

    /// <summary>A strategy promoted the item. The pipeline calls its operation, or called
    /// it.</summary>
    Running
}

/// <summary>An input value that a MapAsync pipeline tracks, and its current status.</summary>
[PublicAPI]
public readonly struct AsyncItem<TInput>
{
    /// <summary>Holds an input value with its status. The execution engine builds one for each
    /// tracked item.</summary>
    /// <param name="value">The initial input value from the source stream.</param>
    /// <param name="status">The status of that value: Queued or Running.</param>
    public AsyncItem(TInput value, AsyncItemStatus status)
    {
        this.Value = value;
        this.Status = status;
    }

    /// <summary>The initial input value from the source stream.</summary>
    public TInput Value { get; }

    /// <summary>The status of this value: a wait for a strategy to promote it, or
    /// Running.</summary>
    public AsyncItemStatus Status { get; }
}

/// <summary>
///     The answer of a <see cref="MapAsyncOperation{TInput,TResult}" />: a result, or a function
///     that makes one. The pipeline calls such a function in the transaction that sends the
///     result. Make one with <see cref="ResultFactory{TResult}" />, which the operation
///     receives.
/// </summary>
/// <typeparam name="TResult">The type that the pipeline publishes.</typeparam>
[PublicAPI]
public sealed class ResultConstructor<TResult>
{
    private readonly TResult? result;
    private readonly Func<TResult>? constructResult;

    internal ResultConstructor(TResult result) => this.result = result;

    internal ResultConstructor(Func<TResult> constructResult) => this.constructResult = constructResult;

    // ReSharper disable once NullableWarningSuppressionIsUsed - result is not null if constructResult is null
    internal TResult GetResult() => this.constructResult != null ? this.constructResult() : this.result!;
}

/// <summary>
///     Makes the answer of a <see cref="MapAsyncOperation{TInput,TResult}" />. The pipeline gives
///     one to each call of the operation. It holds no state, and a caller cannot make one.
/// </summary>
/// <typeparam name="TResult">The type that the pipeline publishes.</typeparam>
[PublicAPI]
public sealed class ResultFactory<TResult>
{
    internal static readonly ResultFactory<TResult> Instance = new();

    private ResultFactory()
    {
    }

    /// <summary>
    ///     Carries a result that the operation has. Use this one when the operation makes no part
    ///     of a SodaFlow graph.
    /// </summary>
    /// <param name="value">The value to publish.</param>
    /// <returns>The answer to return from the operation.</returns>
    public ResultConstructor<TResult> FromValue(TResult value) => new(value);

    /// <summary>
    ///     Carries a function that makes the result. The pipeline calls it one time, in the
    ///     transaction that sends the result, which is what a result with a cell or a stream in it
    ///     must have. Keep the function short, because it holds that transaction while it runs.
    ///     The pipeline calls it only for an item that it publishes: the strategy decides that
    ///     first, and this function runs after that decision. Thus an item that a cancellation
    ///     stopped, or that a strategy refused, makes no result at all.
    /// </summary>
    /// <param name="makeResult">Makes the value to publish.</param>
    /// <returns>The answer to return from the operation.</returns>
    public ResultConstructor<TResult> Construct(Func<TResult> makeResult) => new(makeResult);
}

/// <summary>
///     The status of a MapAsync pipeline. It gives the operation of the pipeline, and each input
///     value that the pipeline tracks now with the status of that value. It is also the only
///     handle to stop the pipeline. See <see cref="AsyncMapStatus.Dispose" />. This type adds
///     <see cref="Items" /> to <see cref="AsyncMapStatus" />, which is what makes it generic: a
///     caller that reads only <see cref="AsyncMapStatus.IsRunning" /> or stops the pipeline can
///     hold the base type.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class AsyncMapStatus<TInput> : AsyncMapStatus
{
    internal AsyncMapStatus(
        Cell<bool> isRunning,
        Cell<IReadOnlyList<AsyncItem<TInput>>> items,
        Action dispose)
        : base(isRunning: isRunning, dispose: dispose) =>
        this.Items = items;

    /// <summary>
    ///     Each value that the pipeline tracks now, Queued or Running. The sequence is not
    ///     specified, but each update is one snapshot that agrees with itself.
    /// </summary>
    public Cell<IReadOnlyList<AsyncItem<TInput>>> Items { get; }
}

/// <summary>
///     The status of a MapAsync pipeline, without the part that the input type decides. It gives
///     the operation of the pipeline, and it is the only handle to stop the pipeline. See
///     <see cref="Dispose" />. A caller that does not read
///     <see cref="AsyncMapStatus{TInput}.Items" /> can hold this type and does not have to name
///     the input type. <see cref="AsyncMapStatus{TInput}" /> is the type that MapAsync returns.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class AsyncMapStatus : IDisposable
{
    private readonly Action dispose;

    // private protected, and not internal: this base is for AsyncMapStatus<TInput> to extend, and
    // an instance of the base alone tracks no items and has no use. AsyncMapBase below does the
    // same.
    private protected AsyncMapStatus(
        Cell<bool> isRunning,
        Action dispose)
    {
        this.IsRunning = isRunning;
        this.dispose = dispose;
    }

    /// <summary>True while one item or more has Status == Running. An item with the Queued status
    /// does not count.</summary>
    public Cell<bool> IsRunning { get; }

    /// <summary>
    ///     Stops this pipeline. The pipeline admits no more values from the source stream, and it
    ///     does not queue them or start them. The cancelOnDispose parameter of MapAsync, which is
    ///     true by default, sets at the setup if a disposal also cancels the tracked items. This
    ///     method does not make that selection, because this IDisposable.Dispose() has no
    ///     parameter and is the only path to a disposal. When cancelOnDispose was true, a disposal
    ///     cancels each tracked item, Queued or Running, as one send on a cancelAll stream does.
    ///     The same limits apply. A Running operation stops only if it monitors its
    ///     CancellationToken, and the pipeline removes a Queued item at the time of its promotion,
    ///     which is not always at the return of this call. This method also does not stop the
    ///     output. Each operation that runs continues to its end and then publishes its result
    ///     or its error. A second call is safe, because each call after the first does
    ///     nothing.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    public void Dispose()
    {
        this.dispose();

        // This type has no finalizer, but it is a base class, thus a subclass can add one. The
        // call makes a disposal here sufficient for such a subclass. CA1816 asks for it on each
        // IDisposable type that other types can extend.
        GC.SuppressFinalize(this);
    }
}

/// <summary>
///     The shared base of the two parts of a MapAsync pipeline: the strategy
///     (<see cref="AsyncConcurrencyStrategy{TInput,TState}" />) and the engine that runs
///     it (<see cref="AsyncMapExecutionManager{TInput,TResult,TStrategyInput}" />).
///     Its only purpose is to hold the small data types that the two parts send to each other:
///     <see cref="AsyncQueuedItem{TInput}" />, <see cref="AsyncToStart{TInput}" />,
///     <see cref="AsyncOutcome{TResult}" />, and <see cref="AsyncStrategyResult{TInput}" />. They
///     are nested types here, and not public top-level types. One class is not a subtype of the
///     other. Without this shared base, one part or the two parts must make these types fully
///     public to name them. With this base, the two parts get them through usual inheritance, and
///     the types stay off the public surface of the library. Only code in this assembly, and code
///     that subclasses <see cref="AsyncConcurrencyStrategy{TInput,TState}" /> to write a
///     custom strategy, can see them. This base is not generic, because TInput and TResult belong
///     to the nested types that use them, and not to each user of this base.
/// </summary>
[PublicAPI]
public abstract class AsyncMapBase
{
    // This prevents a subclass of AsyncMapBase by a type out of this assembly.
    // AsyncConcurrencyStrategy<TInput,TState> is the type that external code subclasses
    // for a custom strategy. That type is in this assembly and can call this constructor. An
    // external subclass of that type does not call this constructor.
    internal AsyncMapBase()
    {
    }

    /// <summary>
    ///     A value that a MapAsync pipeline tracks, from its admission until its promotion, its
    ///     end, or its cancellation. It is also the one object that identifies the value in
    ///     <see cref="AsyncToStart{TInput}" /> and in
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" />. It is
    ///     opaque. A strategy can keep one, usually in the state of its call, to promote it after
    ///     this or to identify it again at its end, and can read its Value. A strategy cannot make
    ///     one, because the constructor is internal and the class is sealed. Thus,
    ///     each instance comes from an
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.Admit" /> call. A strategy
    ///     always receives the same instance that it got before. Thus, ReferenceEquals, or ==, is
    ///     sufficient to identify an admitted value in its state. An equal
    ///     <see cref="Id" /> gives the same answer, and it is also correct across the different
    ///     instances that the execution engine keeps for each admitted value. For that cause the
    ///     SwitchLatest strategy in the library compares the Id.
    /// </summary>
    [PublicAPI]
    protected internal sealed class AsyncQueuedItem<TInput>
    {
        internal AsyncQueuedItem(Guid id, TInput value, CancellationTokenSource cancellation)
        {
            this.Id = id;
            this.Value = value;
            this.Cancellation = cancellation;
        }

        /// <summary>
        ///     The identity of this item. The pipeline assigns it at the admission, and it is the
        ///     same across the different <see cref="AsyncQueuedItem{TInput}" /> instances that the
        ///     execution engine keeps for one admitted value. One instance has the type for the
        ///     strategy, and the other has the type for the public
        ///     <see cref="AsyncItem{TInput}" /> view. An equal ID means the same tracked value.
        /// </summary>
        public Guid Id { get; }

        /// <summary>The value of this item at its admission.</summary>
        public TInput Value { get; }

        /// <summary>
        ///     The source of the cancellation of this item. <see cref="Cancel" /> cancels this
        ///     source, and the execution engine links it into the token of the operation. It is
        ///     internal, because a strategy cancels through <see cref="Cancel" /> and does not use
        ///     this field.
        /// </summary>
        internal CancellationTokenSource Cancellation { get; }

        /// <summary>
        ///     Cancels this tracked item. It is the mechanism of a cancelAll stream and of a
        ///     cancelMatching stream, and a strategy can use it for its own schedule. For example,
        ///     SwitchLatest replaces its previous run with it. It operates on an item with the
        ///     Queued status and on an item with the Running status. The pipeline does not start a
        ///     Queued item at its turn, and a Running operation stops only if it monitors the
        ///     CancellationToken that it got. In each condition the item ends as Canceled, thus
        ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" /> runs for
        ///     it and can start the next operation.
        ///     A call on an item that ended, on an item that a previous call canceled, or on an
        ///     item at its end is safe. Those calls do nothing and are not errors, thus a strategy
        ///     with a stale reference does not monitor the time when an item stops being
        ///     cancellable.
        /// </summary>
        public void Cancel()
        {
            // The execution engine disposes the CancellationTokenSource of an item at its end,
            // thus a stale reference can come here after that disposal. Cancel() on a disposed CTS
            // throws an exception, and a strategy must not have a test for an item that ended.
            // Thus, this code catches only that exception.
            try
            {
                this.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>A tracked item to start now, or to promote now from Queued to Running.</summary>
    [PublicAPI]
    protected internal sealed class AsyncToStart<TInput>
    {
        /// <exception cref="ArgumentNullException"><paramref name="item" /> is null.</exception>
        public AsyncToStart(AsyncQueuedItem<TInput> item, CancellationToken strategyToken = default)
        {
            this.Item = item ?? throw new ArgumentNullException(nameof(item));
            this.StrategyToken = strategyToken;
        }

        /// <summary>The admitted item to start or to promote. It comes from a previous Admit
        /// call.</summary>
        public AsyncQueuedItem<TInput> Item { get; }

        /// <summary>
        ///     An optional second cancellation source to link into this run, with the source of
        ///     the item. It is not necessary to cancel an item that the strategy controls, because
        ///     <see cref="AsyncQueuedItem{TInput}.Cancel" /> does that. Use this to attach a run to
        ///     an external source: a timeout for one operation, an ambient operation token, or a
        ///     shared token for a batch of work. At each other time, keep the default.
        /// </summary>
        public CancellationToken StrategyToken { get; }
    }

    /// <summary>
    ///     How an item ended, which is what a strategy reads. It carries no result value: the
    ///     pipeline makes the result of a MapAsync operation after the strategy decides, and only
    ///     for an item that it publishes. See <see cref="ResultFactory{TResult}.Construct" />. A
    ///     value here thus obliges the pipeline to make each result, also the results that no code
    ///     reads.
    ///     <para>
    ///         Succeeded says that the operation returned. The pipeline publishes an error for
    ///         such an item when the function that makes the result throws.
    ///     </para>
    /// </summary>
    [PublicAPI]
    protected internal sealed class AsyncCompletion
    {
        /// <summary>The exception from the operation when <see cref="kind" /> is Failed, and null
        /// at each other time.</summary>
        private readonly Exception? error;

        // Match is the only path to the data, as it is for AsyncOutcome below, thus a strategy
        // cannot read the error and also ignore the two ends that have none.
        private readonly AsyncCompletionKind kind;

        private AsyncCompletion(AsyncCompletionKind kind, Exception? error)
        {
            this.kind = kind;
            this.error = error;
        }

        /// <summary>Runs the handler for the end of this item, and returns its value.</summary>
        /// <typeparam name="T">The type that each handler returns.</typeparam>
        /// <param name="onSucceeded">Handles an operation that returned.</param>
        /// <param name="onFailed">Handles a run with an error, with the exception from the operation.</param>
        /// <param name="onCanceled">Handles a canceled run, which can have no start.</param>
        /// <returns>The value that the handler returned.</returns>
        public T Match<T>(
            Func<T> onSucceeded,
            Func<Exception, T> onFailed,
            Func<T> onCanceled) =>
            this.kind switch
            {
                AsyncCompletionKind.Succeeded => onSucceeded(),
                // ReSharper disable once NullableWarningSuppressionIsUsed - This object can only be constructed with
                // kind being AsyncCompletionKind.Failed when it sets this.error to a non-null value.
                AsyncCompletionKind.Failed => onFailed(this.error!),
                AsyncCompletionKind.Canceled => onCanceled(),
                _ => throw new InvalidOperationException("Unknown value for kind.")
            };

        /// <summary>
        ///     Runs the handler for the end of this item. Each handler is optional. Give null for
        ///     an end with no handler, and this method ignores that end.
        /// </summary>
        /// <param name="onSucceeded">Handles an operation that returned. It can be null.</param>
        /// <param name="onFailed">
        ///     Handles a run with an error, with the exception from the operation. It can be null.
        /// </param>
        /// <param name="onCanceled">Handles a canceled run, which can have no start. It can be null.</param>
        public void MatchVoid(
            Action? onSucceeded,
            Action<Exception>? onFailed,
            Action? onCanceled)
        {
            switch (this.kind)
            {
                case AsyncCompletionKind.Succeeded:
                    onSucceeded?.Invoke();
                    break;
                case AsyncCompletionKind.Failed:
                    // ReSharper disable once NullableWarningSuppressionIsUsed - This object can only be constructed with
                    // kind being AsyncCompletionKind.Failed when it sets this.error to a non-null value.
                    onFailed?.Invoke(this.error!);
                    break;
                case AsyncCompletionKind.Canceled:
                    onCanceled?.Invoke();
                    break;
                default:
                    throw new InvalidOperationException("Unknown value for kind.");
            }
        }

        /// <summary>Builds a Succeeded end, for an operation that returned.</summary>
        public static AsyncCompletion Succeeded() =>
            new(kind: AsyncCompletionKind.Succeeded, error: null);

        /// <summary>Builds a Failed end with the exception from the operation.</summary>
        public static AsyncCompletion Failed(Exception error) =>
            new(kind: AsyncCompletionKind.Failed, error: error);

        /// <summary>Builds a Canceled end.</summary>
        public static AsyncCompletion Canceled() =>
            new(kind: AsyncCompletionKind.Canceled, error: null);

        /// <summary>The three ends of an item.</summary>
        private enum AsyncCompletionKind
        {
            /// <summary>The operation returned.</summary>
            Succeeded,

            /// <summary>The operation threw an exception. See <see cref="AsyncCompletion.error" />.</summary>
            Failed,

            /// <summary>
            ///     A cancellation stopped the operation, or the operation had no start because a
            ///     cancellation removed it with the Queued status. The pipeline never publishes
            ///     this end, at each return value of OnCompleted. See
            ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" />.
            /// </summary>
            Canceled
        }
    }

    /// <summary>
    ///     The result at the end of an item, which the execution engine holds while it decides
    ///     what to publish. A strategy reads <see cref="AsyncCompletion" /> and never this type.
    /// </summary>
    internal sealed class AsyncOutcome<TResult>
    {
        /// <summary>The exception from the operation when <see cref="kind" /> is Failed, and null
        /// at each other time.</summary>
        private readonly Exception? error;
        // There is no Value property, no Error property, and no Kind property. Match is the only
        // path to the data, thus a strategy cannot read a result and also ignore the condition
        // with no result. There is also no asynchronous Match. OnCompleted is synchronous and
        // answers with data, thus an awaitable Match causes work here that belongs to the
        // execution engine.

        /// <summary>Which of the three ends this item had.</summary>
        private readonly AsyncOutcomeKind kind;

        /// <summary>The return value of the operation when <see cref="kind" /> is Succeeded, and
        /// the default value at each other time.</summary>
        private readonly TResult? value;

        private AsyncOutcome(AsyncOutcomeKind kind, TResult? value, Exception? error)
        {
            this.kind = kind;
            this.value = value;
            this.error = error;
        }

        /// <summary>Runs the handler for the end of this item, and returns its value.</summary>
        /// <typeparam name="T">The type that each handler returns.</typeparam>
        /// <param name="onSucceeded">Handles a run that succeeded, with the value that the operation returned.</param>
        /// <param name="onFailed">Handles a run with an error, with the exception from the operation.</param>
        /// <param name="onCanceled">Handles a canceled run, which can have no start.</param>
        /// <returns>The value that the handler returned.</returns>
        public T Match<T>(
            Func<TResult, T> onSucceeded,
            Func<Exception, T> onFailed,
            Func<T> onCanceled) =>
            this.kind switch
            {
                // ReSharper disable once NullableWarningSuppressionIsUsed - This object can only be constructed with
                // kind being AsyncOutcomeKind.Succeeded when it sets this.value to a non-null value.
                AsyncOutcomeKind.Succeeded => onSucceeded(this.value!),
                // ReSharper disable once NullableWarningSuppressionIsUsed - This object can only be constructed with
                // kind being AsyncOutcomeKind.Failed when it sets this.error to a non-null value.
                AsyncOutcomeKind.Failed => onFailed(this.error!),
                AsyncOutcomeKind.Canceled => onCanceled(),
                _ => throw new InvalidOperationException("Unknown value for kind.")
            };

        /// <summary>
        ///     Runs the handler for the end of this item. Each handler is optional. Give null for
        ///     an outcome with no handler, and this method ignores that outcome.
        /// </summary>
        /// <param name="onSucceeded">
        ///     Handles a run that succeeded, with the value that the operation returned. It can be
        ///     null.
        /// </param>
        /// <param name="onFailed">
        ///     Handles a run with an error, with the exception from the operation. It can be null.
        /// </param>
        /// <param name="onCanceled">Handles a canceled run, which can have no start. It can be null.</param>
        public void MatchVoid(
            Action<TResult>? onSucceeded,
            Action<Exception>? onFailed,
            Action? onCanceled)
        {
            switch (this.kind)
            {
                case AsyncOutcomeKind.Succeeded:
                    // ReSharper disable once NullableWarningSuppressionIsUsed - This object can only be constructed with
                    // kind being AsyncOutcomeKind.Succeeded when it sets this.value to a non-null value.
                    onSucceeded?.Invoke(this.value!);
                    break;
                case AsyncOutcomeKind.Failed:
                    // ReSharper disable once NullableWarningSuppressionIsUsed - This object can only be constructed with
                    // kind being AsyncOutcomeKind.Failed when it sets this.error to a non-null value.
                    onFailed?.Invoke(this.error!);
                    break;
                case AsyncOutcomeKind.Canceled:
                    onCanceled?.Invoke();
                    break;
                default:
                    throw new InvalidOperationException("Unknown value for kind.");
            }
        }

        /// <summary>Builds a Succeeded outcome with the return value of the operation.</summary>
        public static AsyncOutcome<TResult> Succeeded(TResult value) =>
            new(kind: AsyncOutcomeKind.Succeeded, value: value, error: null);

        /// <summary>Builds a Failed outcome with the exception from the operation.</summary>
        public static AsyncOutcome<TResult> Failed(Exception error) =>
            new(kind: AsyncOutcomeKind.Failed, value: default, error: error);

        /// <summary>Builds a Canceled outcome.</summary>
        public static AsyncOutcome<TResult> Canceled() =>
            new(kind: AsyncOutcomeKind.Canceled, value: default, error: null);

        /// <summary>The three ends of an item.</summary>
        private enum AsyncOutcomeKind
        {
            /// <summary>The operation returned a value. See <see cref="AsyncOutcome{TResult}.value" />.</summary>
            Succeeded,

            /// <summary>The operation threw an exception. See <see cref="AsyncOutcome{TResult}.error" />.</summary>
            Failed,

            /// <summary>
            ///     A cancellation stopped the operation, or the operation had no start because a
            ///     cancellation removed it with the Queued status. The pipeline never publishes
            ///     this outcome, at each return value of OnCompleted. See
            ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" />.
            /// </summary>
            Canceled
        }
    }

    /// <summary>
    ///     The answer of a strategy: if the pipeline publishes the outcome that ended now, and
    ///     which items to start next.
    /// </summary>
    [PublicAPI]
    protected internal sealed class AsyncStrategyResult<TInput>
    {
        /// <summary>An empty Next list. This decision starts no more items.</summary>
        public static readonly IReadOnlyList<AsyncToStart<TInput>> None = Array.Empty<AsyncToStart<TInput>>();

        /// <summary>Builds the answer of a strategy from its two decisions.</summary>
        /// <param name="publish">
        ///     True when the pipeline sends the outcome that ended now to the results or to the
        ///     errors.
        /// </param>
        /// <param name="next">
        ///     Tracked items to start now, or to promote now. Give <see cref="None" /> for no
        ///     items.
        /// </param>
        public AsyncStrategyResult(bool publish, IReadOnlyList<AsyncToStart<TInput>> next)
        {
            this.Publish = publish;
            this.Next = next;
        }

        /// <summary>True when the pipeline sends the outcome that ended now to the results or to
        /// the errors.</summary>
        public bool Publish { get; }

        /// <summary>Tracked items to start now, or to promote now, because of this decision.</summary>
        public IReadOnlyList<AsyncToStart<TInput>> Next { get; }
    }

    /// <summary>
    ///     Removes the type of the state of a strategy. Thus,
    ///     <see cref="AsyncMapExecutionManager{TInput,TResult,TStrategyInput}" />
    ///     can hold a strategy with its state, and that class is not generic over the state. That
    ///     keeps TState out of each MapAsync signature. See
    ///     <see cref="StateManager{TInput,TState}" />, which is the only
    ///     implementation.
    /// </summary>
    internal interface IStateManager<TInput>
    {
        /// <summary>
        ///     Sends the call to
        ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.Admit" /> with the state
        ///     in the closure.
        /// </summary>
        IReadOnlyList<AsyncToStart<TInput>> Admit(AsyncQueuedItem<TInput> incoming);

        /// <summary>
        ///     Sends the call to
        ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" /> with the
        ///     state in the closure.
        /// </summary>
        AsyncStrategyResult<TInput> OnCompleted(AsyncQueuedItem<TInput> item, AsyncCompletion completion);
    }

    /// <summary>
    ///     Holds a strategy instance with the one <typeparamref name="TState" /> for a single
    ///     MapAsync call. See
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.CreateStateManager" />. Thus,
    ///     the execution engine can call Admit and OnCompleted, and does not know
    ///     <typeparamref name="TState" />.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    internal class StateManager<TInput, TState>
        : IStateManager<TInput>
    {
        private readonly TState state;
        private readonly AsyncConcurrencyStrategy<TInput, TState> strategy;

        public StateManager(AsyncConcurrencyStrategy<TInput, TState> strategy, TState state)
        {
            this.strategy = strategy;
            this.state = state;
        }

        public IReadOnlyList<AsyncToStart<TInput>> Admit(AsyncQueuedItem<TInput> incoming) =>
            this.strategy.Admit(state: this.state, incoming: incoming);

        public AsyncStrategyResult<TInput> OnCompleted(
            AsyncQueuedItem<TInput> item,
            AsyncCompletion completion) =>
            this.strategy.OnCompleted(state: this.state, item: item, completion: completion);
    }
}

/// <summary>
///     The one internal entry point for the public MapAsync surface of each language wrapper. It
///     connects an impure asynchronous operation to the FRP graph. It listens on a
///     Stream&lt;TInput&gt;, runs an async operation for each send, puts the result into a
///     StreamSink&lt;TResult&gt;, and gives the Queued items and the Running items. It can also
///     connect to streams that cancel Queued work and Running work. There is only this one method,
///     in its most general shape. Each wrapper holds the short overloads that remove the types, or
///     that connect TInput and TResult to the types of a strategy. Those are AsyncStreamExtensions
///     in SodaFlow.Async and the mapAsync family in SodaFlow.FSharp.Async. Each language has a
///     different short shape, and each wrapper has a different type for a value that the strategy
///     does not use.
/// </summary>
internal static class AsyncStreamUtility
{
    /// <summary>
    ///     Runs <paramref name="operation" /> for each send of <paramref name="source" />. It
    ///     sends a value that succeeded to <paramref name="results" /> and a value with an error
    ///     to <paramref name="errors" />. It needs no relation between
    ///     <typeparamref name="TInput" /> and <typeparamref name="TStrategyInput" />, because the
    ///     caller gives <paramref name="inputConverter" /> explicitly. Thus, the narrower
    ///     overloads of each wrapper give their own short shape only with the arguments here: an
    ///     identity converter where the types agree, and a constant converter where the strategy
    ///     does not use the value.
    /// </summary>
    /// <typeparam name="TInput">
    ///     The type in the source stream. It is the input for each call of
    ///     <paramref name="operation" />, the type in
    ///     <see cref="AsyncMapStatus{TInput}.Items" />, and the type that
    ///     <paramref name="cancelMatching" /> compares.
    /// </typeparam>
    /// <typeparam name="TResult">
    ///     The type that <paramref name="operation" /> gives when it succeeds. The pipeline sends
    ///     it to <paramref name="results" />.
    /// </typeparam>
    /// <typeparam name="TStrategyInput">
    ///     The input type of <paramref name="strategy" />. The compiler infers it from the type
    ///     of <paramref name="strategy" />, and no caller gives it explicitly. It needs no
    ///     inheritance relation to <typeparamref name="TInput" />, because
    ///     <paramref name="inputConverter" /> makes it explicitly.
    /// </typeparam>
    /// <param name="source">
    ///     The stream of inputs. The pipeline gives each send to <paramref name="strategy" />,
    ///     and the strategy starts it immediately or makes it wait. After a disposal of the status,
    ///     the pipeline ignores each subsequent send.
    /// </param>
    /// <param name="results">
    ///     This is necessary. The pipeline sends the return value of each operation that
    ///     succeeded here, in the sequence of their ends and not in the sequence of the inputs.
    ///     The pipeline does not send a result from a run that a different run replaced, or from a
    ///     run that a cancellation stopped. See <paramref name="strategy" />.
    /// </param>
    /// <param name="errors">
    ///     This is necessary. The pipeline sends each operation with an error here. There is no
    ///     call of this method with no destination for the errors.
    /// </param>
    /// <param name="operation">
    ///     The asynchronous work for each input. The pipeline calls it inline, thus it does not
    ///     go to a thread pool before its own await. It receives a CancellationToken that combines
    ///     the cancellation of this item with each token from the strategy. An operation that
    ///     obeys that token lets <paramref name="cancelAll" />,
    ///     <paramref name="cancelMatching" />, and <paramref name="cancelOnDispose" /> stop work
    ///     that started. An operation that ignores the token continues to its end, and the
    ///     cancellation then only stops the publication of its result. There is no overload with
    ///     no token.
    /// </param>
    /// <param name="strategy">
    ///     The control of requests that overlap. A strategy instance holds no state of its own,
    ///     and more than one MapAsync call can use the same instance safely, at the same time.
    ///     Each call gets its own new state manager, thus two pipelines never share a scheduling
    ///     state.
    /// </param>
    /// <param name="inputConverter">
    ///     Changes each <typeparamref name="TInput" /> value to the
    ///     <typeparamref name="TStrategyInput" /> of <paramref name="strategy" />, before the
    ///     admission.
    /// </param>
    /// <param name="cancelAll">
    ///     This is optional. Each send cancels each tracked operation, Queued or Running. The
    ///     pipeline does not start a canceled Queued item at its turn. A cancellation stops a
    ///     Running operation only if that operation monitors its CancellationToken.
    /// </param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations, Queued or Running, whose
    ///     input value is in the collection of that send. This uses the default equality comparer
    ///     for TInput. The limits of <paramref name="cancelAll" /> also apply here.
    /// </param>
    /// <param name="cancelOnDispose">
    ///     True when a disposal of the <see cref="AsyncMapStatus{TInput}" /> also cancels each
    ///     item that the pipeline tracks at that time, Queued or Running. The default is true. At
    ///     each value, a disposal always stops the admission of more values. This code sets the
    ///     value here, at the setup, and not as a parameter of Dispose, because
    ///     IDisposable.Dispose() is the only path to a disposal.
    ///     A disposal does not stop the output. Each operation that runs continues to its end
    ///     and publishes to <paramref name="results" /> or to <paramref name="errors" /> after the
    ///     call returns. With true that output is usually not important, because the pipeline
    ///     never publishes a canceled outcome and an operation that obeys its token makes none. An
    ///     operation that ignores its token makes one, and with false that output is the
    ///     purpose.
    /// </param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" />. IsRunning is a Cell&lt;bool&gt; that is true
    ///     while one call or more has the Running status, and a Queued item does not make it true.
    ///     It updates with no glitch, in the transaction of the event that changes it. Items gives
    ///     each tracked value with its status. A disposal of the status stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />, <paramref name="errors" />,
    ///     <paramref name="operation" />, or <paramref name="strategy" /> is null.
    /// </exception>
    internal static AsyncMapStatus<TInput> MapAsyncImpl<TInput, TResult, TStrategyInput>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        MapAsyncOperation<TInput, TResult> operation,
        AsyncConcurrencyStrategyBase<TStrategyInput> strategy,
        Func<TInput, TStrategyInput> inputConverter,
        Stream<UnitInternal>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (results is null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (strategy is null)
        {
            throw new ArgumentNullException(nameof(strategy));
        }

        return new AsyncMapExecutionManager<TInput, TResult, TStrategyInput>(
            strategy: strategy,
            inputConverter: inputConverter,
            results: results,
            errors: errors,
            operation: operation,
            cancelAll: cancelAll,
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose).Attach(source);
    }
}

/// <summary>
///     The face of a strategy that is not generic over <c>TState</c>. It is the type that
///     <see
///         cref="AsyncStreamUtility.MapAsyncImpl{TInput,TResult,TStrategyInput}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},MapAsyncOperation{TInput,TResult},AsyncConcurrencyStrategyBase{TStrategyInput},Func{TInput,TStrategyInput},Stream{UnitInternal},Stream{IReadOnlyCollection{TInput}},bool)" />
///     and its overloads accept. This keeps the <c>TState</c> of a strategy out of each MapAsync
///     signature. A caller and the execution engine see only
///     <see cref="AsyncConcurrencyStrategyBase{TInput}" />, and never
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" />. The internal constructor
///     prevents an external subclass. To write a custom strategy, subclass
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" />.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class AsyncConcurrencyStrategyBase<TInput>
    : AsyncMapBase
{
    internal AsyncConcurrencyStrategyBase()
    {
    }

    /// <summary>
    ///     Makes the <see cref="AsyncMapBase.IStateManager{TInput}" /> for one MapAsync
    ///     call. See <see cref="AsyncConcurrencyStrategy{TInput,TState}.CreateState" />.
    /// </summary>
    internal abstract IStateManager<TInput> CreateStateManager();
}

/// <summary>
///     The base class of a MapAsync scheduling strategy, which is the admission and the sequence
///     of a stream of async requests. A strategy answers two questions, and each answer is data.
///     <see cref="Admit" /> answers "which items start now for this new tracked value?" and
///     <see cref="OnCompleted" /> answers "which items start next for this outcome, and does the
///     pipeline publish it?". A strategy answers only from a <typeparamref name="TState" /> that
///     it controls. A strategy instance holds no state of its own, because each value that
///     changes is in <typeparamref name="TState" />. <see cref="CreateState" /> makes one instance
///     of that state for each MapAsync call. Thus, more than one MapAsync call can use the same
///     strategy instance safely, at the same time. The execution engine (see
///     <see
///         cref="AsyncStreamUtility.MapAsyncImpl{TInput,TResult,TStrategyInput}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},MapAsyncOperation{TInput,TResult},AsyncConcurrencyStrategyBase{TStrategyInput},Func{TInput,TStrategyInput},Stream{UnitInternal},Stream{IReadOnlyCollection{TInput}},bool)" />
///     ) holds the
///     <typeparamref name="TState" /> of each call, and it is the only code that gives that state
///     back to the strategy. <see cref="Admit" /> and <see cref="OnCompleted" /> cannot use the
///     result sink, the error sink, or a Task, and cannot start a Task. The two methods give a
///     description of the necessary operations, and the execution engine does them. The one
///     imperative operation is <see cref="AsyncMapBase.AsyncQueuedItem{TInput}.Cancel" />, which
///     cancels an item that the strategy controls. It publishes nothing and starts nothing. It
///     goes to the cancellation path of an external cancelAll stream, thus the item ends through
///     <see cref="OnCompleted" /> as each other item does. The identity of an item at its end is
///     the item that the strategy got in <see cref="Admit" />. There is no second handle. Keep the
///     item in <typeparamref name="TState" /> to identify it again after this.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class AsyncConcurrencyStrategy<TInput, TState>
    : AsyncConcurrencyStrategyBase<TInput>
{
    internal override IStateManager<TInput> CreateStateManager() =>
        new StateManager<TInput, TState>(strategy: this, state: this.CreateState());

    /// <summary>
    ///     Makes a new scheduling state for one MapAsync call. The engine calls this one time for
    ///     each call. See
    ///     <see
    ///         cref="AsyncStreamUtility.MapAsyncImpl{TInput,TResult,TStrategyInput}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},MapAsyncOperation{TInput,TResult},AsyncConcurrencyStrategyBase{TStrategyInput},Func{TInput,TStrategyInput},Stream{UnitInternal},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     Thus two pipelines with the same strategy instance never read the state of the other,
    ///     also when the two run at the same time.
    /// </summary>
    protected abstract TState CreateState();

    /// <summary>
    ///     Gives the items to start now for a new admitted value. The pipeline tracks that value
    ///     with the Queued status. See <see cref="AsyncMapBase.AsyncQueuedItem{TInput}" />. The
    ///     engine always calls this in a SodaFlow transaction. See
    ///     <see cref="AsyncMapExecutionManager{TInput,TResult,TStrategyInput}" />
    ///     for the cause that makes a change to <paramref name="state" /> here safe with no
    ///     explicit lock. Return an <see cref="AsyncMapBase.AsyncToStart{TInput}" /> with
    ///     <paramref name="incoming" /> to start it immediately. Without that, the item keeps the
    ///     Queued status. To start a Queued item after this, keep <paramref name="incoming" /> in
    ///     <paramref name="state" />, and not only its Value. That keeps its identity and its
    ///     cancellation during the wait, and it is the value that <see cref="OnCompleted" /> gives
    ///     back.
    /// </summary>
    protected internal abstract IReadOnlyList<AsyncToStart<TInput>> Admit(
        TState state,
        AsyncQueuedItem<TInput> incoming);

    /// <summary>
    ///     Gives two answers for the outcome of an item at its end: if the pipeline publishes
    ///     that outcome, and which tracked items start now because of it. For example, the next
    ///     Queued item can start. The engine always calls this in a SodaFlow transaction, as it
    ///     calls <see cref="Admit" />. Each
    ///     <see cref="AsyncMapBase.AsyncToStart{TInput}" /> from this method must contain an
    ///     <see cref="AsyncMapBase.AsyncQueuedItem{TInput}" /> from a previous
    ///     <see cref="Admit" /> call. There is no path for a value with no admission.
    ///     <paramref name="item" /> is the instance that this strategy got for this value in
    ///     <see cref="Admit" />. It is the same instance, thus ReferenceEquals against an item in
    ///     <paramref name="state" /> tells you if that item is the current run. The pipeline never
    ///     publishes a canceled outcome, at each return value from this method. A cancellation is
    ///     always an expected end with no message. Its source is external code, a strategy that
    ///     replaces its own previous run, or a Queued item that a cancellation removes before its
    ///     turn.
    /// </summary>
    protected internal abstract AsyncStrategyResult<TInput> OnCompleted(
        TState state,
        AsyncQueuedItem<TInput> item,
        AsyncCompletion completion);
}

/// <summary>
///     The container of the strategies in the library: Parallel, Queue, QueuePerGroup, and
///     SwitchLatest. Each one uses only the schedule, and not the <c>TInput</c> or the
///     <c>TResult</c> of the call. Thus, each one is generic over a <c>TUnit</c> from the caller.
///     QueuePerGroup is generic over the input type also, because it calculates a group key from
///     the input. No strategy has one fixed type for a value that it does not use. Thus, one
///     shared implementation of the schedule serves each language wrapper, and each wrapper gives
///     the type that is natural for it as <c>TUnit</c>. The C# wrapper gives
///     <c>SodaFlow.Functional.Unit</c>, and the F# wrapper gives its own <c>unit</c>. The
///     alternatives are a public type in Core, which is not possible because Core has no
///     dependency on SodaFlow.Functional, or the same logic in each wrapper.
///     This class is internal and not public. Each method here is generic, thus the class does not
///     be. An internal class lets each language wrapper build its own public interface with its
///     own types above it, and does not show this generic surface. The wrappers are
///     SodaFlow.Async and SodaFlow.FSharp.Async, and the two are friend assemblies through IVT.
///     The name is different from <see cref="AsyncConcurrencyStrategy{TInput,TState}" />,
///     and this class is not a subclass of that class. F# does not always resolve a bare type
///     name across two generic arities of the same name, also with a full qualification. The
///     short hierarchy in the C# wrapper has the same name and is correct, because a type in the
///     consuming assembly has priority above a type of the same name from a reference. The F#
///     wrapper has no type of its own for that priority. Thus, this class keeps a name that is not
///     ambiguous, and does not overload "AsyncConcurrencyStrategy" by arity. The nested
///     strategies below never have that ambiguity, because they always name
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" /> with its three type
///     arguments, and that gives the arity at each other use of the name.
/// </summary>
internal static class AsyncConcurrencyStrategyFactory
{
    /// <summary>Each send starts its own operation immediately. The results come in the sequence
    /// of their ends.</summary>
    /// <typeparam name="TUnit">
    ///     The type of the calling wrapper for a value that this strategy does not use. It
    ///     replaces the input type and the result type. The C# wrapper gives
    ///     <c>SodaFlow.Functional.Unit</c>, and the F# wrapper gives its own <c>unit</c>. This
    ///     strategy never reads a value of it.
    /// </typeparam>
    /// <param name="unitValue">
    ///     The one value of <typeparamref name="TUnit" />. It is the state of this strategy for
    ///     each call, and no code reads it.
    /// </param>
    /// <returns>A strategy instance with no state, for use by more than one call.</returns>
    internal static AsyncConcurrencyStrategyBase<TUnit> Parallel<TUnit>(TUnit unitValue) =>
        new ParallelStrategy<TUnit>(unitValue);

    /// <summary>One operation or no operation runs at a time. A subsequent send goes to the
    /// queue, and the queue runs in sequence.</summary>
    /// <typeparam name="TUnit">
    ///     The type of the calling wrapper for a value that this strategy does not use. See
    ///     <see cref="Parallel{TUnit}" />.
    /// </typeparam>
    /// <returns>A strategy instance with no state, for use by more than one call.</returns>
    internal static AsyncConcurrencyStrategyBase<TUnit> Queue<TUnit>() => QueueStrategy<TUnit>.Instance;

    /// <summary>
    ///     Builds a strategy with one queue for each group. <paramref name="getGroup" /> puts
    ///     each input in a group. In one group, a subsequent send goes behind the sends before it,
    ///     as <see cref="Queue" /> does. Two different groups do not wait for each other.
    /// </summary>
    /// <typeparam name="TUnit">
    ///     The type of the calling wrapper for a value that this strategy does not use. It
    ///     replaces the result type only, because this strategy uses its input to calculate a
    ///     group key. See <see cref="Parallel{TUnit}" />.
    /// </typeparam>
    /// <typeparam name="TInput">The input type of <paramref name="getGroup" />.</typeparam>
    /// <typeparam name="TGroup">The type of the group key.</typeparam>
    /// <param name="getGroup">
    ///     Calculates the group key of an input value. It must be deterministic, because the
    ///     strategy calls it at the admission of the value and again at its end. The two calls
    ///     must agree, or the strategy cannot find the queue of the item.
    /// </param>
    /// <param name="groupComparer">
    ///     An optional equality comparer for the group keys. The default is
    ///     <see cref="EqualityComparer{TGroup}.Default" />.
    /// </param>
    /// <returns>A strategy instance with no state, for use by more than one call.</returns>
    internal static AsyncConcurrencyStrategyBase<TInput> QueuePerGroup<TUnit, TInput, TGroup>(
        Func<TInput, TGroup> getGroup,
        IEqualityComparer<TGroup>? groupComparer = null)
        where TGroup : notnull =>
        new QueuePerGroupStrategy<TUnit, TInput, TGroup>(getGroup: getGroup, groupComparer: groupComparer);

    /// <summary>A new send cancels the operation that runs and replaces it.</summary>
    /// <typeparam name="TUnit">
    ///     The type of the calling wrapper for a value that this strategy does not use. See
    ///     <see cref="Parallel{TUnit}" />.
    /// </typeparam>
    /// <returns>A strategy instance with no state, for use by more than one call.</returns>
    internal static AsyncConcurrencyStrategyBase<TUnit> SwitchLatest<TUnit>() =>
        SwitchLatestStrategy<TUnit>.Instance;

    private sealed class ParallelStrategy<TUnit>
        : AsyncConcurrencyStrategy<TUnit, TUnit>
    {
        private readonly TUnit unitValue;

        public ParallelStrategy(TUnit unitValue) => this.unitValue = unitValue;

        protected override TUnit CreateState() => this.unitValue;

        protected internal override IReadOnlyList<AsyncToStart<TUnit>> Admit(
            TUnit state,
            AsyncQueuedItem<TUnit> incoming) =>
            new[] { new AsyncToStart<TUnit>(incoming) };

        protected internal override AsyncStrategyResult<TUnit> OnCompleted(
            TUnit state,
            AsyncQueuedItem<TUnit> item,
            AsyncCompletion completion) =>
            new(publish: true, next: AsyncStrategyResult<TUnit>.None);
    }

    private sealed class QueueStrategy<TUnit>
        : AsyncConcurrencyStrategy<TUnit, QueueStrategy<TUnit>.State>
    {
        internal static readonly QueueStrategy<TUnit> Instance = new();

        private QueueStrategy()
        {
        }

        protected override State CreateState() => new();

        protected internal override IReadOnlyList<AsyncToStart<TUnit>> Admit(
            State state,
            AsyncQueuedItem<TUnit> incoming)
        {
            if (state.Busy)
            {
                // The item keeps the Queued status, and a cancellation can remove it during the
                // wait.
                state.Pending.Enqueue(incoming);

                return AsyncStrategyResult<TUnit>.None;
            }

            state.Busy = true;

            return new[] { new AsyncToStart<TUnit>(incoming) };
        }

        protected internal override AsyncStrategyResult<TUnit> OnCompleted(
            State state,
            AsyncQueuedItem<TUnit> item,
            AsyncCompletion completion)
        {
            if (state.Pending.Count > 0)
            {
                AsyncQueuedItem<TUnit> next = state.Pending.Dequeue();

                // When a cancellation removed `next` during its wait here, the execution engine
                // finds that at the promotion and goes directly to Outcome.Canceled(). That calls
                // OnCompleted again, and OnCompleted then takes the next item from the queue.
                return new AsyncStrategyResult<TUnit>(
                    publish: true,
                    next: new[] { new AsyncToStart<TUnit>(next) });
            }

            state.Busy = false;

            return new AsyncStrategyResult<TUnit>(publish: true, next: AsyncStrategyResult<TUnit>.None);
        }

        /// <summary>The item with the Running status, when there is one, and the items that wait
        /// behind it.</summary>
        public sealed class State
        {
            internal readonly Queue<AsyncQueuedItem<TUnit>> Pending = new();
            internal bool Busy;
        }
    }

    /// <summary>
    ///     One queue for each group, in the style of <see cref="QueueStrategy{TUnit}" />. Each
    ///     queue operates independently. A group selector puts each input in a group.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class QueuePerGroupStrategy<TUnit, TInput, TGroup>
        : AsyncConcurrencyStrategy<TInput, QueuePerGroupStrategy<TUnit, TInput, TGroup>.State>
        where TGroup : notnull
    {
        private readonly Func<TInput, TGroup> getGroup;
        private readonly IEqualityComparer<TGroup>? groupComparer;

        public QueuePerGroupStrategy(Func<TInput, TGroup> getGroup, IEqualityComparer<TGroup>? groupComparer)
        {
            this.getGroup = getGroup;
            this.groupComparer = groupComparer;
        }

        protected override State CreateState() => new(this.groupComparer);

        protected internal override IReadOnlyList<AsyncToStart<TInput>> Admit(
            State state,
            AsyncQueuedItem<TInput> incoming)
        {
            TGroup group = this.getGroup(incoming.Value);

            if (!state.Groups.TryGetValue(key: group, value: out GroupState? groupState))
            {
                // This is the first value of this group, and it gets its own queue. OnCompleted
                // removes that queue when the group becomes empty.
                groupState = new GroupState();
                state.Groups.Add(key: group, value: groupState);
            }

            if (groupState.Busy)
            {
                // The item keeps the Queued status, and a cancellation can remove it during the
                // wait.
                groupState.Pending.Enqueue(incoming);

                return AsyncStrategyResult<TInput>.None;
            }

            groupState.Busy = true;

            return new[] { new AsyncToStart<TInput>(incoming) };
        }

        protected internal override AsyncStrategyResult<TInput> OnCompleted(
            State state,
            AsyncQueuedItem<TInput> item,
            AsyncCompletion completion)
        {
            TGroup group = this.getGroup(item.Value);

            if (!state.Groups.TryGetValue(key: group, value: out GroupState? groupState))
            {
                throw new InvalidOperationException("Could not find group.");
            }

            if (groupState.Pending.Count > 0)
            {
                AsyncQueuedItem<TInput> next = groupState.Pending.Dequeue();

                // When a cancellation removed `next` during its wait here, the execution engine
                // finds that at the promotion and goes directly to Outcome.Canceled(). That calls
                // OnCompleted again, and OnCompleted then takes the next item from the queue.
                return new AsyncStrategyResult<TInput>(
                    publish: true,
                    next: new[] { new AsyncToStart<TInput>(next) });
            }

            groupState.Busy = false;
            state.Groups.Remove(group);

            return new AsyncStrategyResult<TInput>(publish: true, next: AsyncStrategyResult<TInput>.None);
        }

        /// <summary>The queue state of each group, with the group as the key. This code adds a
        /// group at its first use and removes it when the group becomes empty.</summary>
        public sealed class State
        {
            internal readonly Dictionary<TGroup, GroupState> Groups;

            public State(IEqualityComparer<TGroup>? groupComparer) =>
                this.Groups = new Dictionary<TGroup, GroupState>(groupComparer);
        }

        internal sealed class GroupState
        {
            internal readonly Queue<AsyncQueuedItem<TInput>> Pending = new();
            internal bool Busy;
        }
    }

    private sealed class SwitchLatestStrategy<TUnit>
        : AsyncConcurrencyStrategy<TUnit, SwitchLatestStrategy<TUnit>.State>
    {
        internal static readonly SwitchLatestStrategy<TUnit> Instance = new();

        private SwitchLatestStrategy()
        {
        }

        protected override State CreateState() => new();

        protected internal override IReadOnlyList<AsyncToStart<TUnit>> Admit(
            State state,
            AsyncQueuedItem<TUnit> incoming)
        {
            // This cancels the item that the new item replaces, through the cancellation of that
            // item. This code makes no second CancellationTokenSource, and holds and disposes
            // none. The call is safe when that item ended before now.
            state.Active?.Cancel();
            state.Active = incoming;

            return new[] { new AsyncToStart<TUnit>(incoming) };
        }

        protected internal override AsyncStrategyResult<TUnit> OnCompleted(
            State state,
            AsyncQueuedItem<TUnit> item,
            AsyncCompletion completion)
        {
            // Publish only when no newer run replaced this run.
            bool isCurrent = state.Active != null && state.Active.Id == item.Id;

            // This removes the reference at the end of the current run. Thus, the last QueuedItem,
            // and its value, do not stay in memory after the pipeline becomes empty.
            if (isCurrent)
            {
                state.Active = null;
            }

            return new AsyncStrategyResult<TUnit>(publish: isCurrent, next: AsyncStrategyResult<TUnit>.None);
        }

        /// <summary>The item that runs, when there is one. Each new send replaces it.</summary>
        public sealed class State
        {
            internal AsyncQueuedItem<TUnit>? Active;
        }
    }
}

/// <summary>
///     Runs one MapAsync pipeline. It starts the operations, catches the exceptions, sends the
///     results and the errors, tracks the Queued items and the Running items, connects the
///     external cancellation, and controls the transaction limits. The external cancellation
///     includes a cancellation before a start. A
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" /> does none of this. A
///     strategy answers Admit and OnCompleted with data only, and this class does the operations
///     in that answer. This code makes one instance for each MapAsync call (see
///     <see
///         cref="AsyncStreamUtility.MapAsyncImpl{TInput,TResult,TStrategyInput}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},MapAsyncOperation{TInput,TResult},AsyncConcurrencyStrategyBase{TStrategyInput},Func{TInput,TStrategyInput},Stream{UnitInternal},Stream{IReadOnlyCollection{TInput}},bool)" />
///     ), and that instance holds the one state of the call. This code makes that state at the
///     start and never shares it between two calls. Thus, more than one call can use the same
///     strategy instance safely, at the same time. This class and
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" /> share
///     <see cref="AsyncMapBase" /> only to get the nested data types of that base, such as
///     <see cref="AsyncMapBase.AsyncQueuedItem{TInput}" />. This class is not a subtype of the
///     strategy, and the two have no other relation.
///     In this class, each value that the strategy reads has the type
///     <typeparamref name="TStrategyInput" /> and not <typeparamref name="TInput" />. Those
///     values are the tracked items and ToStart. Only a <typeparamref name="TInput" /> value
///     comes in, from <c>source</c>, and only a <typeparamref name="TResult" /> value goes out,
///     to <c>results</c>. The conversion at the input edge is the work of this class and not of
///     the strategy: <see cref="inputConverter" />. That converter usually has no inverse, thus
///     this code never calculates the initial <typeparamref name="TInput" /> value again from the
///     converted value. It keeps the two values together. See <see cref="Entry" /> and the
///     <c>value</c> parameter that goes through <see cref="PromoteAndLaunch" /> into
///     <see cref="StartOperation" />.
///     The strategy reads no result at all. It reads <see cref="AsyncMapBase.AsyncCompletion" />,
///     which says how the operation ended, and <see cref="Complete" /> asks it before this class
///     makes the result. See <see cref="Publish" />.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class AsyncMapExecutionManager<TInput, TResult, TStrategyInput> : AsyncMapBase
{
    private static readonly Mutation NoMutation =
        new(
            remove: Array.Empty<Guid>(),
            promote: Array.Empty<Guid>(),
            add: Array.Empty<Entry>());

    private readonly Stream<UnitInternal>? cancelAll;

    private readonly Stream<IReadOnlyCollection<TInput>>? cancelMatching;

    // This code sets this at the setup, and only Dispose reads it. See the cancelOnDispose
    // parameter of MapAsync. No code changes it after Attach, thus it needs no transaction and no
    // Interlocked operation.
    private readonly bool cancelOnDispose;

    // Dispose always sends this in a transaction. It uses the Snapshot(tracked) and Cancel()
    // pattern of a cancelAll stream from the caller. Attach always connects it, and a cancelAll
    // stream from the caller does not change that.
    private readonly StreamSink<UnitInternal> disposeCancelTrigger = StreamInternal.CreateSinkImpl<UnitInternal>();

    private readonly StreamSink<Exception> errors;

    private readonly Func<TInput, TStrategyInput> inputConverter;

    // This carries the edits to the list of tracked items: an add, a promotion, and a removal.
    // The source is the transform of Map, which is not a registered listener callback (see
    // Attach), or code that is not in a SodaFlow callback, such as a continuation on a background
    // thread. Send is legal in the two conditions.
    private readonly StreamSink<Mutation> mutations =
        StreamInternal.CreateSinkImpl<Mutation>(CombineMutations);

    private readonly MapAsyncOperation<TInput, TResult> operation;

    private readonly StreamSink<TResult> results;

    // This code makes this at the start, as the class remarks give, and never replaces it. No
    // lock protects it, and no lock is necessary. Only code in a SodaFlow transaction uses it:
    // the Map in Attach, and the Transaction.RunVoid in Complete. SodaFlow puts each transaction
    // in the process in sequence behind one global lock, thus one transaction or no transaction
    // runs at a time, on each thread. This code uses that guarantee of one transaction
    // at a time. A SodaFlow implementation with no such guarantee makes a lock necessary for this
    // state.
    private readonly IStateManager<TStrategyInput> stateManager;

    // These fields hold the strong reference that each Listen subscription needs to stay
    // attached. Listen keeps only a weak reference on the side of the source stream, and Attach
    // gives the cause. While code can get to this execution manager, these fields keep the
    // subscriptions. When no code references the manager, these fields go with it and the
    // subscriptions stop. Dispose also calls Unlisten on them, to disconnect them immediately
    // and deterministically, and does not wait for the GC.
    private IListener? cancelAllListener;

    private IListener? cancelMatchingListener;

    private IListener? disposeCancelListener;

    // 0 is active and 1 is disposed. This makes a second call of Dispose safe. The disposed
    // field above is different, because each thread can call Dispose with no transaction open.
    // Thus, this field needs its own thread-safe test with Interlocked, and does not use the
    // sequence that SodaFlow gives.
    private int disposeState;

    // This stops a new admission after a disposal. Only code in a SodaFlow transaction reads it
    // and writes it, in Attach and in Dispose. Thus, it uses the guarantee of one transaction at a
    // time, as each other field in this class does, and a volatile field is not necessary.
    private bool disposed;

    internal AsyncMapExecutionManager(
        AsyncConcurrencyStrategyBase<TStrategyInput> strategy,
        Func<TInput, TStrategyInput> inputConverter,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        MapAsyncOperation<TInput, TResult> operation,
        Stream<UnitInternal>? cancelAll,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching,
        bool cancelOnDispose)
    {
        this.inputConverter = inputConverter;
        this.stateManager = strategy.CreateStateManager();
        this.results = results;
        this.errors = errors;
        this.operation = operation;
        this.cancelAll = cancelAll;
        this.cancelMatching = cancelMatching;
        this.cancelOnDispose = cancelOnDispose;
    }

    internal AsyncMapStatus<TInput> Attach(Stream<TInput> source)
    {
        Cell<Entry[]> trackedCell =
            TransactionInternal.Apply((trans, _) =>
            {
                LoopedCell<Dictionary<Guid, Entry>> entryByIdCellLoop = new();

                // Map runs as usual transaction code and is not a registered listener callback.
                // Thus, the SodaFlow rule against Send in a callback does not apply to it, and
                // it runs in the transaction of the source. The pipeline tracks each admitted
                // value from this moment. It adds the value with the Queued status, and then
                // promotes it to Running for each ToStart that Admit returns. For the strategies
                // in the library, that is usually the value itself. After a disposal this code
                // does nothing permanently, because no code calls Admit again and thus no value
                // goes to the queue or starts.
                Stream<Mutation> starts =
                    source
                        .SnapshotImpl(
                            c: entryByIdCellLoop,
                            f: static (value, entryById) => (Value: value, EntryById: entryById))
                        .MapImpl(o =>
                        {
                            if (this.disposed)
                            {
                                return NoMutation;
                            }

                            CancellationTokenSource cancellation = new();

                            Guid newEntryId = Guid.NewGuid();

                            // Here inputConverter changes TInput into TStrategyInput. This is the
                            // input edge, and the class remarks give more. The newEntry below
                            // keeps the initial TInput, because the result of inputConverter
                            // usually has no inverse.
                            AsyncQueuedItem<TStrategyInput> incoming =
                                new(
                                    value: this.inputConverter(o.Value),
                                    id: newEntryId,
                                    cancellation: cancellation);

                            Entry newEntry =
                                new(
                                    item: new AsyncQueuedItem<TInput>(
                                        value: o.Value,
                                        id: newEntryId,
                                        cancellation: cancellation),
                                    status: AsyncItemStatus.Queued,
                                    value: o.Value);

                            IReadOnlyList<AsyncToStart<TStrategyInput>> toStart =
                                this.stateManager.Admit(incoming: incoming);

                            Guid[] promote = new Guid[toStart.Count];

                            for (int i = 0; i < toStart.Count; i++)
                            {
                                promote[i] = toStart[i].Item.Id;

                                TInput value;

                                if (newEntryId == promote[i])
                                {
                                    value = o.Value;
                                }
                                else if (o.EntryById.TryGetValue(key: promote[i], value: out Entry? entry))
                                {
                                    value = entry.Value;
                                }
                                else
                                {
                                    throw new InvalidOperationException("Could not find item to start.");
                                }

                                this.PromoteAndLaunch(
                                    toStart: toStart[i],
                                    value: value,
                                    entryByIdCell: entryByIdCellLoop);
                            }

                            return new Mutation(
                                remove: Array.Empty<Guid>(),
                                promote: promote,
                                add: new[] { newEntry });
                        });

                Cell<Entry[]> trackedCell =
                    starts
                        .MergeImpl(s: this.mutations, f: CombineMutations)
                        .AccumImpl(
                            initialState: Array.Empty<Entry>(),
                            f: static (mutation, list) => Apply(list: list, mutation: mutation));

                Cell<Dictionary<Guid, Entry>> entryByIdCell =
                    trackedCell.MapImpl(static tracked => tracked.ToDictionary(static e => e.Item.Id));

                entryByIdCellLoop.Loop(trans: trans, c: entryByIdCell);

                return trackedCell;
            });

        // Snapshot puts each send on a cancellation stream with the value of `tracked` from
        // the start of that transaction. Thus, a cancellation and an admission in one transaction
        // cannot race each other. Cancel() is a usual BCL call and not a SodaFlow send(), thus a
        // listener callback can call it. This applies to a Queued item and to a Running item in
        // the same manner, because each tracked entry has its own CancellationTokenSource from
        // its admission, at each status.
        //
        // This code uses Listen and not ListenStrong. The caller gives cancelAll and
        // cancelMatching, and those streams can continue after one MapAsync call. For example,
        // one "Cancel" stream can serve a full view. With ListenStrong, the source stream keeps
        // this pipeline in memory permanently. That pipeline is this execution manager, the
        // result sink, the error sink, and each object that `tracked` reaches. It stays in memory
        // at each state of the references from the caller to IsRunning and Items, and Dispose
        // becomes the only path to release it. With Listen, the subscription continues only while
        // other code keeps this execution manager reachable, which is usually the caller with
        // IsRunning and Items. When no code keeps it, the GC can collect the full graph and this
        // callback stops. This is a protection against a Dispose that a caller forgets, and not a
        // replacement for that call. The time of a GC is not deterministic, and a GC does nothing
        // for a Task that runs. Such a Task continues to its end, at each state of the listeners
        // for a cancellation.
        if (this.cancelAll != null)
        {
            this.cancelAllListener =
                this.cancelAll
                    .SnapshotImpl(c: trackedCell, f: static (_, entries) => entries)
                    .ListenImpl(static entries =>
                    {
                        foreach (Entry e in entries)
                        {
                            e.Item.Cancellation.Cancel();
                        }
                    });
        }

        if (this.cancelMatching != null)
        {
            this.cancelMatchingListener =
                this.cancelMatching
                    .SnapshotImpl(
                        c: trackedCell,
                        f: static (toCancel, entries) => (ToCancel: toCancel, Entries: entries))
                    .ListenImpl(static pair =>
                    {
                        if (pair.ToCancel.Count == 0)
                        {
                            return;
                        }

                        // This compares against the initial TInput value of each tracked Entry,
                        // which is Entry.Value, with the default equality comparer for TInput.
                        // cancelMatching uses TInput and not TStrategyInput, thus this code needs
                        // no conversion.
                        HashSet<TInput> targets = new(pair.ToCancel);

                        // ReSharper disable once LoopCanBePartlyConvertedToQuery - Done for performance reasons.
                        foreach (Entry e in pair.Entries)
                        {
                            if (targets.Contains(e.Item.Value))
                            {
                                e.Item.Cancellation.Cancel();
                            }
                        }
                    });
        }

        // This code always connects this, and a cancelAll stream from the caller does not change
        // that. A Dispose() with cancelOnDispose true sends into it. This also uses Listen, for
        // consistency, but the selection is less important here. disposeCancelTrigger is a field
        // of this class, thus this pair is always in the reference graph of this execution
        // manager, and the .NET GC collects a cycle that no code can get to, with ListenStrong
        // and with Listen.
        this.disposeCancelListener =
            this.disposeCancelTrigger
                .SnapshotImpl(c: trackedCell, f: static (_, entries) => entries)
                .ListenImpl(static entries =>
                {
                    foreach (Entry e in entries)
                    {
                        e.Item.Cancellation.Cancel();
                    }
                });

        Cell<bool> isRunning =
            trackedCell.MapImpl(static entries =>
                Array.Exists(array: entries, match: static e => e.Status == AsyncItemStatus.Running));

        Cell<IReadOnlyList<AsyncItem<TInput>>> items =
            trackedCell.MapImpl<IReadOnlyList<AsyncItem<TInput>>>(static entries =>
                Array.ConvertAll(
                    array: entries,
                    converter: static e => new AsyncItem<TInput>(value: e.Item.Value, status: e.Status)));

        return new AsyncMapStatus<TInput>(isRunning: isRunning, items: items, dispose: this.Dispose);
    }

    // This is the one limit in this class where the code starts a Task and does not wait for it.
    // The try, the catch, and the finally in StartOperation cover all conditions, thus its Task
    // always ends correctly and no code here uses its result. A fault is thus a defect in this
    // class, and not a result of a call. This code throws it again, with its initial stack
    // record, and does not discard it.
    //
    // See the result of that second throw. The TPL puts an exception from a continuation delegate
    // into the Task of that continuation, and never sends it to the thread that ended the first
    // Task. This code discards that Task, thus the exception becomes an unobserved task exception
    // in each condition, and the second throw moves it one Task forward. It goes to a
    // TaskScheduler.UnobservedTaskException handler and to a debugger, and it does not stop the
    // process. For a stop of the process at a defect, send it through
    // ThreadPool.QueueUserWorkItem.
    private static void FireAndForget(Task task) =>
        task.ContinueWith(
            // ReSharper disable once NullableWarningSuppressionIsUsed - This continuation has
            // TaskContinuationOptions.OnlyOnFaulted set, so the task's Exception property will be non-null.
            continuationAction: static t => ExceptionDispatchInfo.Capture(t.Exception!.GetBaseException()).Throw(),
            cancellationToken: CancellationToken.None,
            continuationOptions: TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            scheduler: TaskScheduler.Default);

    // This has two uses. It is the coalesce function of mutations in one transaction, which is
    // necessary because Complete can call itself (see the remarks of Complete) and can thus make
    // more than one Send() to this sink in one transaction. It is also the merge function of
    // starts.Merge(mutations, ...) in Attach, for the different condition where starts and
    // mutations send in the same transaction. For example, the Admit of a strategy promotes a
    // previous item that a cancellation removed and that the strategy holds, with the new item
    // that it admits.
    private static Mutation CombineMutations(Mutation a, Mutation b) =>
        new(
            remove: Concat(a: a.Remove, b: b.Remove),
            promote: Concat(a: a.Promote, b: b.Promote),
            add: Concat(a: a.Add, b: b.Add));

    private static Entry[] Apply(Entry[] list, Mutation mutation)
    {
        if (mutation.Remove.Length > 0)
        {
            Entry[] kept = new Entry[list.Length];
            int count = 0;

            // ReSharper disable once LoopCanBePartlyConvertedToQuery - Done for performance reasons.
            foreach (Entry e in list)
            {
                bool remove = mutation.Remove.Any(itemToRemove => e.Item.Id == itemToRemove);

                if (!remove)
                {
                    kept[count++] = e;
                }
            }

            if (count != list.Length)
            {
                Array.Resize(array: ref kept, newSize: count);
                list = kept;
            }
        }

        if (mutation.Add.Length > 0)
        {
            list = Concat(a: list, b: mutation.Add);
        }

        if (mutation.Promote.Length > 0)
        {
            Entry[]? updated = null;

            // ReSharper disable once LoopCanBePartlyConvertedToQuery - Done for performance reasons.
            foreach (Guid idToPromote in mutation.Promote)
            {
                int idx =
                    Array.FindIndex(
                        array: list,
                        match: e => e.Item.Id == idToPromote);

                if (idx >= 0 && list[idx].Status != AsyncItemStatus.Running)
                {
                    if (updated == null)
                    {
                        updated = new Entry[list.Length];
                        Array.Copy(sourceArray: list, destinationArray: updated, length: list.Length);
                    }

                    updated[idx] = updated[idx].WithStatus(AsyncItemStatus.Running);
                }
            }

            if (updated != null)
            {
                list = updated;
            }
        }

        return list;
    }

    private static T[] Concat<T>(T[] a, T[] b)
    {
        if (a.Length == 0)
        {
            return b;
        }

        if (b.Length == 0)
        {
            return a;
        }

        T[] result = new T[a.Length + b.Length];
        Array.Copy(sourceArray: a, destinationArray: result, length: a.Length);

        Array.Copy(
            sourceArray: b,
            sourceIndex: 0,
            destinationArray: result,
            destinationIndex: a.Length,
            length: b.Length);

        return result;
    }

    /// <summary>
    ///     Stops this pipeline. See <see cref="AsyncMapStatus.Dispose" /> for the full
    ///     contract. The cancelOnDispose value at Attach sets the cancellation of the tracked
    ///     items, and this method has no parameter for it, because IDisposable.Dispose() is the
    ///     only public path to a disposal. <see cref="disposeState" /> makes this method run one
    ///     time, because each thread can call it with no SodaFlow transaction open.
    /// </summary>
    private void Dispose()
    {
        if (Interlocked.CompareExchange(location1: ref this.disposeState, value: 1, comparand: 0) != 0)
        {
            return;
        }

        TransactionInternal.RunImpl(() =>
        {
            this.disposed = true;

            if (this.cancelOnDispose)
            {
                this.disposeCancelTrigger.SendImpl(UnitInternal.Value);
            }

            return UnitInternal.Value;
        });

        this.cancelAllListener?.Unlisten();
        this.cancelMatchingListener?.Unlisten();
        this.disposeCancelListener?.Unlisten();
    }

    private void PromoteAndLaunch(
        AsyncToStart<TStrategyInput> toStart,
        TInput value,
        Cell<Dictionary<Guid, Entry>> entryByIdCell)
    {
        if (toStart.Item.Cancellation.IsCancellationRequested)
        {
            // A cancellation removed this item with the Queued status. This code ends it
            // immediately and does not call the operation. It uses the usual end path, thus a
            // strategy such as Queue in the library starts the next item.
            // Post defers this, as it defers the usual start below. Complete opens its own
            // transaction with TransactionInternal.RunImpl, and this code can run synchronously
            // in the transaction that processes the admission. A strategy can cancel incoming and
            // return it as a ToStart in the same Admit call, and not only in a subsequent Admit
            // call or OnCompleted call in a different transaction. A call of Complete inline here
            // puts a transaction in an open transaction, and that throws "Send may not be called
            // inside a callback."
            TransactionInternal.PostImpl(() =>
                this.Complete(
                    item: toStart.Item,
                    pending: AsyncOutcome<ResultConstructor<TResult>>.Canceled(),
                    entryByIdCell: entryByIdCell,
                    tokenToCheck: null));

            return;
        }

        // This code defers only the call of the operation. The transaction that promoted the
        // entry gave it the Running status synchronously. Post prevents the start of asynchronous work,
        // which is a side effect, in the transaction that processes the event. StartOperation is
        // a usual async Task method. FireAndForget is the one position where this code does not
        // await its Task, and that is explicit and not a discard in an async void method.
        TransactionInternal.PostImpl(() =>
            FireAndForget(this.StartOperation(toStart: toStart, value: value, entryByIdCell: entryByIdCell)));
    }

    private async Task StartOperation(
        AsyncToStart<TStrategyInput> toStart,
        TInput value,
        Cell<Dictionary<Guid, Entry>> entryByIdCell)
    {
        // The operation monitors the token of the strategy, when there is one, such as an
        // external timeout, and also the cancellation of this item. The two are linked.
        CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                token1: toStart.StrategyToken,
                token2: toStart.Item.Cancellation.Token);

        try
        {
            // This code calls the operation inline, on this thread, and awaits it. The
            // operation moves to a different thread only when it must, such as at its own first
            // await. This code never sends it to the thread pool only to start it. When the
            // operation ends before this await, the await continues synchronously and there is
            // no continuation on a different thread. `value` is the initial TInput at the
            // admission of this item. PromoteAndLaunch gives it to this method, as the class
            // remarks give, and no code calculates it again from TStrategyInput.
            ResultConstructor<TResult> resultConstructor =
                await this.operation(
                        input: value,
                        resultFactory: ResultFactory<TResult>.Instance,
                        token: linked.Token)
                    .ConfigureAwait(false);

            this.Complete(
                item: toStart.Item,
                pending: AsyncOutcome<ResultConstructor<TResult>>.Succeeded(resultConstructor),
                entryByIdCell: entryByIdCell,
                tokenToCheck: linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            this.Complete(
                item: toStart.Item,
                pending: AsyncOutcome<ResultConstructor<TResult>>.Canceled(),
                entryByIdCell: entryByIdCell,
                tokenToCheck: null);
        }
        catch (Exception ex)
        {
            this.Complete(
                item: toStart.Item,
                pending: AsyncOutcome<ResultConstructor<TResult>>.Failed(ex),
                entryByIdCell: entryByIdCell,
                tokenToCheck: linked.Token);
        }
        finally
        {
            linked.Dispose();
        }
    }

    /// <summary>
    ///     The one method that makes the effects of an item at its end. An item with no start,
    ///     which a cancellation removed with the Queued status, also comes here. This method asks
    ///     the strategy for its decision. Then, in one atomic SodaFlow transaction, it publishes
    ///     the outcome if the strategy asks for that, removes the entry of this item, promotes
    ///     each item that the strategy selects, and disposes the CancellationTokenSource of this
    ///     item. The removal of the entry and the disposal of its CancellationTokenSource are in
    ///     the same transaction, thus the Snapshot of a cancellation stream never reads a stale
    ///     entry. That Snapshot sees the entry from before this transaction, and a cancellation
    ///     can then remove it, or it does not see the entry at all, from after this transaction.
    ///     This method can call itself. For example, it empties some Queued items that a
    ///     cancellation removed, in one sequence, through the short path in PromoteAndLaunch. A
    ///     SodaFlow transaction in a transaction is safe, but a very long queue with a
    ///     cancellation on each item makes a depth of calls in relation to that length.
    /// </summary>
    private void Complete(
        AsyncQueuedItem<TStrategyInput> item,
        AsyncOutcome<ResultConstructor<TResult>> pending,
        Cell<Dictionary<Guid, Entry>> entryByIdCell,
        CancellationToken? tokenToCheck) =>
        TransactionInternal.RunImpl(() =>
        {
            // A cancellation that arrived while the operation ran makes the item Canceled, also
            // when the operation gave a result or threw.
            AsyncOutcome<ResultConstructor<TResult>> outcome =
                tokenToCheck is { IsCancellationRequested: true }
                    ? AsyncOutcome<ResultConstructor<TResult>>.Canceled()
                    : pending;

            // The strategy reads how the operation ended and not the result, thus this call comes
            // before the construction of the result. See AsyncCompletion for what that gives: the
            // pipeline makes a result only for an item that it publishes.
            AsyncStrategyResult<TStrategyInput> decision =
                this.stateManager.OnCompleted(
                    item: item,
                    completion: outcome.Match(
                        onSucceeded: static _ => AsyncCompletion.Succeeded(),
                        onFailed: AsyncCompletion.Failed,
                        onCanceled: AsyncCompletion.Canceled));

            if (decision.Publish)
            {
                outcome.MatchVoid(
                    onSucceeded: resultConstructor => this.Publish(
                        resultConstructor: resultConstructor,
                        tokenToCheck: tokenToCheck),
                    onFailed: this.errors.SendImpl,
                    onCanceled: null);
            }

            Guid[] promote = new Guid[decision.Next.Count];
            TInput[] values = new TInput[decision.Next.Count];

            Dictionary<Guid, Entry>? entryById = null;

            for (int i = 0; i < decision.Next.Count; i++)
            {
                entryById ??= entryByIdCell.SampleImpl();

                Guid id = decision.Next[i].Item.Id;

                if (entryById.TryGetValue(key: id, value: out Entry? entry))
                {
                    values[i] = entry.Value;
                }
                else
                {
                    throw new InvalidOperationException("Could not find item to start.");
                }

                promote[i] = id;
            }

            this.mutations.SendImpl(
                new Mutation(
                    remove: new[] { item.Id },
                    promote: promote,
                    add: Array.Empty<Entry>()));

            item.Cancellation.Dispose();

            for (int i = 0; i < decision.Next.Count; i++)
            {
                this.PromoteAndLaunch(
                    toStart: decision.Next[i],
                    value: values[i],
                    entryByIdCell: entryByIdCell);
            }

            return UnitInternal.Value;
        });

    /// <summary>
    ///     Makes the result of an item and sends it. This is the one code that calls the function
    ///     of a <see cref="ResultConstructor{TResult}" />, and it runs in the transaction that
    ///     publishes. Thus a result that holds a cell or a stream comes into existence in that
    ///     transaction, and that is the cause for a constructor and not a value.
    ///     <para>
    ///         A throw from that function goes to the errors. The strategy saw this item as
    ///         Succeeded, because the operation did return, and this code then publishes the error
    ///         of the construction and not a result. A cancellation that the function throws, with
    ///         the token of this item canceled, publishes nothing, as a cancellation always
    ///         does.
    ///     </para>
    ///     <para>
    ///         The send is not in the try. A listener that throws is not a failure of this
    ///         operation, and an error stream that receives such a throw hides its source.
    ///     </para>
    /// </summary>
    private void Publish(ResultConstructor<TResult> resultConstructor, CancellationToken? tokenToCheck)
    {
        TResult result;

        try
        {
            result = resultConstructor.GetResult();
        }
        catch (OperationCanceledException) when (tokenToCheck is { IsCancellationRequested: true })
        {
            return;
        }
        catch (Exception e)
        {
            this.errors.SendImpl(e);

            return;
        }

        this.results.SendImpl(result);
    }

    // ---- The records of the tracked items. They are private to the execution engine, and a
    // strategy cannot read them. ----

    private sealed class Entry
    {
        public Entry(TInput value, AsyncQueuedItem<TInput> item, AsyncItemStatus status)
        {
            this.Value = value;
            this.Item = item;
            this.Status = status;
        }

        public TInput Value { get; }

        public AsyncQueuedItem<TInput> Item { get; }

        public AsyncItemStatus Status { get; }

        public Entry WithStatus(AsyncItemStatus status) => new(value: this.Value, item: this.Item, status: status);
    }

    private sealed class Mutation
    {
        public Mutation(
            Guid[] remove,
            Guid[] promote,
            Entry[] add)
        {
            this.Remove = remove;
            this.Promote = promote;
            this.Add = add;
        }

        public Guid[] Remove { get; }

        public Guid[] Promote { get; }

        public Entry[] Add { get; }
    }
}

/*
This example uses the C# wrapper's public surface (SodaFlow.Async.AsyncStreamExtensions.MapAsync
and SodaFlow.Async.AsyncConcurrencyStrategy) — not this project's own internal MapAsyncImpl/
AsyncConcurrencyStrategyFactory directly, which no external consumer ever references. The F#
wrapper's equivalents (mapAsync and the functions in SodaFlow.Async, e.g. queueStrategy()) follow
the same shape, typed against F#'s native unit instead of SodaFlow.Functional.Unit.

Usage:

StreamSink<string> requests = Stream.CreateSink<string>();
StreamSink<SearchResults> results = Stream.CreateSink<SearchResults>();
StreamSink<Exception> errors = Stream.CreateSink<Exception>();
StreamSink<Unit> cancelAll = Stream.CreateSink<Unit>();                                 // e.g. a "Cancel" button
StreamSink<IReadOnlyCollection<string>> cancelMatching =
    Stream.CreateSink<IReadOnlyCollection<string>>();                           // e.g. per-row cancel buttons

// The strategy instance has no state, and more than one call can use it. Keep this instance and
// give it to more than one MapAsync call, at the same time. Each call gets its own scheduling
// state.
//
// This code sets cancelOnDispose here, at the setup, and the default is true. A call of Dispose()
// does not select it.
using AsyncMapStatus<string> status = requests.MapAsync(
    results: results,
    errors: errors,
    operation: async (query, ct) => await searchService.SearchAsync(query, ct),
    strategy: AsyncConcurrencyStrategy.Queue(),
    cancelAll: cancelAll,
    cancelMatching: cancelMatching,
    cancelOnDispose: true);

Cell<bool> isSearching = status.IsRunning;                  // true only while something is actually executing
Cell<IReadOnlyList<AsyncItem<string>>> queueView = status.Items; // both Queued and Running entries, for e.g. a queue-position display

// cancelAll.Send(Unit.Value) cancels each item, Queued or Running.
// cancelMatching.Send(new[] { "some query" }) cancels only that item, at each status.

// This stops the full pipeline, for example when a user closes the view that holds it. It is the
// only path to a disposal. There is no overload with a bool, because cancelOnDispose above makes
// that selection. A disposal always stops the admission of more requests. The cancelOnDispose
// value at the setup sets the cancellation of the tracked items.
status.Dispose();

Custom concurrency logic:
Subclass AsyncConcurrencyStrategy<TInput, TState> and implement CreateState/Admit/OnCompleted
as pure reporting — they have no access to the result/error sinks and don't start Tasks
themselves; they just describe what should happen and let the execution engine carry it out.
CreateState is called once per MapAsync call, so TState is where all of a strategy's mutable
bookkeeping lives instead of on the strategy instance itself — that's what makes a single
strategy instance safe to reuse across multiple MapAsync calls without their scheduling state
bleeding into each other. The one thing you can do imperatively is item.Cancel(), to cancel an
item you're managing (queued or running); it still completes normally through OnCompleted as
AsyncCompletion.Canceled, so you can chain from there. Every value you don't immediately start stays
tracked as Queued automatically; hold onto its AsyncQueuedItem (not just its Value) in TState if
you intend to promote it later or recognize it again in OnCompleted — AsyncQueuedItem is a
sealed class you can't construct, and you're always handed back the very instance you were
given, so ReferenceEquals — or comparing Id, as the built-in SwitchLatest strategy does — is
enough for "is this still current?" checks, with no separate handle needed. A strategy that
neither starts nor remembers an incoming value leaves it
permanently Queued — if you want a "reject outright" behavior, promote it immediately and have
your own logic complete it right away instead.

Sharing one strategy across several TInput types:
AsyncConcurrencyStrategy<TInput, TState> doesn't have to be written against the exact TInput of
any one MapAsync call. MapAsync itself is MapAsync<TInput, TResult, TStrategyInput>, with
`where TInput : TStrategyInput` — so a strategy written against a common base or interface
(e.g. AsyncConcurrencyStrategy<IRequest, TState>) can be handed to MapAsync calls over several
different, more specific Stream<TInput> streams, one shared instance governing all of them
together (e.g. one Queue strategy serializing every kind of IRequest across multiple streams).
TStrategyInput is always inferred from the strategy argument's type — for the common case where
the strategy is written against the call's exact type, it's simply equal to TInput and nothing
needs to be specified explicitly. TState never appears in a MapAsync signature at all — it's
fully opaque to callers.

A strategy never sees a result. OnCompleted takes an AsyncCompletion, which says only whether
the operation returned, threw, or was canceled, and the pipeline makes the result afterwards and
only for an item it publishes — see ResultFactory.Construct. That is why there
is no TStrategyResult and no resultConverter: they existed to give the strategy a value, and the
strategy no longer reads one.
*/
