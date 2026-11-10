using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace SodaFlow.Async;

/// <summary>
///     The work that a MapAsync pipeline does for one value from the source
///     stream. It gives a <see cref="MapAsyncResult{TResult}" /> and not a result,
///     because the two answers are not the same:
///     <see cref="ResultFactory{TResult}.FromValue" /> carries a value that the
///     operation has, and <see cref="ResultFactory{TResult}.Construct" /> carries
///     a function that the pipeline calls in the transaction that sends the
///     result. Use the second one when the result contains a cell, a stream, or
///     another part of a SodaFlow graph, because such a part must come into
///     existence in that transaction.
/// </summary>
/// <typeparam name="TInput">The type in the source stream.</typeparam>
/// <typeparam name="TResult">The type that the pipeline publishes.</typeparam>
/// <param name="input">
///     The value from the source stream, as the pipeline admitted
///     it.
/// </param>
/// <param name="resultFactory">Makes the two kinds of answer. It has no state.</param>
/// <param name="token">
///     Cancels this operation. It combines the cancellation of this item with the
///     cancellation of the strategy. An operation that does not monitor it runs to
///     its end, and the pipeline then does not publish its result.
/// </param>
/// <returns>A Task with the value, or with the function that makes the value.</returns>
[PublicAPI]
public delegate Task<MapAsyncResult<TResult>> MapAsyncOperation<in TInput, TResult>(
    TInput input,
    ResultFactory<TResult> resultFactory,
    CancellationToken token);

/// <summary>The two states of a tracked item: a wait for a slot, or execution.</summary>
[PublicAPI]
public enum AsyncItemStatus
{
    /// <summary>
    ///     The pipeline admits and tracks the item, and no strategy promoted it to
    ///     Running.
    /// </summary>
    Queued,

    /// <summary>
    ///     A strategy promoted the item. The pipeline calls its operation, or called
    ///     it.
    /// </summary>
    Running
}

/// <summary>
///     An input value that a MapAsync pipeline tracks, and its current
///     status.
/// </summary>
[PublicAPI]
public readonly struct AsyncItem<TInput>
{
    /// <summary>
    ///     Holds an input value with its status. The execution engine builds one for
    ///     each tracked item.
    /// </summary>
    /// <param name="value">The initial input value from the source stream.</param>
    /// <param name="status">The status of that value: Queued or Running.</param>
    public AsyncItem(TInput value, AsyncItemStatus status)
    {
        this.Value = value;
        this.Status = status;
    }

    /// <summary>The initial input value from the source stream.</summary>
    public TInput Value { get; }

    /// <summary>
    ///     The status of this value: a wait for a strategy to promote it, or Running.
    /// </summary>
    public AsyncItemStatus Status { get; }
}

/// <summary>
///     The answer of a <see cref="MapAsyncOperation{TInput,TResult}" />: a result,
///     or a function that makes one. The pipeline calls such a function in the
///     transaction that sends the result. Make one with
///     <see cref="ResultFactory{TResult}" />, which the operation receives.
/// </summary>
/// <typeparam name="TResult">The type that the pipeline publishes.</typeparam>
[PublicAPI]
public sealed class MapAsyncResult<TResult>
{
    private readonly Func<TResult>? constructResult;
    private readonly TResult? result;

    internal MapAsyncResult(TResult result) => this.result = result;

    internal MapAsyncResult(Func<TResult> constructResult) => this.constructResult = constructResult;

    // ReSharper disable once NullableWarningSuppressionIsUsed - result is not null if constructResult is null
    internal TResult GetResult() => this.constructResult != null ? this.constructResult() : this.result!;
}

/// <summary>
///     Makes the answer of a <see cref="MapAsyncOperation{TInput,TResult}" />. The
///     pipeline gives one to each call of the operation. It holds no state, and a
///     caller cannot make one.
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
    ///     Carries a result that the operation has. Use this one when the operation
    ///     makes no part of a SodaFlow graph.
    /// </summary>
    /// <param name="value">The value to publish.</param>
    /// <returns>The answer to return from the operation.</returns>
    public MapAsyncResult<TResult> FromValue(TResult value) => new(value);

    /// <summary>
    ///     Carries a function that makes the result. The pipeline calls it one time,
    ///     in the transaction that sends the result, which is what a result with a
    ///     cell or a stream in it must have. Keep the function short, because it holds
    ///     that transaction while it runs.
    ///     The pipeline calls it only for an item that it publishes: the strategy
    ///     decides that first, and this function runs after that decision. Thus, an
    ///     item that a cancellation stopped, or that a strategy refused, makes no
    ///     result at all.
    /// </summary>
    /// <param name="makeResult">Makes the value to publish.</param>
    /// <returns>The answer to return from the operation.</returns>
    public MapAsyncResult<TResult> Construct(Func<TResult> makeResult) => new(makeResult);
}

/// <summary>
///     The status of a MapAsync pipeline. It gives the operation of the pipeline,
///     and each input value that the pipeline tracks now with the status of that
///     value. It is also the only handle to stop the pipeline. See
///     <see cref="AsyncMapStatus.Dispose" />. This type adds <see cref="Items" />
///     to <see cref="AsyncMapStatus" />, which is what makes it generic: a caller
///     that reads only <see cref="AsyncMapStatus.IsRunning" /> or stops the
///     pipeline can hold the base type.
///     <see cref="AsyncMapStatus{TInput,TResult}" /> extends this one with
///     Execute, thus the count of the type parameters a caller keeps selects what
///     that caller can do.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public class AsyncMapStatus<TInput> : AsyncMapStatus
{
    // private protected, and not internal: AsyncMapStatus<TInput, TResult> is the one subclass,
    // and it is in this assembly. This prevents a subclass by external code, as the base does.
    private protected AsyncMapStatus(
        Cell<bool> isRunning,
        Cell<IReadOnlyList<AsyncItem<TInput>>> items,
        Action dispose)
        : base(isRunning: isRunning, dispose: dispose) =>
        this.Items = items;

    /// <summary>
    ///     Each value that the pipeline tracks now, Queued or Running. The sequence is
    ///     not specified, but each update is one snapshot that agrees with itself.
    /// </summary>
    /// <remarks>
    ///     An item enters this list in the transaction of the value that the pipeline
    ///     admits. An item leaves the list in a subsequent transaction, when the item
    ///     ends. A cancellation does not end an item in the transaction of the
    ///     cancellation stream. A Queued item leaves immediately after that
    ///     transaction. A Running item leaves when its operation ends.
    /// </remarks>
    public Cell<IReadOnlyList<AsyncItem<TInput>>> Items { get; }
}

/// <summary>
///     The status of a MapAsync pipeline, with the Execute methods. MapAsync
///     answers with this type. A caller that wants only
///     <see cref="AsyncMapStatus.IsRunning" /> and the disposal holds
///     <see cref="AsyncMapStatus" />. One that also wants
///     <see cref="AsyncMapStatus{TInput}.Items" /> holds
///     <see cref="AsyncMapStatus{TInput}" />, and one that also wants an Execute
///     method holds this type. Thus, the count of the type parameters a caller
///     keeps says what that caller does with the pipeline.
///     <para>
///         Execute has one purpose. The operation of a MapAsync pipeline calls a
///         second MapAsync pipeline with it, and waits for the result of that one
///         value. See <see cref="Execute(TInput)" />.
///     </para>
/// </summary>
/// <typeparam name="TInput">The type of the input values.</typeparam>
/// <typeparam name="TResult">The type of the results.</typeparam>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public sealed class AsyncMapStatus<TInput, TResult> : AsyncMapStatus<TInput>
{
    private readonly Func<TInput, Task<TResult>> execute;
    private readonly Func<Cell<TInput>, Task<TResult>> executeCell;
    private readonly ExecuteIfSomeAction<TInput, TResult> executeIfSome;

    internal AsyncMapStatus(
        Cell<bool> isRunning,
        Cell<IReadOnlyList<AsyncItem<TInput>>> items,
        Action dispose,
        Func<TInput, Task<TResult>> execute,
        Func<Cell<TInput>, Task<TResult>> executeCell,
        ExecuteIfSomeAction<TInput, TResult> executeIfSome)
        : base(isRunning: isRunning, items: items, dispose: dispose)
    {
        this.execute = execute;
        this.executeCell = executeCell;
        this.executeIfSome = executeIfSome;
    }

    /// <summary>
    ///     Puts one value into this pipeline and answers with the Task of that value
    ///     alone.
    ///     <para>
    ///         This method has one purpose. The operation of a MapAsync pipeline calls
    ///         a second MapAsync pipeline with it, and waits for the result of that
    ///         one value. Thus, an operation can be a pipeline of its own, and the
    ///         strategy of the inner pipeline controls the inner work. An operation is
    ///         an async method, thus it can await the Task.
    ///     </para>
    ///     <para>
    ///         Other code does not use this method. Code that has a value for a
    ///         pipeline sends that value on the source stream of the pipeline, and
    ///         reads the results stream. That is the interface of a pipeline. It keeps
    ///         the identity of one value out of code that does not use that identity.
    ///     </para>
    ///     <para>
    ///         The value goes through the strategy as a value from the source stream
    ///         does, thus the call obeys the concurrency rules of the pipeline. The
    ///         result reaches the results stream also, and this method gives the same
    ///         object.
    ///     </para>
    ///     <para>
    ///         The Task ends one time, in each condition. It gives the result where
    ///         the operation gives one and the strategy publishes it. That result is
    ///         the same object that the results stream gets. It carries the exception
    ///         where the operation throws and the strategy publishes that, and the
    ///         errors stream gets the same exception.
    ///     </para>
    ///     <para>
    ///         These conditions cancel the Task. A cancellation that stops the value
    ///         cancels it. A strategy that refuses the value cancels it. A strategy
    ///         that does not publish the outcome cancels it also. Such a strategy says
    ///         that no code wants the result, which is a cancellation at a different
    ///         moment. A disposal of the pipeline before the admission of the value
    ///         cancels it.
    ///     </para>
    ///     <para>
    ///         One condition gives no end to the Task, and it is not in the position
    ///         that this method is for. A strategy that keeps a value in the queue
    ///         permanently, and does not cancel that value, gives no end for the
    ///         pipeline to read. Each strategy in this library ends each value. The
    ///         documented method for a strategy to refuse a value cancels that value,
    ///         thus that method ends the Task.
    ///     </para>
    ///     <para>
    ///         A transaction that fails cancels the Task. Where a transaction is open,
    ///         this method defers the value into the post queue of that transaction. A
    ///         transaction that fails discards that queue, thus the value never enters
    ///         the pipeline. This method asks the transaction to cancel the Task in
    ///         that condition. A throw from the body of the transaction and a throw
    ///         while it propagates give the same result. A posted action that calls
    ///         this method, and whose own transaction fails, gives that result also.
    ///     </para>
    ///     <para>
    ///         A call with a transaction open is legal. This method defers the value
    ///         to a transaction of its own in that condition. The pipeline then admits
    ///         the value after the transaction of the caller ends. An operation runs
    ///         with no transaction open, also the code before its first await. Thus,
    ///         a call from an operation sends the value immediately.
    ///     </para>
    /// </summary>
    /// <param name="value">The value to put into the pipeline.</param>
    /// <returns>The Task of this value alone.</returns>
    public Task<TResult> Execute(TInput value) => this.execute(value);

    /// <summary>
    ///     Puts the value of a cell into this pipeline and answers with the Task of
    ///     that value alone. This method reads the cell in the transaction that puts
    ///     the value in. Thus, the pipeline admits the value that the cell has at that
    ///     instant. A caller that samples a cell and then calls
    ///     <see cref="Execute(TInput)" /> has two transactions, and the value of the
    ///     cell can change between them. A deferral carries the read with it. The read
    ///     and the send are thus together in each condition.
    ///     <para>
    ///         This method has the same one purpose as <see cref="Execute(TInput)" />,
    ///         and the Task obeys the same rules. Read the remarks of that method.
    ///     </para>
    /// </summary>
    /// <param name="value">The cell to read.</param>
    /// <returns>The Task of the value that the cell gives.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is null.</exception>
    public Task<TResult> Execute(Cell<TInput> value) => this.executeCell(value);

    // The Execute path for an input that can be missing. This assembly cannot refer to the Maybe
    // type of each language, thus each wrapper adds the public overload and calls this method.
    // `read` runs in the transaction of the send, as the read of the cell does for
    // Execute(Cell<TInput>). Where it gives no value, the pipeline admits nothing and calls
    // `onNone` in that transaction.
    internal void ExecuteIfSome(
        Func<MaybeInternal<TInput>> read,
        ExecuteCompletion<TResult> completion,
        Action onNone) =>
        this.executeIfSome(read: read, completion: completion, onNone: onNone);
}

/// <summary>
///     The status of a MapAsync pipeline, without the part that the input type
///     decides. It gives the operation of the pipeline, and it is the only handle
///     to stop the pipeline. See <see cref="Dispose" />. A caller that does not
///     read <see cref="AsyncMapStatus{TInput}.Items" /> can hold this type and
///     does not have to name the input type. <see cref="AsyncMapStatus{TInput}" />
///     is the type that MapAsync returns.
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

    /// <summary>
    ///     True while one item or more has Status == Running. An item with the Queued
    ///     status does not count.
    /// </summary>
    public Cell<bool> IsRunning { get; }

    /// <summary>
    ///     Stops this pipeline. The pipeline admits no more values from the source
    ///     stream, and it does not queue them or start them. The cancelOnDispose
    ///     parameter of MapAsync, which is true by default, sets at the setup if a
    ///     disposal also cancels the tracked items. This method does not make that
    ///     selection, because this IDisposable.Dispose() has no parameter and is the
    ///     only path to a disposal. When cancelOnDispose was true, a disposal cancels
    ///     each tracked item, Queued or Running, as one send on a cancelAll stream
    ///     does.
    ///     The same limits apply. A Running operation stops only if it monitors its
    ///     CancellationToken, and the pipeline removes a Queued item at the time of
    ///     its promotion, which is not always at the return of this call. This method
    ///     also does not stop the output. Each operation that runs continues to its
    ///     end and then publishes its result or its error. A second call is safe,
    ///     because each call after the first does nothing.
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

// The answer of one Execute call. The pipeline holds this type and not a TaskCompletionSource,
// because the Task of a call can have a type that is not TResult. An overload for an optional
// input gives an optional result, and the change occurs here, in the transaction that publishes.
// A continuation on a Task<TResult> can do the same change, but it adds an asynchronous step
// between the result and the caller.
internal abstract class ExecuteCompletion<TResult>
{
    // Makes the output now, and gives the action that answers the Task with it. Flush calls this in
    // the transaction that publishes, and runs the action only when that transaction commits.
    public abstract Action PrepareResult(TResult result);

    public abstract void TrySetException(Exception error);

    public abstract void TrySetCanceled();
}

internal sealed class ExecuteCompletion<TResult, TOutput>(in Func<TResult, TOutput> toOutput)
    : ExecuteCompletion<TResult>
{
    // RunContinuationsAsynchronously, and it is necessary and not a preference. Flush answers this
    // source in a last action of the transaction that publishes. Without it, the continuation of
    // the caller that awaits the Task runs there, on that thread. A send from that continuation
    // throws.
    private readonly TaskCompletionSource<TOutput> source =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Func<TResult, TOutput> toOutput = toOutput;

    public Task<TOutput> Task => this.source.Task;

    public override Action PrepareResult(TResult result)
    {
        TOutput output = this.toOutput(result);

        return () => this.source.TrySetResult(output);
    }

    public override void TrySetException(Exception error) => this.source.TrySetException(error);

    public override void TrySetCanceled() => this.source.TrySetCanceled();

    // Gives an answer that no result made. The overload for an optional input calls this when it
    // reads no input.
    public void TrySetOutput(TOutput output) => this.source.TrySetResult(output);
}

// The Execute path for an input that can be missing. See ExecuteIfSome on
// AsyncMapStatus<TInput, TResult>. This is a named delegate and not an Action, because a call can
// then give each argument with its name.
internal delegate void ExecuteIfSomeAction<TInput, TResult>(
    Func<MaybeInternal<TInput>> read,
    ExecuteCompletion<TResult> completion,
    Action onNone);

// Makes the report of a MapAsync pipeline. Each language package gives one that makes its own
// sealed type. This is a named delegate and not a Func, for the same cause as ExecuteIfSomeAction.
internal delegate MapAsyncDeliveryExceptionBase MapAsyncDeliveryExceptionFactory(
    string message,
    Exception innerException,
    MaybeInternal<Exception> undeliveredError);

/// <summary>
///     The shared base of the two parts of a MapAsync pipeline: the strategy (
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" />) and the engine that
///     runs it (
///     <see cref="AsyncMapExecutionManager{TInput,TResult,TStrategyInput}" />).
///     Its only purpose is to hold the small data types that the two parts send to
///     each other:
///     <see cref="AsyncQueuedItem{TInput}" />, <see cref="AsyncToStart{TInput}" />
///     , <see cref="AsyncOutcome{TResult}" />, and
///     <see cref="AsyncStrategyResult{TInput}" />. They are nested types here, and
///     not public top-level types. One class is not a subtype of the other.
///     Without this shared base, one part or the two parts must make these types
///     fully public to name them. With this base, the two parts get them through
///     usual inheritance, and the types stay off the public surface of the
///     library. Only code in this assembly, and code that subclasses
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" /> to write a custom
///     strategy, can see them. This base is not generic, because TInput and
///     TResult belong to the nested types that use them, and not to each user of
///     this base.
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
    ///     A value that a MapAsync pipeline tracks, from its admission until its
    ///     promotion, its end, or its cancellation. It is also the one object that
    ///     identifies the value in <see cref="AsyncToStart{TInput}" /> and in
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" />. It is
    ///     opaque. A strategy can keep one, usually in the state of its call, to
    ///     promote it after this or to identify it again at its end, and can read its
    ///     Value. A strategy cannot make one, because the constructor is internal and
    ///     the class is sealed. Thus, each instance comes from an
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.Admit" /> call. A
    ///     strategy always receives the same instance that it got before. Thus,
    ///     ReferenceEquals, or ==, is sufficient to identify an admitted value in its
    ///     state. An equal <see cref="Id" /> gives the same answer, and it is also
    ///     correct across the different instances that the execution engine keeps for
    ///     each admitted value. For that cause the SwitchLatest strategy in the
    ///     library compares the Id.
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
        ///     The identity of this item. The pipeline assigns it at the admission, and it
        ///     is the same across the different <see cref="AsyncQueuedItem{TInput}" />
        ///     instances that the execution engine keeps for one admitted value. One
        ///     instance has the type for the strategy, and the other has the type for the
        ///     public <see cref="AsyncItem{TInput}" /> view. An equal ID means the same
        ///     tracked value.
        /// </summary>
        public Guid Id { get; }

        /// <summary>The value of this item at its admission.</summary>
        public TInput Value { get; }

        /// <summary>
        ///     The source of the cancellation of this item. <see cref="Cancel" /> cancels
        ///     this source, and the execution engine links it into the token of the
        ///     operation. It is internal, because a strategy cancels through
        ///     <see cref="Cancel" /> and does not use this field.
        /// </summary>
        internal CancellationTokenSource Cancellation { get; }

        /// <summary>
        ///     Cancels this tracked item. It is the mechanism of a cancelAll stream and of
        ///     a cancelMatching stream, and a strategy can use it for its own schedule.
        ///     For example, SwitchLatest replaces its previous run with it. It operates on
        ///     an item with the Queued status and on an item with the Running status. The
        ///     pipeline does not start a Queued item at its turn, and a Running operation
        ///     stops only if it monitors the CancellationToken that it got. In each
        ///     condition the item ends as Canceled, thus
        ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" /> runs for
        ///     it and can start the next operation.
        ///     A call on an item that ended, on an item that a previous call canceled, or
        ///     on an item at its end is safe. Those calls do nothing and are not errors,
        ///     thus a strategy with a stale reference does not monitor the time when an
        ///     item stops being cancellable.
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

        /// <summary>
        ///     The admitted item to start or to promote. It comes from a previous Admit
        ///     call.
        /// </summary>
        public AsyncQueuedItem<TInput> Item { get; }

        /// <summary>
        ///     An optional second cancellation source to link into this run, with the
        ///     source of the item. It is not necessary to cancel an item that the strategy
        ///     controls, because <see cref="AsyncQueuedItem{TInput}.Cancel" /> does that.
        ///     Use this to attach a run to an external source: a timeout for one
        ///     operation, an ambient operation token, or a shared token for a batch of
        ///     work. At each other time, keep the default.
        /// </summary>
        public CancellationToken StrategyToken { get; }
    }

    /// <summary>
    ///     One item that a MapAsync pipeline tracks now, with its status. A strategy
    ///     reads a list of these for the queue of the pipeline. See
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.Admit" /> and
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" />.
    /// </summary>
    /// <typeparam name="TInput">The input type of the strategy.</typeparam>
    [PublicAPI]
    protected internal sealed class AsyncTrackedItem<TInput>
    {
        internal AsyncTrackedItem(AsyncQueuedItem<TInput> item, AsyncItemStatus status)
        {
            this.Item = item;
            this.Status = status;
        }

        /// <summary>
        ///     The item, as the strategy received it at its admission. It is the same
        ///     instance, thus ReferenceEquals recognizes it, Id recognizes it, and Cancel
        ///     on it cancels this item.
        /// </summary>
        public AsyncQueuedItem<TInput> Item { get; }

        /// <summary>The status of this item now: Queued, or Running.</summary>
        public AsyncItemStatus Status { get; }

        internal AsyncTrackedItem<TInput> WithStatus(AsyncItemStatus status) => new(item: this.Item, status: status);
    }

    /// <summary>
    ///     How an item ended, which is what a strategy reads. It carries no result
    ///     value: the pipeline makes the result of a MapAsync operation after the
    ///     strategy decides, and only for an item that it publishes. See
    ///     <see cref="ResultFactory{TResult}.Construct" />. A value here thus obliges
    ///     the pipeline to make each result, also the results that no code reads.
    ///     <para>
    ///         Succeeded says that the operation returned. The pipeline publishes an
    ///         error for such an item when the function that makes the result throws.
    ///     </para>
    ///     <para>
    ///         A cancellation is not one of these ends. It goes to
    ///         <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCanceled" />,
    ///         which reads the items alone, because a canceled end carries nothing to
    ///         read.
    ///     </para>
    /// </summary>
    [PublicAPI]
    protected internal sealed class AsyncCompletion
    {
        /// <summary>
        ///     The exception from the operation when <see cref="kind" /> is Failed, and
        ///     null at each other time.
        /// </summary>
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
        /// <param name="onFailed">
        ///     Handles a run with an error, with the exception from the
        ///     operation.
        /// </param>
        /// <returns>The value that the handler returned.</returns>
        public T Match<T>(
            [InstantHandle] Func<T> onSucceeded,
            [InstantHandle] Func<Exception, T> onFailed) =>
            this.kind switch
            {
                AsyncCompletionKind.Succeeded => onSucceeded(),
                // ReSharper disable once NullableWarningSuppressionIsUsed - This object can only be constructed with
                // kind being AsyncCompletionKind.Failed when it sets this.error to a non-null value.
                AsyncCompletionKind.Failed => onFailed(this.error!),
                _ => throw new InvalidOperationException("Unknown value for kind.")
            };

        /// <summary>
        ///     Runs the handler for the end of this item. Each handler is optional. Give
        ///     null for an end with no handler, and this method ignores that end.
        /// </summary>
        /// <param name="onSucceeded">Handles an operation that returned. It can be null.</param>
        /// <param name="onFailed">
        ///     Handles a run with an error, with the exception from the operation. It can
        ///     be null.
        /// </param>
        public void MatchVoid(
            [InstantHandle] Action? onSucceeded,
            [InstantHandle] Action<Exception>? onFailed)
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
                default:
                    throw new InvalidOperationException("Unknown value for kind.");
            }
        }

        /// <summary>Builds a Succeeded end, for an operation that returned.</summary>
        public static AsyncCompletion Succeeded() => new(kind: AsyncCompletionKind.Succeeded, error: null);

        /// <summary>Builds a Failed end with the exception from the operation.</summary>
        public static AsyncCompletion Failed(Exception error) => new(kind: AsyncCompletionKind.Failed, error: error);

        /// <summary>The two ends that a strategy can publish.</summary>
        private enum AsyncCompletionKind
        {
            /// <summary>The operation returned.</summary>
            Succeeded,

            /// <summary>
            ///     The operation threw an exception. See
            ///     <see cref="AsyncCompletion.error" />.
            /// </summary>
            Failed
        }
    }

    /// <summary>
    ///     The result at the end of an item, which the execution engine holds while it
    ///     decides what to publish. A strategy reads <see cref="AsyncCompletion" />
    ///     and never this type.
    /// </summary>
    internal sealed class AsyncOutcome<TResult>
    {
        /// <summary>
        ///     The exception from the operation when <see cref="kind" /> is Failed, and
        ///     null at each other time.
        /// </summary>
        private readonly Exception? error;
        // There is no Value property, no Error property, and no Kind property. Match is the only
        // path to the data, thus a strategy cannot read a result and also ignore the condition
        // with no result. There is also no asynchronous Match. OnCompleted is synchronous and
        // answers with data, thus an awaitable Match causes work here that belongs to the
        // execution engine.

        /// <summary>Which of the three ends this item had.</summary>
        private readonly AsyncOutcomeKind kind;

        /// <summary>
        ///     The return value of the operation when <see cref="kind" /> is Succeeded,
        ///     and the default value at each other time.
        /// </summary>
        private readonly TResult? value;

        private AsyncOutcome(AsyncOutcomeKind kind, TResult? value, Exception? error)
        {
            this.kind = kind;
            this.value = value;
            this.error = error;
        }

        /// <summary>Runs the handler for the end of this item, and returns its value.</summary>
        /// <typeparam name="T">The type that each handler returns.</typeparam>
        /// <param name="onSucceeded">
        ///     Handles a run that succeeded, with the value that the
        ///     operation returned.
        /// </param>
        /// <param name="onFailed">
        ///     Handles a run with an error, with the exception from the
        ///     operation.
        /// </param>
        /// <param name="onCanceled">Handles a canceled run, which can have no start.</param>
        /// <returns>The value that the handler returned.</returns>
        public T Match<T>(
            [InstantHandle] Func<TResult, T> onSucceeded,
            [InstantHandle] Func<Exception, T> onFailed,
            [InstantHandle] Func<T> onCanceled) =>
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
            /// <summary>
            ///     The operation returned a value. See
            ///     <see cref="AsyncOutcome{TResult}.value" />.
            /// </summary>
            Succeeded,

            /// <summary>
            ///     The operation threw an exception. See
            ///     <see cref="AsyncOutcome{TResult}.error" />.
            /// </summary>
            Failed,

            /// <summary>
            ///     A cancellation stopped the operation, or the operation had no start because
            ///     a cancellation removed it with the Queued status. The pipeline never
            ///     publishes this outcome, at each return value of OnCompleted. See
            ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" />.
            /// </summary>
            Canceled
        }
    }

    /// <summary>
    ///     The answer of a strategy for one end that can send: if the pipeline
    ///     publishes that outcome, and which items to start next. A cancellation has
    ///     no such answer, because it publishes nothing. See
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCanceled" />, which
    ///     gives the items to start and nothing else.
    /// </summary>
    [PublicAPI]
    protected internal sealed class AsyncStrategyResult<TInput>
    {
        /// <summary>An empty Next list. This decision starts no more items.</summary>
        public static readonly IReadOnlyList<AsyncToStart<TInput>> None = [];

        /// <summary>Builds the answer of a strategy from its two decisions.</summary>
        /// <param name="publish">
        ///     True when the pipeline sends the outcome that ended now to the results or
        ///     to the errors. False is the same as a cancellation for a caller: the
        ///     pipeline sends nothing for that item.
        /// </param>
        /// <param name="next">
        ///     Tracked items to start now, or to promote now. Give <see cref="None" /> for
        ///     no items.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="next" /> is null.</exception>
        public AsyncStrategyResult(bool publish, IReadOnlyList<AsyncToStart<TInput>> next)
        {
            // A custom strategy makes this object. A null here fails in the engine, and the
            // message there names nothing that the author of that strategy can act on.
            this.Publish = publish;
            this.Next = next ?? throw new ArgumentNullException(nameof(next));
        }

        /// <summary>True when the pipeline sends the outcome that ended now.</summary>
        public bool Publish { get; }

        /// <summary>
        ///     Tracked items to start now, or to promote now, because of this
        ///     decision.
        /// </summary>
        public IReadOnlyList<AsyncToStart<TInput>> Next { get; }
    }

    /// <summary>
    ///     Removes the type of the state of a strategy. Thus,
    ///     <see cref="AsyncMapExecutionManager{TInput,TResult,TStrategyInput}" /> can
    ///     hold a strategy with its state, and that class is not generic over the
    ///     state. That keeps TState out of each MapAsync signature. See
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
        IReadOnlyList<AsyncToStart<TInput>> Admit(
            AsyncQueuedItem<TInput> incoming,
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked);

        /// <summary>
        ///     Sends the call to
        ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCompleted" /> with the
        ///     state in the closure.
        /// </summary>
        AsyncStrategyResult<TInput> OnCompleted(
            AsyncQueuedItem<TInput> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked);

        /// <summary>
        ///     Sends the call to
        ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.OnCanceled" /> with the
        ///     state in the closure.
        /// </summary>
        IReadOnlyList<AsyncToStart<TInput>> OnCanceled(
            IReadOnlyList<AsyncQueuedItem<TInput>> canceled,
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked);
    }

    /// <summary>
    ///     Holds a strategy instance with the one <typeparamref name="TState" /> for a
    ///     single MapAsync call. See
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.CreateStateManager" />.
    ///     Thus, the execution engine can call each method of a strategy, and does not
    ///     know <typeparamref name="TState" />.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    internal class StateManager<TInput, TState>(
        in AsyncConcurrencyStrategy<TInput, TState> strategy,
        in TState state)
        : IStateManager<TInput>
    {
        private readonly TState state = state;
        private readonly AsyncConcurrencyStrategy<TInput, TState> strategy = strategy;

        public IReadOnlyList<AsyncToStart<TInput>> Admit(
            AsyncQueuedItem<TInput> incoming,
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked) =>
            this.strategy.Admit(state: this.state, incoming: incoming, tracked: tracked);

        public AsyncStrategyResult<TInput> OnCompleted(
            AsyncQueuedItem<TInput> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked) =>
            this.strategy.OnCompleted(
                state: this.state,
                item: item,
                completion: completion,
                tracked: tracked);

        public IReadOnlyList<AsyncToStart<TInput>> OnCanceled(
            IReadOnlyList<AsyncQueuedItem<TInput>> canceled,
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked) =>
            this.strategy.OnCanceled(
                state: this.state,
                canceled: canceled,
                tracked: tracked);
    }
}

/// <summary>
///     The one internal entry point for the public MapAsync surface of each
///     language wrapper. It connects an impure asynchronous operation to the FRP
///     graph. It listens on a Stream&lt;TInput&gt;, runs an async operation for
///     each send, puts the result into a StreamSink&lt;TResult&gt;, and gives the
///     Queued items and the Running items. It can also connect to streams that
///     cancel Queued work and Running work. There is only this one method, in its
///     most general shape. Each wrapper holds the short overloads that remove the
///     types, or that connect TInput and TResult to the types of a strategy. Those
///     are AsyncStreamExtensions in SodaFlow.Async and the mapAsync family in
///     SodaFlow.FSharp.Async. Each language has a different short shape, and each
///     wrapper has a different type for a value that the strategy does not use.
/// </summary>
internal static class AsyncStreamUtility
{
    /// <summary>
    ///     Runs <paramref name="operation" /> for each send of
    ///     <paramref name="source" />. It sends a value that succeeded to
    ///     <paramref name="results" /> and a value with an error to
    ///     <paramref name="errors" />. It needs no relation between
    ///     <typeparamref name="TInput" /> and <typeparamref name="TStrategyInput" />,
    ///     because the caller gives <paramref name="inputConverter" /> explicitly.
    ///     Thus, the narrower overloads of each wrapper give their own short shape
    ///     only with the arguments here: an identity converter where the types agree,
    ///     and a constant converter where the strategy does not use the value.
    /// </summary>
    /// <typeparam name="TInput">
    ///     The type in the source stream. It is the input for each call of
    ///     <paramref name="operation" />, the type in
    ///     <see cref="AsyncMapStatus{TInput}.Items" />, and the type that
    ///     <paramref name="cancelMatching" /> compares.
    /// </typeparam>
    /// <typeparam name="TResult">
    ///     The type that <paramref name="operation" /> gives when it succeeds. The
    ///     pipeline sends it to <paramref name="results" />.
    /// </typeparam>
    /// <typeparam name="TStrategyInput">
    ///     The input type of <paramref name="strategy" />. The compiler infers it from
    ///     the type of <paramref name="strategy" />, and no caller gives it
    ///     explicitly. It needs no inheritance relation to
    ///     <typeparamref name="TInput" />, because <paramref name="inputConverter" />
    ///     makes it explicitly.
    /// </typeparam>
    /// <param name="source">
    ///     The stream of inputs. The pipeline gives each send to
    ///     <paramref name="strategy" />, and the strategy starts it immediately or
    ///     makes it wait. After a disposal of the status, the pipeline ignores each
    ///     subsequent send.
    /// </param>
    /// <param name="results">
    ///     This is necessary. The pipeline sends the return value of each operation
    ///     that succeeded here, in the sequence of their ends and not in the sequence
    ///     of the inputs.
    ///     The pipeline does not send a result from a run that a different run
    ///     replaced, or from a run that a cancellation stopped. See
    ///     <paramref name="strategy" />.
    /// </param>
    /// <param name="errors">
    ///     This is necessary. The pipeline sends each operation with an error here.
    ///     There is no call of this method with no destination for the errors.
    /// </param>
    /// <param name="handleException">
    ///     This is necessary. The pipeline gives it each exception that has no caller
    ///     and that the errors cannot receive. A listener can stop a send, and the
    ///     pipeline can then fail to end the item or to report that failure. A defect
    ///     in the pipeline also comes here. The pipeline continues after each call.
    ///     A throw from this action ends the process, as a throw from the handler of a
    ///     timer system does.
    /// </param>
    /// <param name="createDeliveryException">
    ///     This is necessary. It makes the exception that the pipeline sends when a
    ///     listener stops the send of an outcome. Its arguments are the message, the
    ///     exception of the listener, and the error that the pipeline did not send.
    ///     Each language package gives the function that makes its own sealed type.
    ///     See <see cref="MapAsyncDeliveryExceptionBase" />.
    /// </param>
    /// <param name="operation">
    ///     The asynchronous work for each input. The pipeline calls it inline, thus it
    ///     does not go to a thread pool before its own await. It receives a
    ///     CancellationToken that combines the cancellation of this item with each
    ///     token from the strategy. An operation that obeys that token lets
    ///     <paramref name="cancelAll" />, <paramref name="cancelMatching" />, and
    ///     <paramref name="cancelOnDispose" /> stop work that started. An operation
    ///     that ignores the token continues to its end, and the cancellation then only
    ///     stops the publication of its result. There is no overload with no token.
    /// </param>
    /// <param name="strategy">
    ///     The control of requests that overlap. A strategy instance holds no state of
    ///     its own, and more than one MapAsync call can use the same instance safely,
    ///     at the same time.
    ///     Each call gets its own new state manager, thus two pipelines never share a
    ///     scheduling state.
    /// </param>
    /// <param name="inputConverter">
    ///     Changes each <typeparamref name="TInput" /> value to the
    ///     <typeparamref name="TStrategyInput" /> of <paramref name="strategy" />,
    ///     before the admission.
    /// </param>
    /// <param name="cancelAll">
    ///     This is optional. Each send cancels each tracked operation, Queued or
    ///     Running. The pipeline does not start a canceled Queued item at its turn. A
    ///     cancellation stops a Running operation only if that operation monitors its
    ///     CancellationToken.
    /// </param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations, Queued or
    ///     Running, whose input value is in the collection of that send. This uses the
    ///     default equality comparer for TInput. The limits of
    ///     <paramref name="cancelAll" /> also apply here.
    /// </param>
    /// <param name="cancelOnDispose">
    ///     True when a disposal of the <see cref="AsyncMapStatus{TInput}" /> also
    ///     cancels each item that the pipeline tracks at that time, Queued or Running.
    ///     The default is true. At each value, a disposal always stops the admission
    ///     of more values. This code sets the value here, at the setup, and not as a
    ///     parameter of Dispose, because IDisposable.Dispose() is the only path to a
    ///     disposal.
    ///     A disposal does not stop the output. Each operation that runs continues to
    ///     its end and publishes to <paramref name="results" /> or to
    ///     <paramref name="errors" /> after the call returns. With true that output is
    ///     usually not important, because the pipeline never publishes a canceled
    ///     outcome and an operation that obeys its token makes none. An operation that
    ///     ignores its token makes one, and with false that output is the purpose.
    /// </param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" />. IsRunning is a Cell&lt;bool&gt;
    ///     that is true while one call or more has the Running status, and a Queued
    ///     item does not make it true.
    ///     It updates with no glitch, in the transaction of the event that changes it.
    ///     Items gives each tracked value with its status. A disposal of the status
    ///     stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />,
    ///     <paramref name="errors" />, <paramref name="handleException" />,
    ///     <paramref name="createDeliveryException" />, <paramref name="operation" />,
    ///     or <paramref name="strategy" /> is null.
    /// </exception>
    internal static AsyncMapStatus<TInput, TResult> MapAsyncImpl<TInput, TResult, TStrategyInput>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Action<Exception> handleException,
        MapAsyncDeliveryExceptionFactory createDeliveryException,
        MapAsyncOperation<TInput, TResult> operation,
        AsyncConcurrencyStrategyBase<TStrategyInput> strategy,
        Func<TInput, TStrategyInput> inputConverter,
        Stream<UnitInternal>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true)
    {
        ThrowHelper.ThrowIfNull(argument: source, paramName: nameof(source));
        ThrowHelper.ThrowIfNull(argument: results, paramName: nameof(results));
        ThrowHelper.ThrowIfNull(argument: errors, paramName: nameof(errors));
        ThrowHelper.ThrowIfNull(argument: handleException, paramName: nameof(handleException));
        ThrowHelper.ThrowIfNull(argument: createDeliveryException, paramName: nameof(createDeliveryException));
        ThrowHelper.ThrowIfNull(argument: operation, paramName: nameof(operation));
        ThrowHelper.ThrowIfNull(argument: strategy, paramName: nameof(strategy));

        return new AsyncMapExecutionManager<TInput, TResult, TStrategyInput>(
            strategy: strategy,
            inputConverter: inputConverter,
            results: results,
            errors: errors,
            handleException: handleException,
            createDeliveryException: createDeliveryException,
            operation: operation,
            cancelAll: cancelAll,
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose).Attach(source);
    }
}

/// <summary>
///     The face of a strategy that is not generic over <c>TState</c>. It is the
///     type that
///     <see
///         cref="AsyncStreamUtility.MapAsyncImpl{TInput,TResult,TStrategyInput}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Action{Exception},MapAsyncDeliveryExceptionFactory,MapAsyncOperation{TInput,TResult},AsyncConcurrencyStrategyBase{TStrategyInput},Func{TInput,TStrategyInput},Stream{UnitInternal},Stream{IReadOnlyCollection{TInput}},bool)" />
///     and its overloads accept. This keeps the <c>TState</c> of a strategy out of
///     each MapAsync signature. A caller and the execution engine see only
///     <see cref="AsyncConcurrencyStrategyBase{TInput}" />, and never
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" />. The internal
///     constructor prevents an external subclass. To write a custom strategy,
///     subclass <see cref="AsyncConcurrencyStrategy{TInput,TState}" />.
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
    ///     Makes the <see cref="AsyncMapBase.IStateManager{TInput}" /> for one
    ///     MapAsync call. See
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TState}.CreateState" />.
    /// </summary>
    internal abstract IStateManager<TInput> CreateStateManager();
}

/// <summary>
///     The base class of a MapAsync scheduling strategy, which is the admission
///     and the sequence of a stream of async requests. A strategy answers two
///     questions, and each answer is data.
///     <see cref="Admit" /> answers "which items start now for this new tracked
///     value?" and <see cref="OnCompleted" /> answers "which items start next for
///     this outcome, and does the pipeline publish it?". A strategy answers only
///     from a <typeparamref name="TState" /> that it controls. A strategy instance
///     holds no state of its own, because each value that changes is in
///     <typeparamref name="TState" />. <see cref="CreateState" /> makes one
///     instance of that state for each MapAsync call. Thus, more than one MapAsync
///     call can use the same strategy instance safely, at the same time. The
///     execution engine (see
///     <see
///         cref="AsyncStreamUtility.MapAsyncImpl{TInput,TResult,TStrategyInput}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Action{Exception},MapAsyncDeliveryExceptionFactory,MapAsyncOperation{TInput,TResult},AsyncConcurrencyStrategyBase{TStrategyInput},Func{TInput,TStrategyInput},Stream{UnitInternal},Stream{IReadOnlyCollection{TInput}},bool)" />
///     ) holds the <typeparamref name="TState" /> of each call, and it is the only
///     code that gives that state back to the strategy. <see cref="Admit" /> and
///     <see cref="OnCompleted" /> cannot use the result sink, the error sink, or a
///     Task, and cannot start a Task. The two methods give a description of the
///     necessary operations, and the execution engine does them. The one
///     imperative operation is
///     <see cref="AsyncMapBase.AsyncQueuedItem{TInput}.Cancel" />, which cancels
///     an item that the strategy controls. It publishes nothing and starts
///     nothing. It goes to the cancellation path of an external cancelAll stream,
///     thus the item ends through <see cref="OnCompleted" /> as each other item
///     does. The identity of an item at its end is the item that the strategy got
///     in <see cref="Admit" />. There is no second handle. Keep the item in
///     <typeparamref name="TState" /> to identify it again after this.
/// </summary>
[PublicAPI]
// ReSharper disable once InheritdocConsiderUsage
public abstract class AsyncConcurrencyStrategy<TInput, TState>
    : AsyncConcurrencyStrategyBase<TInput>
{
    internal override IStateManager<TInput> CreateStateManager() =>
        new StateManager<TInput, TState>(strategy: this, state: this.CreateState());

    /// <summary>
    ///     Makes a new scheduling state for one MapAsync call. The engine calls this
    ///     one time for each call. See
    ///     <see
    ///         cref="AsyncStreamUtility.MapAsyncImpl{TInput,TResult,TStrategyInput}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Action{Exception},MapAsyncDeliveryExceptionFactory,MapAsyncOperation{TInput,TResult},AsyncConcurrencyStrategyBase{TStrategyInput},Func{TInput,TStrategyInput},Stream{UnitInternal},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     Thus, two pipelines with the same strategy instance never read the state of
    ///     the other, also when the two run at the same time.
    /// </summary>
    protected abstract TState CreateState();

    /// <summary>
    ///     Gives the items to start now for a new admitted value. The pipeline tracks
    ///     that value with the Queued status. See
    ///     <see cref="AsyncMapBase.AsyncQueuedItem{TInput}" />. The engine always
    ///     calls this in a SodaFlow transaction. See
    ///     <see cref="AsyncMapExecutionManager{TInput,TResult,TStrategyInput}" /> for
    ///     the cause that makes a change to <paramref name="state" /> here safe with
    ///     no explicit lock. Return an
    ///     <see cref="AsyncMapBase.AsyncToStart{TInput}" /> with
    ///     <paramref name="incoming" /> to start it immediately. Without that, the
    ///     item keeps the Queued status. To start a Queued item after this, keep
    ///     <paramref name="incoming" /> in <paramref name="state" />, and not only its
    ///     Value. That keeps its identity and its cancellation during the wait, and it
    ///     is the value that <see cref="OnCompleted" /> gives back.
    ///     <paramref name="tracked" /> gives the same items, thus a strategy that
    ///     reads only the queue of the pipeline can keep no queue of its own.
    /// </summary>
    /// <param name="state">The scheduling state of this MapAsync call.</param>
    /// <param name="incoming">The value that the pipeline admits now.</param>
    /// <param name="tracked">
    ///     Each item that the pipeline tracks, Queued or Running, in the sequence of
    ///     their admissions. It does not hold <paramref name="incoming" />, which the
    ///     pipeline adds after this call. It holds each other item that the pipeline
    ///     tracks at this moment, with the status that item has now. An earlier edit
    ///     of this same transaction is in it. For example, an item that ended in this
    ///     transaction is gone, and an item that <see cref="OnCompleted" /> started in
    ///     this transaction is Running. This list does not change while this method
    ///     runs.
    /// </param>
    protected internal abstract IReadOnlyList<AsyncToStart<TInput>> Admit(
        TState state,
        AsyncQueuedItem<TInput> incoming,
        IReadOnlyList<AsyncTrackedItem<TInput>> tracked);

    /// <summary>
    ///     Gives two answers for one item whose operation returned or threw: if the
    ///     pipeline publishes that outcome, and which tracked items start. For
    ///     example, the next Queued item can start because of that end. The engine
    ///     always calls this in a SodaFlow transaction, as it calls
    ///     <see cref="Admit" />. Each <see cref="AsyncMapBase.AsyncToStart{TInput}" />
    ///     from this method must contain an
    ///     <see cref="AsyncMapBase.AsyncQueuedItem{TInput}" /> from a previous
    ///     <see cref="Admit" /> call. There is no path for a value with no admission.
    ///     <para>
    ///         One item, and never two. A stream carries one value for each
    ///         transaction, and the edit of the queue for this end is in the
    ///         transaction that sends its result. Thus, one such end is the most that
    ///         a transaction can hold. A cancellation is the one end that comes in a
    ///         group, and it goes to <see cref="OnCanceled" />.
    ///     </para>
    ///     <para>
    ///         The engine can call this method two times for one item. A listener that
    ///         throws in the transaction of the first call makes that transaction fail.
    ///         The engine then ends the item again in a new transaction, and publishes
    ///         nothing from that second call. The decision of the first call goes with
    ///         that transaction. A change to <paramref name="state" /> from that call
    ///         stays. Thus, a strategy must give a correct decision for an
    ///         item that it got before.
    ///     </para>
    /// </summary>
    /// <param name="state">The scheduling state of this MapAsync call.</param>
    /// <param name="item">
    ///     The item that ended. It is the instance that this strategy got for that
    ///     value in <see cref="Admit" />. It is the same instance, thus
    ///     ReferenceEquals against an item in <paramref name="state" /> tells you if
    ///     that item is the current run.
    /// </param>
    /// <param name="completion">
    ///     How the operation ended: it returned, or it threw. A cancellation is not
    ///     one of these ends. See <see cref="OnCanceled" />.
    /// </param>
    /// <param name="tracked">
    ///     Each item that the pipeline tracks, Queued or Running, in the sequence of
    ///     their admissions. It holds no <paramref name="item" />: the pipeline
    ///     removes that item before this call. Thus, a strategy can select the first
    ///     Queued item with no test against it. It holds each other item that the
    ///     pipeline tracks at this moment, with the status that item has now, and an
    ///     earlier edit of this same transaction is in it. An item that this decision
    ///     starts is Queued in it, and not Running. This list does not change while
    ///     this method runs.
    /// </param>
    protected internal abstract AsyncStrategyResult<TInput> OnCompleted(
        TState state,
        AsyncQueuedItem<TInput> item,
        AsyncCompletion completion,
        IReadOnlyList<AsyncTrackedItem<TInput>> tracked);

    /// <summary>
    ///     Gives the tracked items to start because a cancellation ended one item or
    ///     more. There is no publish decision here: a canceled item sends nothing to
    ///     the results and nothing to the errors. A cancellation is always an expected
    ///     end with no message. Its source is external code, a strategy that replaces
    ///     its own previous run, or a Queued item that a cancellation removes before
    ///     its turn.
    ///     <para>
    ///         The engine calls this one time for the cancellations of one
    ///         transaction. One send of a cancellation stream can end each Queued item
    ///         and each Running item whose operation observes its token, all at one
    ///         instant. One call gives one decision over the queue that holds no item
    ///         of those ends. Thus, a strategy does not start an item that is about to
    ///         end.
    ///     </para>
    ///     <para>
    ///         A strategy that holds a reference to its current run clears that
    ///         reference here.
    ///         Otherwise, the last value stays in memory after the pipeline becomes
    ///         empty.
    ///     </para>
    ///     <para>
    ///         The engine can call this method two times for the same items, for the
    ///         cause that <see cref="OnCompleted" /> gives.
    ///     </para>
    /// </summary>
    /// <param name="state">The scheduling state of this MapAsync call.</param>
    /// <param name="canceled">
    ///     Each item that a cancellation ended now, in the sequence of their
    ///     admissions. Each one is the instance that this strategy got in
    ///     <see cref="Admit" />.
    /// </param>
    /// <param name="tracked">
    ///     The queue of this pipeline, in the sequence of the admissions. It holds no
    ///     item of <paramref name="canceled" />: the pipeline removes each of those
    ///     before this call. The other rules of the <see cref="OnCompleted" /> list
    ///     hold here also.
    /// </param>
    /// <returns>
    ///     Tracked items to start now, or to promote now. Give
    ///     <see cref="AsyncMapBase.AsyncStrategyResult{TInput}.None" /> for no items.
    /// </returns>
    protected internal abstract IReadOnlyList<AsyncToStart<TInput>> OnCanceled(
        TState state,
        IReadOnlyList<AsyncQueuedItem<TInput>> canceled,
        IReadOnlyList<AsyncTrackedItem<TInput>> tracked);
}

/// <summary>
///     The container of the strategies in the library: Parallel, Queue,
///     QueuePerGroup, and SwitchLatest. Each one uses only the schedule, and not
///     the <c>TInput</c> or the <c>TResult</c> of the call. Thus, each one is
///     generic over a <c>TUnit</c> from the caller. QueuePerGroup is generic over
///     the input type also, because it calculates a group key from the input. No
///     strategy has one fixed type for a value that it does not use. Thus, one
///     shared implementation of the schedule serves each language wrapper, and
///     each wrapper gives the type that is natural for it as <c>TUnit</c>. The C#
///     wrapper gives <c>SodaFlow.Functional.Unit</c>, and the F# wrapper gives its
///     own <c>unit</c>. The alternatives are a public type in Core, which is not
///     possible because Core has no dependency on SodaFlow.Functional, or the same
///     logic in each wrapper.
///     This class is internal and not public. Each method here is generic, thus
///     the class does not be. An internal class lets each language wrapper build
///     its own public interface with its own types above it, and does not show
///     this generic surface. The wrappers are SodaFlow.Async and
///     SodaFlow.FSharp.Async, and the two are friend assemblies through IVT.
///     The name is different from
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" />, and this class is
///     not a subclass of that class. F# does not always resolve a bare type name
///     across two generic arities of the same name, also with a full
///     qualification. The short hierarchy in the C# wrapper has the same name and
///     is correct, because a type in the consuming assembly has priority above a
///     type of the same name from a reference. The F# wrapper has no type of its
///     own for that priority. Thus, this class keeps a name that is not ambiguous,
///     and does not overload "AsyncConcurrencyStrategy" by arity. The nested
///     strategies below never have that ambiguity, because they always name
///     <see cref="AsyncConcurrencyStrategy{TInput,TState}" /> with its three type
///     arguments, and that gives the arity at each other use of the name.
/// </summary>
internal static class AsyncConcurrencyStrategyFactory
{
    /// <summary>
    ///     Each send starts its own operation immediately. The results come in the
    ///     sequence of their ends.
    /// </summary>
    /// <typeparam name="TUnit">
    ///     The type of the calling wrapper for a value that this strategy does not
    ///     use. It replaces the input type and the result type. The C# wrapper gives
    ///     <c>SodaFlow.Functional.Unit</c>, and the F# wrapper gives its own
    ///     <c>unit</c>. This strategy never reads a value of it.
    /// </typeparam>
    /// <param name="unitValue">
    ///     The one value of <typeparamref name="TUnit" />. It is the state of this
    ///     strategy for each call, and no code reads it.
    /// </param>
    /// <returns>A strategy instance with no state, for use by more than one call.</returns>
    internal static AsyncConcurrencyStrategyBase<TUnit> Parallel<TUnit>(TUnit unitValue) =>
        new ParallelStrategy<TUnit>(unitValue);

    /// <summary>
    ///     One operation or no operation runs at a time. A subsequent send goes to the
    ///     queue, and the queue runs in sequence.
    /// </summary>
    /// <typeparam name="TUnit">
    ///     The type of the calling wrapper for a value that this strategy does not
    ///     use. See <see cref="Parallel{TUnit}" />.
    /// </typeparam>
    /// <returns>A strategy instance with no state, for use by more than one call.</returns>
    internal static AsyncConcurrencyStrategyBase<TUnit> Queue<TUnit>() => QueueStrategy<TUnit>.Instance;

    /// <summary>
    ///     Builds a strategy with one queue for each group.
    ///     <paramref name="getGroup" /> puts each input in a group. In one group, a
    ///     subsequent send goes behind the sends before it, as <see cref="Queue" />
    ///     does. Two different groups do not wait for each other.
    /// </summary>
    /// <typeparam name="TUnit">
    ///     The type of the calling wrapper for a value that this strategy does not
    ///     use. It replaces the result type only, because this strategy uses its input
    ///     to calculate a group key. See <see cref="Parallel{TUnit}" />.
    /// </typeparam>
    /// <typeparam name="TInput">The input type of <paramref name="getGroup" />.</typeparam>
    /// <typeparam name="TGroup">The type of the group key.</typeparam>
    /// <param name="getGroup">
    ///     Calculates the group key of an input value. It must be deterministic,
    ///     because the strategy calls it at the admission of the value and again at
    ///     its end. The two calls must agree, or the strategy cannot find the queue of
    ///     the item.
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
    ///     The type of the calling wrapper for a value that this strategy does not
    ///     use. See <see cref="Parallel{TUnit}" />.
    /// </typeparam>
    /// <returns>A strategy instance with no state, for use by more than one call.</returns>
    internal static AsyncConcurrencyStrategyBase<TUnit> SwitchLatest<TUnit>() => SwitchLatestStrategy<TUnit>.Instance;

    private sealed class ParallelStrategy<TUnit>(in TUnit unitValue)
        : AsyncConcurrencyStrategy<TUnit, TUnit>
    {
        private readonly TUnit unitValue = unitValue;

        protected override TUnit CreateState() => this.unitValue;

        protected internal override IReadOnlyList<AsyncToStart<TUnit>> Admit(
            TUnit state,
            AsyncQueuedItem<TUnit> incoming,
            IReadOnlyList<AsyncTrackedItem<TUnit>> tracked) => [new(incoming)];

        protected internal override AsyncStrategyResult<TUnit> OnCompleted(
            TUnit state,
            AsyncQueuedItem<TUnit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<TUnit>> tracked) =>
            new(publish: true, next: AsyncStrategyResult<TUnit>.None);

        // Each item starts at its admission, thus a cancellation frees nothing to start.
        protected internal override IReadOnlyList<AsyncToStart<TUnit>> OnCanceled(
            TUnit state,
            IReadOnlyList<AsyncQueuedItem<TUnit>> canceled,
            IReadOnlyList<AsyncTrackedItem<TUnit>> tracked) =>
            AsyncStrategyResult<TUnit>.None;
    }

    private sealed class QueueStrategy<TUnit>
        : AsyncConcurrencyStrategy<TUnit, object?>
    {
        internal static readonly QueueStrategy<TUnit> Instance = new();

        private QueueStrategy()
        {
        }

        // This strategy holds no state of its own. The queue that it reads is the queue of the
        // pipeline, which keeps each item that no code started in the sequence of the admissions.
        protected override object? CreateState() => null;

        protected internal override IReadOnlyList<AsyncToStart<TUnit>> Admit(
            object? state,
            AsyncQueuedItem<TUnit> incoming,
            IReadOnlyList<AsyncTrackedItem<TUnit>> tracked) =>
            IsBusy(tracked)

                // The item keeps the Queued status, and a cancellation can remove it during the
                // wait. The pipeline holds it, thus this strategy does not.
                ? AsyncStrategyResult<TUnit>.None
                : [new AsyncToStart<TUnit>(incoming)];

        protected internal override AsyncStrategyResult<TUnit> OnCompleted(
            object? state,
            AsyncQueuedItem<TUnit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<TUnit>> tracked) =>
            new(publish: true, next: StartOne(tracked));

        protected internal override IReadOnlyList<AsyncToStart<TUnit>> OnCanceled(
            object? state,
            IReadOnlyList<AsyncQueuedItem<TUnit>> canceled,
            IReadOnlyList<AsyncTrackedItem<TUnit>> tracked) =>
            StartOne(tracked);

        // One item starts, whatever the count of the ends. A cancellation can end the item that
        // runs and some items that wait, and one item runs at a time.
        //
        // `tracked` holds no item that ended, thus the test for the next item takes the first
        // Queued item. The sequence of `tracked` is the sequence of the admissions.
        private static IReadOnlyList<AsyncToStart<TUnit>> StartOne(IReadOnlyList<AsyncTrackedItem<TUnit>> tracked)
        {
            AsyncTrackedItem<TUnit>? next = IsBusy(tracked) ? null : FirstQueued(tracked);

            return next is null
                ? AsyncStrategyResult<TUnit>.None
                : [new AsyncToStart<TUnit>(next.Item)];
        }

        private static bool IsBusy(IReadOnlyList<AsyncTrackedItem<TUnit>> tracked)
        {
            // An indexed loop, and not Any: this runs for each admission, and the item with the
            // Running status is the oldest one that the pipeline holds, thus it is at the start
            // of the list.
            // ReSharper disable once LoopCanBeConvertedToQuery - Done for performance reasons.
            // ReSharper disable once ForCanBeConvertedToForeach - Done for performance reasons.
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i].Status == AsyncItemStatus.Running)
                {
                    return true;
                }
            }

            return false;
        }

        private static AsyncTrackedItem<TUnit>? FirstQueued(IReadOnlyList<AsyncTrackedItem<TUnit>> tracked)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery - Done for performance reasons.
            // ReSharper disable once ForCanBeConvertedToForeach - Done for performance reasons.
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i].Status == AsyncItemStatus.Queued)
                {
                    return tracked[i];
                }
            }

            return null;
        }
    }

    /// <summary>
    ///     One queue for each group, in the style of
    ///     <see cref="QueueStrategy{TUnit}" />. Each queue operates independently. A
    ///     group selector puts each input in a group.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class QueuePerGroupStrategy<TUnit, TInput, TGroup>(
        in Func<TInput, TGroup> getGroup,
        in IEqualityComparer<TGroup>? groupComparer)
        : AsyncConcurrencyStrategy<TInput, QueuePerGroupStrategy<TUnit, TInput, TGroup>.State>
        where TGroup : notnull
    {
        private readonly Func<TInput, TGroup> getGroup = getGroup;
        private readonly IEqualityComparer<TGroup>? groupComparer = groupComparer;

        protected override State CreateState() => new(this.groupComparer);

        protected internal override IReadOnlyList<AsyncToStart<TInput>> Admit(
            State state,
            AsyncQueuedItem<TInput> incoming,
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked)
        {
            TGroup group = this.getGroup(incoming.Value);

            return this.IsBusy(tracked: tracked, state: state, group: group)

                // The item keeps the Queued status, and a cancellation can remove it during the
                // wait. The pipeline holds it, thus this strategy does not.
                ? AsyncStrategyResult<TInput>.None
                : [new AsyncToStart<TInput>(incoming)];
        }

        protected internal override AsyncStrategyResult<TInput> OnCompleted(
            State state,
            AsyncQueuedItem<TInput> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked)
        {
            TGroup group = this.getGroup(item.Value);

            if (this.IsBusy(tracked: tracked, state: state, group: group))
            {
                return new AsyncStrategyResult<TInput>(publish: true, next: AsyncStrategyResult<TInput>.None);
            }

            // `tracked` holds no item that ended, thus this takes the first Queued item of the
            // group. The sequence of `tracked` is the sequence of the admissions, and a different
            // group between two items of this group does not change that sequence.
            AsyncTrackedItem<TInput>? first =
                this.FirstQueued(tracked: tracked, state: state, group: group);

            return new AsyncStrategyResult<TInput>(
                publish: true,
                next: first is null
                    ? AsyncStrategyResult<TInput>.None
                    : [new AsyncToStart<TInput>(first.Item)]);
        }

        protected internal override IReadOnlyList<AsyncToStart<TInput>> OnCanceled(
            State state,
            IReadOnlyList<AsyncQueuedItem<TInput>> canceled,
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked)
        {
            // One item starts for each group that becomes free. The items of one call can be in
            // more than one group, thus this reads the group of each one. A group with two of them
            // gives one start, because the second test finds the first start in `started`.
            List<AsyncToStart<TInput>> next = [];
            List<TGroup> started = [];

            // ReSharper disable once ForCanBeConvertedToForeach - Done for performance reasons.
            for (int i = 0; i < canceled.Count; i++)
            {
                TGroup group = this.getGroup(canceled[i].Value);

                if (started.Exists(other => state.GroupComparer.Equals(x: other, y: group))
                    || this.IsBusy(tracked: tracked, state: state, group: group))
                {
                    continue;
                }

                AsyncTrackedItem<TInput>? first =
                    this.FirstQueued(tracked: tracked, state: state, group: group);

                if (first is null)
                {
                    continue;
                }

                started.Add(group);
                next.Add(new AsyncToStart<TInput>(first.Item));
            }

            return next;
        }

        private bool IsBusy(IReadOnlyList<AsyncTrackedItem<TInput>> tracked, State state, TGroup group)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery - Done for performance reasons.
            // ReSharper disable once ForCanBeConvertedToForeach - Done for performance reasons.
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i].Status == AsyncItemStatus.Running
                    && state.GroupComparer.Equals(x: this.getGroup(tracked[i].Item.Value), y: group))
                {
                    return true;
                }
            }

            return false;
        }

        private AsyncTrackedItem<TInput>? FirstQueued(
            IReadOnlyList<AsyncTrackedItem<TInput>> tracked,
            State state,
            TGroup group)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery - Done for performance reasons.
            // ReSharper disable once ForCanBeConvertedToForeach - Done for performance reasons.
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i].Status == AsyncItemStatus.Queued
                    && state.GroupComparer.Equals(x: this.getGroup(tracked[i].Item.Value), y: group))
                {
                    return tracked[i];
                }
            }

            return null;
        }

        /// <summary>
        ///     The comparer for the group keys. This strategy holds no queue: the queue of
        ///     the pipeline holds each item, and a group is a test on that queue.
        /// </summary>
        public sealed class State(in IEqualityComparer<TGroup>? groupComparer)
        {
            internal readonly IEqualityComparer<TGroup> GroupComparer =
                groupComparer ?? EqualityComparer<TGroup>.Default;
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
            AsyncQueuedItem<TUnit> incoming,
            IReadOnlyList<AsyncTrackedItem<TUnit>> tracked)
        {
            // This cancels the item that the new item replaces, through the cancellation of that
            // item. This code makes no second CancellationTokenSource, and holds and disposes
            // none. The call is safe when that item ended before now.
            state.Active?.Cancel();
            state.Active = incoming;

            return [new AsyncToStart<TUnit>(incoming)];
        }

        protected internal override AsyncStrategyResult<TUnit> OnCompleted(
            State state,
            AsyncQueuedItem<TUnit> item,
            AsyncCompletion completion,
            IReadOnlyList<AsyncTrackedItem<TUnit>> tracked)
        {
            // Publish only the run that no newer run replaced. An older run that a new send
            // replaced ends here also, and no code wants its result.
            bool active = state.Active != null && state.Active.Id == item.Id;

            if (active)
            {
                state.Active = null;
            }

            // Nothing starts at an end. The Admit above promotes each item that it admits, thus
            // no item waits with the Queued status, and the queue holds nothing to start. The test
            // SwitchLatest_LeavesNoItemQueued reads each value of the cell and holds that rule.
            return new AsyncStrategyResult<TUnit>(publish: active, next: AsyncStrategyResult<TUnit>.None);
        }

        protected internal override IReadOnlyList<AsyncToStart<TUnit>> OnCanceled(
            State state,
            IReadOnlyList<AsyncQueuedItem<TUnit>> canceled,
            IReadOnlyList<AsyncTrackedItem<TUnit>> tracked)
        {
            // This removes the reference at the end of the current run. Thus, the last QueuedItem,
            // and its value, do not stay in memory after the pipeline becomes empty. A cancellation
            // of more than one item can hold the current run and the runs that it replaced.
            // ReSharper disable once ForCanBeConvertedToForeach - Done for performance reasons.
            for (int i = 0; i < canceled.Count; i++)
            {
                if (state.Active != null && state.Active.Id == canceled[i].Id)
                {
                    state.Active = null;
                }
            }

            // A cancellation starts nothing, for the cause that OnCompleted above gives: this
            // strategy keeps no queue, because Admit promotes each item.
            return AsyncStrategyResult<TUnit>.None;
        }

        /// <summary>The item that runs, when there is one. Each new send replaces it.</summary>
        public sealed class State
        {
            internal AsyncQueuedItem<TUnit>? Active;
        }
    }
}

/// <summary>
///     Runs one MapAsync pipeline. It starts the operations, catches the
///     exceptions, sends the results and the errors, tracks the Queued items and
///     the Running items, connects the external cancellation, and controls the
///     transaction limits. The external cancellation includes a cancellation
///     before a start. A <see cref="AsyncConcurrencyStrategy{TInput,TState}" />
///     does none of this. A strategy answers Admit and OnCompleted with data only,
///     and this class does the operations in that answer. This code makes one
///     instance for each MapAsync call (see
///     <see
///         cref="AsyncStreamUtility.MapAsyncImpl{TInput,TResult,TStrategyInput}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Action{Exception},MapAsyncDeliveryExceptionFactory,MapAsyncOperation{TInput,TResult},AsyncConcurrencyStrategyBase{TStrategyInput},Func{TInput,TStrategyInput},Stream{UnitInternal},Stream{IReadOnlyCollection{TInput}},bool)" />
///     ), and that instance holds the one state of the call. This code makes that
///     state at the start and never shares it between two calls. Thus, more than
///     one call can use the same strategy instance safely, at the same time. This
///     class and <see cref="AsyncConcurrencyStrategy{TInput,TState}" /> share
///     <see cref="AsyncMapBase" /> only to get the nested data types of that base,
///     such as <see cref="AsyncMapBase.AsyncQueuedItem{TInput}" />. This class is
///     not a subtype of the strategy, and the two have no other relation.
///     In this class, each value that the strategy reads has the type
///     <typeparamref name="TStrategyInput" /> and not
///     <typeparamref name="TInput" />. Those values are the tracked items and
///     ToStart. Only a <typeparamref name="TInput" /> value comes in, from
///     <c>source</c>, and only a <typeparamref name="TResult" /> value goes out,
///     to <c>results</c>. The conversion at the input edge is the work of this
///     class and not of the strategy: <see cref="inputConverter" />. That
///     converter usually has no inverse, thus this code never calculates the
///     initial <typeparamref name="TInput" /> value again from the converted
///     value. It keeps the two values together. See <see cref="Entry" /> and the
///     <c>value</c> parameter that goes through <see cref="PromoteAndLaunch" />
///     into <see cref="StartOperation" />.
///     The strategy reads no result at all. It reads
///     <see cref="AsyncMapBase.AsyncCompletion" />, which says how the operation
///     ended, and <see cref="Flush" /> asks it before this class makes the result.
///     See <see cref="Publish" />.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class AsyncMapExecutionManager<TInput, TResult, TStrategyInput> : AsyncMapBase
{
    // The number of ids up to which EntryIndex and IdSet search an array and make no dictionary or
    // set.
    private const int LinearSearchLimit = 8;

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

    // Gets each exception that has no caller and that the errors cannot receive. Report is the one
    // caller. See the handleException parameter of MapAsyncImpl.
    private readonly Action<Exception> handleException;

    // Makes the report of Recover. See the createDeliveryException parameter of MapAsyncImpl.
    private readonly MapAsyncDeliveryExceptionFactory createDeliveryException;

    // Each value that Execute puts into this pipeline, with the ExecuteCompletion that the
    // Task of that call answers. This sink is private to this manager, thus Execute is the one
    // sender. SendAdmission always sends in a transaction of its own: it opens one where none is
    // open, and it defers where one is. This sink and `source` thus never fire in one transaction.
    // One OrElse of the two is sufficient, and the pipeline keeps the value of each side.
    private readonly StreamSink<Admission> executeRequests = StreamInternal.CreateSinkImpl<Admission>();

    private readonly Func<TInput, TStrategyInput> inputConverter;

    private readonly MapAsyncOperation<TInput, TResult> operation;

    // This carries the queue of tracked items after the edits that Flush makes. One sequence of
    // edits makes the value a strategy reads. It makes the value of the cell also. Thus, the two
    // cannot disagree.
    //
    // Flush sends one value for each transaction, because one transaction holds one call of Flush.
    // See the OrElse in Attach. The coalesce keeps the last value, which is correct for each count
    // of sends. Each edit applies to the queue that the edit before it made.
    //
    // Flush is not in a SodaFlow callback. A continuation on a background thread opens its own
    // transaction, and the deferred path opens one for each action. Thus Send is legal.
    private readonly StreamSink<Entry[]> queueUpdates =
        StreamInternal.CreateSinkImpl<Entry[]>(static (_, last) => last);

    private readonly StreamSink<TResult> results;

    // This code makes this at the start, as the class remarks give, and never replaces it. No
    // lock protects it, and no lock is necessary. Only code in a SodaFlow transaction uses it:
    // the Map in Attach, and the Apply in Flush. SodaFlow puts each transaction
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

    // The queue after the edits of Flush, with the transaction those edits belong to. A
    // Sample of the cell in a transaction gives the value from the start of that transaction. The
    // cell takes a new value at the end of one. Thus, an Admit that runs after Flush, in the same
    // transaction, cannot read the cell. It reads this field.
    //
    // Flush is the one writer and CurrentQueue is the one reader. Flush keeps its own two edits as
    // local values, thus OnCompleted needs no field. EndItem gives Flush a transaction of its own.
    // Thus, one transaction holds one call of Flush, and that call is the first work in it. A
    // firing of `source` cannot come before it. Thus, the queue after Flush is the only value that
    // a read after it, in the same transaction, can want.
    //
    // Owner is the transaction that the value belongs to. A transaction that ends, and a
    // transaction that throws, keep a value against an owner that no code uses again. The next
    // transaction finds a different owner and reads the cell. Thus, no action must run to clear
    // this field. That is necessary, because Transaction discards its queue of last actions when
    // it throws.
    //
    // EndItem gives Flush a transaction of its own in each condition. Where no transaction is
    // open, EndItem calls Flush, and the Apply in Flush opens one. Where one is open, EndItem
    // defers Flush until that transaction releases the lock, and the Apply in Flush then opens a
    // new one. Thus, Owner is always the transaction of Flush. The read that must find this field is an Admit in that same
    // transaction. The publish of Flush sends a result, and the graph can send that result back to
    // `source`. An Admit in a different transaction finds a different owner, and the cell holds the
    // value by then.
    private (Entry[] Queue, TransactionInternal Owner)? completedQueueAndOwner;

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

    // The canceled ends that this transaction made, and the transaction they belong to. A
    // cancellation can end more than one item at one instant, and no canceled end sends anything.
    // Thus, one Flush reads them all, and the strategy makes one decision over them. An end that
    // can send does not come here: see EndItem. A transaction that ends, and a transaction that
    // throws, keep a list against an owner that no code uses again. The next transaction makes a
    // list of its own.
    private (List<PendingEnd> Ends, TransactionInternal Owner)? pendingCancellations;

    internal AsyncMapExecutionManager(
        AsyncConcurrencyStrategyBase<TStrategyInput> strategy,
        Func<TInput, TStrategyInput> inputConverter,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Action<Exception> handleException,
        MapAsyncDeliveryExceptionFactory createDeliveryException,
        MapAsyncOperation<TInput, TResult> operation,
        Stream<UnitInternal>? cancelAll,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching,
        bool cancelOnDispose)
    {
        this.inputConverter = inputConverter;
        this.stateManager = strategy.CreateStateManager();
        this.results = results;
        this.errors = errors;
        this.handleException = handleException;
        this.createDeliveryException = createDeliveryException;
        this.operation = operation;
        this.cancelAll = cancelAll;
        this.cancelMatching = cancelMatching;
        this.cancelOnDispose = cancelOnDispose;
    }

    internal AsyncMapStatus<TInput, TResult> Attach(Stream<TInput> source)
    {
        Cell<Entry[]> trackedCell =
            TransactionInternal.Apply(
                code: static (trans, _, state) => state.Self.BuildTrackedCell(trans: trans, source: state.Source),
                state: (Self: this, Source: source));

        // Snapshot puts each send on a cancellation stream with the value of `tracked` from
        // the start of that transaction. Thus, a cancellation and an admission in one transaction
        // cannot race each other. This applies to a Queued item and to a Running item in the same
        // manner, because each tracked entry has its own CancellationTokenSource from its
        // admission, at each status.
        //
        // The listener selects the entries, and CancelAfterCommit cancels them after the
        // transaction commits. A cancellation is an effect out of the graph, and nothing can
        // remove that effect. Before, the listener called Cancel() in its callback. A subsequent
        // throw in that transaction did not stop the cancel, although no cancellation committed.
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
                    .SnapshotImpl(b: trackedCell, f: static (_, entries) => entries)
                    .ListenImpl(entries => this.CancelAfterCommit(entries: entries, trackedCell: trackedCell));
        }

        if (this.cancelMatching != null)
        {
            this.cancelMatchingListener =
                this.cancelMatching
                    .SnapshotImpl(
                        b: trackedCell,
                        f: static (toCancel, entries) => (ToCancel: toCancel, Entries: entries))
                    .ListenImpl(pair =>
                    {
                        if (pair.ToCancel.Count == 0)
                        {
                            return;
                        }

                        // This compares against the initial TInput value of each tracked Entry,
                        // which is Entry.Value, with the default equality comparer for TInput.
                        // cancelMatching uses TInput and not TStrategyInput, thus this code needs
                        // no conversion.
                        //
                        // The selection runs here, in the transaction, and not with the cancel
                        // after it. It calls the equality of TInput, which is code of the caller.
                        // A throw from that code here fails the transaction and cancels nothing.
                        HashSet<TInput> targets =
                            new(collection: pair.ToCancel, comparer: EqualityComparer<TInput>.Default);

                        this.CancelAfterCommit(
                            entries: Array.FindAll(array: pair.Entries, match: e => targets.Contains(e.Item.Value)),
                            trackedCell: trackedCell);
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
                .SnapshotImpl(b: trackedCell, f: static (_, entries) => entries)
                .ListenImpl(entries => this.CancelTracked(entries: entries, trackedCell: trackedCell));

        Cell<bool> isRunning =
            trackedCell.MapImpl(static entries =>
                Array.Exists(array: entries, match: static e => e.Status == AsyncItemStatus.Running));

        Cell<IReadOnlyList<AsyncItem<TInput>>> items =
            trackedCell.MapImpl<IReadOnlyList<AsyncItem<TInput>>>(static entries =>
                Array.ConvertAll(
                    array: entries,
                    converter: static e => new AsyncItem<TInput>(value: e.Item.Value, status: e.Status)));

        return new AsyncMapStatus<TInput, TResult>(
            isRunning: isRunning,
            items: items,
            dispose: this.Dispose,
            execute: this.Execute,
            executeCell: this.ExecuteFromCell,
            executeIfSome: this.ExecuteIfSome);
    }

    // The cell that holds the queue, built in the transaction that attaches the source. A static
    // lambda passes this and the source as the state, thus Attach captures nothing.
    //
    // CurrentQueue reads this cell at the first edit of each transaction. The loop is necessary
    // because the transform below is upstream of the cell and reads it. The block gets the source
    // as its state and captures only this.
    private Cell<Entry[]> BuildTrackedCell(TransactionInternal trans, Stream<TInput> source) =>
        LoopedCell<Entry[]>.WithoutCapturesImpl(
            trans: trans,
            state: source,
            f: (trackedCellLoop, source) =>
            {
                // Map runs as usual transaction code and is not a registered listener callback.
                // Thus, it runs in the transaction of the source. It must not Send. The
                // transaction counts it as a callback while a send drives it. Thus, it returns the
                // new queue into the graph, and Flush sends the edits that it makes.
                //
                // The pipeline tracks each admitted value from this moment. It adds the value with
                // the Queued status, and then promotes it to Running for each ToStart that Admit
                // returns. For the strategies in the library, that is usually the value itself.
                // After a disposal this code does nothing permanently, because no code calls Admit
                // again and thus no value goes to the queue or starts.
                Stream<Entry[]> starts =
                    source.MapImpl(static o => new Admission(value: o, completion: null))
                        .OrElseImpl(this.executeRequests)
                        .MapImpl(admission =>
                        {
                            TInput o = admission.Value;

                            if (this.disposed)
                            {
                                // Execute answers in each condition, thus a value that arrives after a
                                // disposal is a cancellation and not a value that disappears.
                                admission.Completion?.TrySetCanceled();

                                return this.CurrentQueue(trackedCellLoop);
                            }

                            CancellationTokenSource cancellation = new();

                            Guid newEntryId = Guid.NewGuid();

                            // Here inputConverter changes TInput into TStrategyInput. This is the input
                            // edge, and the class remarks give more. The newEntry below keeps the
                            // initial TInput, because the result of inputConverter usually has no
                            // inverse.
                            AsyncQueuedItem<TStrategyInput> incoming =
                                new(
                                    value: this.inputConverter(o),
                                    id: newEntryId,
                                    cancellation: cancellation);

                            Entry newEntry =
                                new(
                                    item: new AsyncQueuedItem<TInput>(
                                        value: o,
                                        id: newEntryId,
                                        cancellation: cancellation),
                                    tracked: new AsyncTrackedItem<TStrategyInput>(
                                        item: incoming,
                                        status: AsyncItemStatus.Queued),
                                    value: o,
                                    completion: admission.Completion);

                            // The queue that the strategy reads holds each item that this pipeline
                            // tracks now. That is the value after each edit of this transaction. It
                            // does not hold `incoming`, because the edit below is what adds that one.
                            Entry[] tracked = this.CurrentQueue(trackedCellLoop);

                            IReadOnlyList<AsyncToStart<TStrategyInput>> toStart =
                                this.stateManager.Admit(
                                    incoming: incoming,
                                    tracked: new TrackedItems(tracked));

                            Guid[] promote = new Guid[toStart.Count];
                            TInput[] values = new TInput[toStart.Count];
                            EntryIndex trackedIndex = new(entries: tracked, lookups: toStart.Count);

                            for (int i = 0; i < toStart.Count; i++)
                            {
                                promote[i] = toStart[i].Item.Id;

                                if (newEntryId == promote[i])
                                {
                                    values[i] = o;
                                }
                                else
                                {
                                    int position = trackedIndex.IndexOf(promote[i]);

                                    if (position < 0)
                                    {
                                        throw new InvalidOperationException("Could not find item to start.");
                                    }

                                    values[i] = tracked[position].Value;
                                }
                            }

                            // A strategy refuses a value where it cancels `incoming` and does not
                            // promote it. The entry then keeps the Queued status permanently, thus no
                            // end comes for it, and Execute gets no answer from that path. A promoted item that a
                            // cancellation holds is different: PromoteAndLaunch ends that one, and the
                            // usual end path answers the Task.
                            if (admission.Completion != null
                                && cancellation.IsCancellationRequested
                                && Array.IndexOf(array: promote, value: newEntryId) < 0)
                            {
                                admission.Completion.TrySetCanceled();
                            }

                            for (int i = 0; i < toStart.Count; i++)
                            {
                                this.PromoteAndLaunch(
                                    toStart: toStart[i],
                                    value: values[i],
                                    trackedCell: trackedCellLoop);
                            }

                            // One edit: the add of `incoming`, and the promotion of each item that
                            // starts. Apply removes, then adds, then promotes, thus the promotion of
                            // `incoming` in this same edit finds the entry that the add put there.
                            return Apply(
                                list: tracked,
                                mutation: new Mutation(
                                    remove: [],
                                    promote: promote,
                                    add: [newEntry]));
                        });

                // Hold and not Accum. This class makes the queue itself, with Apply, and
                // the cell carries the value that it made. One sequence of edits makes the value
                // that a strategy reads and the value that the cell takes. Thus, the two cannot
                // disagree about the queue, or about the sequence of the edits.
                //
                // OrElse, and the sequence of the two is what makes it correct. One transaction
                // gives at most one value here from each side. `source` is a stream, thus it fires
                // one time for each transaction. One transaction holds one call of Flush,
                // because a continuation opens its own transaction and the deferred path opens one
                // for each action.
                //
                // Where the two fire together, the graph of the caller makes `source` from the
                // results. The value of `starts` then comes after the value of Flush. Flush
                // sends in its own body. The publish operation that admits the next value goes to
                // `source` when this transaction sends the values it holds, which is after that
                // body. OrElse takes the value on the left, thus `starts` is on the left.
                Cell<Entry[]> trackedCell =
                    starts.OrElseImpl(this.queueUpdates).HoldImpl([]);

                return trackedCell;
            });

    // This is the one limit in this class where the code starts a Task and does not wait for it.
    // StartOperation catches each exception and gives it to Report, which never throws. Thus, its
    // Task cannot fault. This continuation is a guard against a change that breaks that rule. It
    // reads the exception, which marks the Task as observed, and it gives the exception to Report.
    //
    // Before, this code threw a fault again in the continuation. That made an unobserved task
    // exception, which ends the process on .NET Framework where ThrowUnobservedTaskExceptions is
    // on.
    private void FireAndForget(Task task) =>
        task.ContinueWith(
            // ReSharper disable once NullableWarningSuppressionIsUsed - This continuation has
            // TaskContinuationOptions.OnlyOnFaulted set, so the task's Exception property will be non-null.
            continuationAction: static (t, state) => (state as AsyncMapExecutionManager<TInput, TResult, TStrategyInput>)?.Report(t.Exception!.GetBaseException()),
            state: this,
            cancellationToken: CancellationToken.None,
            continuationOptions: TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            scheduler: TaskScheduler.Default);

    // Gives an exception with no caller to handleException. A throw from that action is a defect
    // of the caller. It goes to the thread pool, where it ends the process, as a throw from the
    // handler of a timer system does. It must not come back into this class, where a Task holds it
    // and no code observes it.
    private void Report(Exception error)
    {
        try
        {
            this.handleException(error);
        }
        catch (Exception handlerFailure)
        {
            ExceptionDispatchInfo failure = ExceptionDispatchInfo.Capture(handlerFailure);

            ThreadPool.QueueUserWorkItem(callBack: static state => (state as ExceptionDispatchInfo)?.Throw(), state: failure);
        }
    }

    // The queue that a strategy reads. A call of Flush in this transaction leaves the queue it made
    // in completedQueueAndOwner, and this gives that value. Where no call of Flush in this
    // transaction left one, the cell gives the queue from the start of the transaction, which is
    // the correct answer.
    //
    // The transform in Attach is the one caller, and it always runs in a transaction. The test for
    // a transaction records that. It changes no answer, because a call with no transaction finds
    // no owner that is equal, and reads the cell.
    private Entry[] CurrentQueue(Behavior<Entry[]> trackedCell)
    {
        if (this.completedQueueAndOwner != null)
        {
            TransactionInternal? current = TransactionInternal.GetCurrentTransaction();

            if (current is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(this.CurrentQueue)} must be called from within a transaction.");
            }

            if (ReferenceEquals(objA: current, objB: this.completedQueueAndOwner.Value.Owner))
            {
                return this.completedQueueAndOwner.Value.Queue;
            }
        }

        return trackedCell.SampleImpl();
    }

    private static Entry[] Apply(Entry[] list, Mutation mutation)
    {
        if (mutation.Remove.Length > 0)
        {
            Entry[] kept = new Entry[list.Length];
            int count = 0;
            IdSet remove = new(mutation.Remove);

            // ReSharper disable once LoopCanBePartlyConvertedToQuery - Done for performance reasons.
            foreach (Entry e in list)
            {
                if (!remove.Contains(e.Item.Id))
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
            EntryIndex index = new(entries: list, lookups: mutation.Promote.Length);

            // ReSharper disable once LoopCanBePartlyConvertedToQuery - Done for performance reasons.
            foreach (Guid idToPromote in mutation.Promote)
            {
                int idx = index.IndexOf(idToPromote);

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

    // The three Execute methods of AsyncMapStatus<TInput, TResult> call these three. The remarks of
    // those methods give the contract, and SendAdmission below gives the mechanism.
    private Task<TResult> Execute(TInput value)
    {
        ExecuteCompletion<TResult, TResult> completion = NewExecuteCompletion();

        SendAdmission(
            send: () => this.SendExecuteRequest(value: value, completion: completion),
            completion: completion);

        return completion.Task;
    }

    // The read of the cell is in the function that this gives to SendAdmission. Thus, that read
    // is in the transaction of the send and not in this method.
    private Task<TResult> ExecuteFromCell(Cell<TInput> value)
    {
        ThrowHelper.ThrowIfNull(argument: value, paramName: nameof(value));

        ExecuteCompletion<TResult, TResult> completion = NewExecuteCompletion();

        SendAdmission(
            send: () => this.SendExecuteRequest(value: value.SampleImpl(), completion: completion),
            completion: completion);

        return completion.Task;
    }

    // The caller makes `completion`, because the type of its Task is the type of the caller. The
    // read and the test of the value are in the transaction of the send, for the same cause as in
    // ExecuteFromCell. Where `read` gives no value at that instant, the call admits nothing. It
    // does not wait for a value.
    private void ExecuteIfSome(
        Func<MaybeInternal<TInput>> read,
        ExecuteCompletion<TResult> completion,
        Action onNone) =>
        SendAdmission(
            send: () =>
            {
                if (read().TryGetValue(out TInput value))
                {
                    this.SendExecuteRequest(value: value, completion: completion);
                }
                else
                {
                    onNone();
                }
            },
            completion: completion);

    // Sends one admission of an Execute call, and never in a transaction that other code opened.
    //
    // Execute is for a call from the operation of a pipeline. An operation starts after the
    // transaction lock is free, thus such a call has no transaction open. A caller can also have a
    // transaction of its own open. Execute then defers and does not throw, as EndItem does, and
    // the admission gets a transaction of its own.
    //
    // `send` makes the value in the transaction of the send and not before it. Thus, an overload
    // that takes a cell reads that cell in the transaction that admits its value. PostImpl gives
    // that transaction: it opens one where none is open, and it defers to one where a transaction
    // is open.
    //
    // A transaction that fails discards the actions that PostImpl holds. The value then never
    // enters the pipeline, and no end comes for the Task. Thus, this gives PostImpl the release
    // that cancels `completion`, which PostImpl runs where the send does not. TrySetCanceled does
    // nothing where the send ran, thus the two paths cannot disagree.
    private static void SendAdmission(Action send, ExecuteCompletion<TResult> completion) =>
        TransactionInternal.PostImpl(
            action: send,
            onFailure: _ => completion.TrySetCanceled());

    private void SendExecuteRequest(TInput value, ExecuteCompletion<TResult> completion) =>
        this.executeRequests.SendImpl(new Admission(value: value, completion: completion));

    private static ExecuteCompletion<TResult, TResult> NewExecuteCompletion() => new(static result => result);

    /// <summary>
    ///     Stops this pipeline. See <see cref="AsyncMapStatus.Dispose" /> for the full
    ///     contract. The cancelOnDispose value at Attach sets the cancellation of the
    ///     tracked items, and this method has no parameter for it, because
    ///     IDisposable.Dispose() is the only public path to a disposal.
    ///     <see cref="disposeState" /> makes this method run one time, because each
    ///     thread can call it with no SodaFlow transaction open.
    /// </summary>
    private void Dispose()
    {
        if (Interlocked.CompareExchange(location1: ref this.disposeState, value: 1, comparand: 0) != 0)
        {
            return;
        }

        TransactionInternal.Apply(
            code: static (_, _, self) =>
            {
                self.disposed = true;

                if (self.cancelOnDispose)
                {
                    self.disposeCancelTrigger.SendImpl(UnitInternal.Value);
                }

                return UnitInternal.Value;
            },
            state: this);

        this.cancelAllListener?.Unlisten();
        this.cancelMatchingListener?.Unlisten();
        this.disposeCancelListener?.Unlisten();
    }

    private void PromoteAndLaunch(
        AsyncToStart<TStrategyInput> toStart,
        TInput value,
        Cell<Entry[]> trackedCell)
    {
        if (toStart.Item.Cancellation.IsCancellationRequested)
        {
            // A cancellation removed this item with the Queued status. This code ends it and does
            // not call the operation. It uses the usual end path, thus a strategy such as Queue in
            // the library starts the next item. EndItem defers itself where a transaction is open,
            // thus this call needs no deferral of its own. It puts this end with each other end of
            // the transaction, and one Flush after the transaction gives them to the strategy
            // together.
            this.EndItem(
                item: toStart.Item,
                pending: AsyncOutcome<MapAsyncResult<TResult>>.Canceled(),
                trackedCell: trackedCell,
                tokenToCheck: null);

            return;
        }

        // This code defers only the call of the operation. The transaction that promoted the
        // entry gave it the Running status synchronously. Post prevents the start of asynchronous work,
        // which is a side effect, in the transaction that processes the event. The post then holds
        // the call until the transaction lock is free. A posted action runs in a transaction, and
        // the code of an operation before its first await must not run with the lock. Code there
        // that blocks stops each thread from opening a transaction, and code that waits for a
        // transaction on a different thread never ends. AfterReleaseImpl runs the call on this
        // thread, thus the operation starts inline, as before, and not on the thread pool.
        // StartOperation is a usual async Task method. FireAndForget is the one position where this
        // code does not await its Task, and that is explicit and not a discard in an async void
        // method.
        TransactionInternal.PostImpl(() =>
            TransactionInternal.AfterReleaseImpl(() =>
                this.FireAndForget(
                    this.StartOperation(
                        toStart: toStart,
                        value: value,
                        trackedCell: trackedCell))));
    }

    private async Task StartOperation(
        AsyncToStart<TStrategyInput> toStart,
        TInput value,
        Cell<Entry[]> trackedCell)
    {
        // Each exception goes to Report, thus the Task of this method cannot fault. See
        // FireAndForget.
        try
        {
            // The operation monitors the token of the strategy, when there is one, such as an
            // external timeout, and also the cancellation of this item. The two are linked.
            CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    token1: toStart.StrategyToken,
                    token2: toStart.Item.Cancellation.Token);

            AsyncOutcome<MapAsyncResult<TResult>> pending;
            CancellationToken? tokenToCheck;

            // The try holds the operation and not the end of the item. A throw from the end is a
            // failure of a listener, and not a failure of the operation. Before, the catch below got it
            // and ended the item a second time, as if the operation threw.
            try
            {
                // This code calls the operation inline, on this thread, and awaits it. The
                // operation moves to a different thread only when it must, such as at its own first
                // await. This code never sends it to the thread pool only to start it. When the
                // operation ends before this await, the await continues synchronously and there is
                // no continuation on a different thread. `value` is the initial TInput at the
                // admission of this item. PromoteAndLaunch gives it to this method, as the class
                // remarks give, and no code calculates it again from TStrategyInput.
                MapAsyncResult<TResult> operationResult =
                    await this.operation(
                            input: value,
                            resultFactory: ResultFactory<TResult>.Instance,
                            token: linked.Token)
                        .ConfigureAwait(false);

                pending = AsyncOutcome<MapAsyncResult<TResult>>.Succeeded(operationResult);
                tokenToCheck = linked.Token;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                pending = AsyncOutcome<MapAsyncResult<TResult>>.Canceled();
                tokenToCheck = null;
            }
            catch (Exception ex)
            {
                pending = AsyncOutcome<MapAsyncResult<TResult>>.Failed(ex);
                tokenToCheck = linked.Token;
            }

            try
            {
                this.EndItem(
                    item: toStart.Item,
                    pending: pending,
                    trackedCell: trackedCell,
                    tokenToCheck: tokenToCheck);
            }
            finally
            {
                linked.Dispose();
            }
        }
        catch (Exception e)
        {
            this.Report(e);
        }
    }

    // Gives the cancel of the entries to the transaction of the listener as a posted action. That
    // action runs after the transaction commits, and a transaction that fails discards it. It
    // runs in a transaction of its own. Thus, EndItem collects the ends of the Queued entries for
    // one Flush, as before.
    private void CancelAfterCommit(Entry[] entries, Cell<Entry[]> trackedCell)
    {
        if (entries.Length == 0)
        {
            return;
        }

        TransactionInternal.PostImpl(
            action: static state => state.Self.CancelCommitted(entries: state.Entries, trackedCell: state.TrackedCell),
            state: (Self: this, Entries: entries, TrackedCell: trackedCell));
    }

    // Cancels each entry of a cancellation that committed, and ends the Queued ones.
    //
    // The entries are from the start of the transaction that asked for the cancel. An item of
    // them can end in that same transaction, for example where the cancellation stream comes from
    // the results. The Flush of that end disposes the source of the item at the commit, which is
    // before this code. Thus, this code uses Cancel() of the item, which does nothing for a
    // disposed source.
    //
    // Cancel() of a source runs the callbacks on its token and throws what they throw. That throw
    // must not stop the cancel of the entries after it, because the cancellation committed for
    // each of them. It has no caller: the transaction that asked for the cancel is complete.
    // Thus, each throw goes to handleException after the last entry.
    private void CancelCommitted(IEnumerable<Entry> entries, Cell<Entry[]> trackedCell)
    {
        List<Exception>? failures = null;

        foreach (Entry e in entries)
        {
            try
            {
                e.Item.Cancel();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }

            if (e.Status != AsyncItemStatus.Queued)
            {
                continue;
            }

            this.EndItem(
                item: e.Tracked.Item,
                pending: AsyncOutcome<MapAsyncResult<TResult>>.Canceled(),
                trackedCell: trackedCell,
                tokenToCheck: null);
        }

        if (failures != null)
        {
            this.Report(failures.Count == 1 ? failures[0] : new AggregateException(failures));
        }
    }

    // Cancels each entry at a disposal, and ends the Queued ones here. This does not wait for a
    // commit, because the pipeline stays disposed after a transaction that fails.
    //
    // A Running item observes its token and its operation ends, thus EndItem runs for it on the
    // usual path. A Queued item runs no operation, thus nothing observes its token. Before this,
    // the end of such an item waited for a promotion, and a promotion comes from the end of a
    // different item. Where no other item ended, the item stayed in the queue with the Queued
    // status, and no stream said that it ended.
    //
    // EndItem defers each end, because this code runs in a registered listener callback and a
    // send is not legal there. A canceled end sends nothing, thus a cancellation of more than one
    // item gives the strategy one call and not one call for each item. That call reads the queue
    // that holds no item of those ends.
    private void CancelTracked(IEnumerable<Entry> entries, Cell<Entry[]> trackedCell)
    {
        foreach (Entry e in entries)
        {
            e.Item.Cancellation.Cancel();

            if (e.Status != AsyncItemStatus.Queued)
            {
                continue;
            }

            this.EndItem(
                item: e.Tracked.Item,
                pending: AsyncOutcome<MapAsyncResult<TResult>>.Canceled(),
                trackedCell: trackedCell,
                tokenToCheck: null);
        }
    }

    // Ends an item, and never from a SodaFlow callback.
    //
    // The operation of a Running item ends on the thread that cancels its token: Cancel() runs the
    // registrations of the token, one of those completes the Task of the operation, and the
    // continuation of that Task runs there. That thread is in the callback of the listener for a
    // cancellation stream, thus a send from Flush throws, and the throw goes into the
    // machinery of Cancel() where no code reports it. The item then had an OnCompleted and stayed
    // in the queue. The strategy and the pipeline disagreed from that moment.
    //
    // A test for a transaction covers each path to here. A continuation on a thread from the pool
    // has none and calls Flush. A continuation on the thread of a cancellation has one, and
    // AfterReleaseImpl gives the end its own transaction after that one releases the lock.
    private void EndItem(
        AsyncQueuedItem<TStrategyInput> item,
        AsyncOutcome<MapAsyncResult<TResult>> pending,
        Cell<Entry[]> trackedCell,
        CancellationToken? tokenToCheck)
    {
        // A cancellation that arrived while the operation ran makes the item Canceled, also when
        // the operation gave a result or threw.
        AsyncOutcome<MapAsyncResult<TResult>> outcome =
            tokenToCheck is { IsCancellationRequested: true }
                ? AsyncOutcome<MapAsyncResult<TResult>>.Canceled()
                : pending;

        PendingEnd end = new(item: item, outcome: outcome, tokenToCheck: tokenToCheck);

        // A canceled end sends nothing, and it goes to OnCanceled. Each other end can send a
        // result or an error, and it goes to OnCompleted. One transaction carries one value of a
        // stream, thus one such end is the most that a transaction can hold. The edit of the queue
        // for such an end also belongs in the transaction that sends its result. Where it is not, a
        // graph that reads the queue and the result together sees two instants where the pipeline
        // made one. Thus, this code collects only canceled ends in one list.
        bool sends =
            outcome.Match(
                onSucceeded: static _ => true,
                onFailed: static _ => true,
                onCanceled: static () => false);

        TransactionInternal? current = TransactionInternal.GetCurrentTransaction();

        if (current is null)
        {
            // A continuation on a thread from the pool. This end is the only one of its
            // transaction, and Flush opens that transaction.
            List<PendingEnd> alone = [end];
            this.Flush(ends: alone, sends: sends, trackedCell: trackedCell);

            return;
        }

        // A transaction is open, thus this code can be in a callback, where a send is not legal.
        // AfterReleaseImpl runs the Flush after this transaction releases the lock, and the Apply in
        // Flush opens a transaction of its own. There, a send is legal and the cell holds the value
        // that this transaction gave it. PostImpl is not sufficient. A posted action runs in a child
        // transaction, and a failure of that child goes to the code that ran this transaction. Flush
        // must see its own failure, because it ends the items again after a failure.
        if (sends)
        {
            List<PendingEnd> alone = [end];

            TransactionInternal.AfterReleaseImpl(() => this.Flush(ends: alone, sends: true, trackedCell: trackedCell));

            return;
        }

        // The canceled ends of this transaction go in one list, and one Flush after the transaction
        // reads them.
        if (this.pendingCancellations is null
            || !ReferenceEquals(objA: this.pendingCancellations.Value.Owner, objB: current))
        {
            this.pendingCancellations = (Ends: [], Owner: current);

            List<PendingEnd> ends = this.pendingCancellations.Value.Ends;

            TransactionInternal.AfterReleaseImpl(() =>
                this.Flush(ends: ends, sends: false, trackedCell: trackedCell));
        }

        this.pendingCancellations.Value.Ends.Add(end);
    }

    /// <summary>
    ///     The one method that makes the effects of each item at its end. An item with
    ///     no start, which a cancellation removed with the Queued status, also comes
    ///     here. This method asks the strategy for one decision over the items that
    ///     end together. Then, in one atomic SodaFlow transaction, it publishes each
    ///     outcome that the strategy asks for, removes the entry of each item,
    ///     promotes each item that the strategy selects, and disposes the
    ///     CancellationTokenSource of each one.
    ///     <para>
    ///         <paramref name="sends" /> says which method answers. A cancellation can
    ///         end more than one item at one instant, and no canceled end sends
    ///         anything. Thus, EndItem gives all of those ends to one call, and
    ///         OnCanceled makes one decision for them. One edit of the queue then
    ///         answers one instant, and no observer of the queue sees a state between
    ///         the ends.
    ///     </para>
    ///     <para>
    ///         An end that sends a result or an error is alone in its call, and
    ///         OnCompleted reads it. One transaction carries one value of a stream,
    ///         thus one such end is the most that a transaction can hold. The edit of
    ///         the queue for that end is in this same transaction as its send. Where
    ///         it is not, a graph that reads the queue and the result together sees
    ///         two instants where the pipeline made one. A caller that wants results
    ///         for a batch of inputs asks for that in the types. One MapAsync goes
    ///         from TInput to a list of inputs, and a second goes from that list to a
    ///         list of results.
    ///     </para>
    ///     <para>
    ///         The removal of an entry and the disposal of its CancellationTokenSource
    ///         are in the same transaction, thus the Snapshot of a cancellation stream
    ///         never reads a stale entry. That Snapshot sees the entry from before
    ///         this transaction, and a cancellation can then remove it, or it does not
    ///         see the entry at all, from after this transaction.
    ///     </para>
    ///     <para>
    ///         EndItem gives this method each end, and never from a callback. Thus, a
    ///         long queue with a cancellation on each item does not make a depth of
    ///         calls.
    ///     </para>
    /// </summary>
    /// <param name="ends">
    ///     The ends to make the effects of. One of them can send only where
    ///     <paramref name="sends" /> is true.
    /// </param>
    /// <param name="sends">
    ///     True for the one end that can send a result or an error, which OnCompleted
    ///     reads. False for the canceled ends of one transaction, which OnCanceled
    ///     reads.
    /// </param>
    /// <param name="trackedCell">The cell that holds the queue of this pipeline.</param>
    /// <remarks>
    ///     <para>
    ///         A listener can throw in the transaction of this method. That listener
    ///         can be on the results, on the errors, or on the queue. The transaction
    ///         then fails, and the edit of the queue and each send go with it. The Task
    ///         of Execute and the CancellationTokenSource of each item change only when
    ///         the transaction commits. Thus, after a failure, each item stays live in
    ///         the queue.
    ///     </para>
    ///     <para>
    ///         This method then ends the items again, in a new transaction that sends
    ///         nothing. That end cancels the Task of each item, as for a result that
    ///         the strategy does not publish, and the next item can start. Then this
    ///         method sends the failure to the errors in a
    ///         <see cref="MapAsyncDeliveryExceptionBase" />. That send has a transaction of
    ///         its own. Thus, a listener on the errors that throws cannot stop the
    ///         pipeline. A throw from that second end or from that send goes to
    ///         handleException in an AggregateException after the first failure.
    ///         Thus, this method does not throw.
    ///     </para>
    ///     <para>
    ///         This method runs only where no transaction is open. Thus, the Apply here
    ///         opens a transaction, and a failure of that transaction comes back here.
    ///         See <see cref="EndItem" />.
    ///     </para>
    /// </remarks>
    private void Flush(IReadOnlyList<PendingEnd> ends, bool sends, Cell<Entry[]> trackedCell)
    {
        try
        {
            this.FlushOnce(ends: ends, sends: sends, trackedCell: trackedCell, report: null);
        }
        catch (Exception failure)
        {
            this.Recover(ends: ends, sends: sends, trackedCell: trackedCell, failure: failure);
        }
    }

    // The strategy reads the second end also. Its decision from the first end went with the first
    // transaction, and its state keeps each change of that call. See the remarks of
    // AsyncConcurrencyStrategy.OnCompleted.
    private void Recover(IReadOnlyList<PendingEnd> ends, bool sends, Cell<Entry[]> trackedCell, Exception failure)
    {
        // The error that the first transaction sent, where it sent one. A listener after the throw
        // did not get it. It stays only in the report and in the Task of Execute.
        MapAsyncDeliveryExceptionBase report =
            this.createDeliveryException(
                message: MapAsyncDeliveryExceptionBase.DefaultMessage,
                innerException: failure,
                undeliveredError: sends ? ends[0].SentError : MaybeInternal<Exception>.None);

        try
        {
            this.FlushOnce(ends: ends, sends: sends, trackedCell: trackedCell, report: report);
        }
        catch (Exception endFailure)
        {
            this.Report(new AggregateException(failure, endFailure));

            return;
        }

        try
        {
            TransactionInternal.Apply(
                code: static (_, _, state) =>
                {
                    state.Errors.SendImpl(state.Report);

                    return UnitInternal.Value;
                },
                state: (Errors: this.errors, Report: report));
        }
        catch (Exception reportFailure)
        {
            this.Report(new AggregateException(report, reportFailure));
        }
    }

    // With no report, this sends each outcome. With a report, this ends the items again and sends
    // nothing. The Task of each end that the first transaction published then fails with that
    // report.
    private void FlushOnce(
        IReadOnlyList<PendingEnd> ends,
        bool sends,
        Cell<Entry[]> trackedCell,
        MapAsyncDeliveryExceptionBase? report) =>
        TransactionInternal.Apply(
            code: static (transaction, _, state) =>
            {
                state.Self.FlushInTransaction(
                    transaction: transaction,
                    ends: state.Ends,
                    sends: state.Sends,
                    trackedCell: state.TrackedCell,
                    report: state.Report);

                return UnitInternal.Value;
            },
            state: (Self: this, Ends: ends, Sends: sends, TrackedCell: trackedCell, Report: report));

    // Settles the items that one flush ended, when its transaction commits. This answers the Task of
    // each Execute call first, and then disposes each CancellationTokenSource.
    private static void Settle(List<PendingEnd> live, Action?[]? settlements)
    {
        if (settlements != null)
        {
            foreach (Action? settlement in settlements)
            {
                settlement?.Invoke();
            }
        }

        foreach (PendingEnd end in live)
        {
            end.Item.Cancellation.Dispose();
        }
    }

    private void FlushInTransaction(
        TransactionInternal transaction,
        IReadOnlyList<PendingEnd> ends,
        bool sends,
        Cell<Entry[]> trackedCell,
        MapAsyncDeliveryExceptionBase? report)
    {
        Entry[] queue = this.CurrentQueue(trackedCell);

        // An item ends one time. A cancellation ends a Queued item, and a promotion of that
        // same item ends it also, thus two paths can come to one item. An end for an item that
        // the queue no longer holds is the second path, and it stops here. Without this test,
        // the strategy gets a second end for an item that ended, and starts an item for it.
        List<PendingEnd> live = [];

        // The ExecuteCompletion of each end in `live`, at the same index. An end carries
        // none, because EndItem gets an item and not an entry, thus this reads it off the entry
        // in the queue.
        List<ExecuteCompletion<TResult>?> completions = [];

        EntryIndex queueIndex = new(entries: queue, lookups: ends.Count);

        // ReSharper disable once ForCanBeConvertedToForeach - Done for performance reasons.
        for (int i = 0; i < ends.Count; i++)
        {
            int position = queueIndex.IndexOf(ends[i].Item.Id);

            if (position >= 0)
            {
                live.Add(ends[i]);
                completions.Add(queue[position].Completion);
            }
        }

        if (live.Count == 0)
        {
            return;
        }

        Guid[] removals = new Guid[live.Count];

        for (int i = 0; i < live.Count; i++)
        {
            removals[i] = live[i].Item.Id;
        }

        // The removals come first. Thus, the queue that the strategy reads, and the queue that
        // an Admit of this same transaction reads, hold no item that ends here. A loop from
        // the result stream to the input stream makes one transaction of the two calls. The
        // two must agree about what this pipeline holds.
        Entry[] tracked =
            Apply(
                list: queue,
                mutation: new Mutation(
                    remove: removals,
                    promote: [],
                    add: []));

        IReadOnlyList<AsyncToStart<TStrategyInput>> next;
        bool publish;

        if (sends)
        {
            // One end, because EndItem gives an end that can send a call of its own. The
            // strategy reads how the operation ended and not its result, thus this call comes
            // before the construction of a result. See AsyncCompletion for what that gives:
            // the pipeline makes a result only for an item that it publishes.
            AsyncStrategyResult<TStrategyInput> decision =
                this.stateManager.OnCompleted(
                    item: live[0].Item,
                    completion: live[0]
                        .Outcome.Match(
                            onSucceeded: static _ => AsyncCompletion.Succeeded(),
                            onFailed: AsyncCompletion.Failed,

                            // EndItem gives a canceled end to the other branch, thus this code is
                            // unreachable. AsyncCompletion has no Canceled value to give here.
                            onCanceled: static () =>
                                throw new InvalidOperationException("A canceled end cannot come to OnCompleted.")),
                    tracked: new TrackedItems(tracked));

            // A second end, which has a report, publishes nothing, whatever the strategy says.
            publish = report is null && decision.Publish;
            next = decision.Next;
        }
        else
        {
            AsyncQueuedItem<TStrategyInput>[] canceled =
                new AsyncQueuedItem<TStrategyInput>[live.Count];

            for (int i = 0; i < live.Count; i++)
            {
                canceled[i] = live[i].Item;
            }

            // A canceled item sends nothing, thus this method makes no publish decision. It
            // gives the items to start and no more.
            next =
                this.stateManager.OnCanceled(
                    canceled: canceled,
                    tracked: new TrackedItems(tracked));

            publish = false;
        }

        // The answer of each Execute call, at the same index as `live`. These run when this
        // transaction commits, thus a transaction that fails answers no Task.
        Action?[]? settlements = null;

        for (int i = 0; i < live.Count; i++)
        {
            PendingEnd end = live[i];
            ExecuteCompletion<TResult>? completion = completions[i];

            if (!publish)
            {
                // A cancellation says that no code wants this result, and a strategy that
                // does not publish says the same. Thus, Execute answers as a cancellation
                // answers. The remarks of AsyncCompletion give the same rule. An end that the
                // first transaction published is different: a listener stopped that outcome, and
                // the Task fails with the report that the errors get.
                if (completion != null)
                {
                    (settlements ??= new Action?[live.Count])[i] =
                        report != null && end.Published ? () => completion.TrySetException(report) : completion.TrySetCanceled;
                }

                continue;
            }

            end.Published = true;

            // One send at the most reaches this code, because EndItem gives an end that sends
            // a call of its own. Thus, the send is in the transaction that removed the item,
            // and a graph that reads the queue and the result sees one instant.
            Action? settlement =
                end.Outcome.Match(
                    onSucceeded: operationResult =>
                        this.Publish(
                            end: end,
                            operationResult: operationResult,
                            completion: completion),
                    onFailed: e =>
                    {
                        end.SentError = MaybeInternal.Some(e);
                        this.errors.SendImpl(e);

                        return completion is null ? null : () => completion.TrySetException(e);
                    },

                    // A canceled end never publishes: OnCanceled makes no publish decision, thus
                    // the test above sends each one to the release of its Task.
                    onCanceled: static () =>
                        throw new InvalidOperationException("A canceled end cannot publish."));

            if (settlement != null)
            {
                (settlements ??= new Action?[live.Count])[i] = settlement;
            }
        }

        Guid[] promote = new Guid[next.Count];
        TInput[] values = new TInput[next.Count];
        EntryIndex trackedIndex = new(entries: tracked, lookups: next.Count);

        for (int i = 0; i < next.Count; i++)
        {
            Guid id = next[i].Item.Id;
            int position = trackedIndex.IndexOf(id);

            if (position < 0)
            {
                throw new InvalidOperationException("Could not find item to start.");
            }

            values[i] = tracked[position].Value;
            promote[i] = id;
        }

        tracked =
            Apply(
                list: tracked,
                mutation: new Mutation(
                    remove: [],
                    promote: promote,
                    add: []));

        this.completedQueueAndOwner = (Queue: tracked, Owner: transaction);

        this.queueUpdates.SendImpl(tracked);

        // The removal of the entries and the disposal of their sources stay in one transaction.
        // A last action runs only at the commit. Before, the failure of a flush disposed the source of
        // an item in the queue. The cancellation at a disposal then threw.
        transaction.Last(() => Settle(live: live, settlements: settlements));

        for (int i = 0; i < next.Count; i++)
        {
            this.PromoteAndLaunch(
                toStart: next[i],
                value: values[i],
                trackedCell: trackedCell);
        }
    }

    /// <summary>
    ///     Makes the result of an item and sends it. This is the one code that calls
    ///     the function of a <see cref="MapAsyncResult{TResult}" />, and it runs in
    ///     the transaction that publishes. Thus, a result that holds a cell or a
    ///     stream comes into existence in that transaction, and that is the cause for
    ///     a constructor and not a value.
    ///     <para>
    ///         A throw from that function goes to the errors. The strategy saw this
    ///         item as Succeeded, because the operation did return, and this code then
    ///         publishes the error of the construction and not a result. A
    ///         cancellation that the function throws, with the token of this item
    ///         canceled, publishes nothing, as a cancellation always does.
    ///     </para>
    ///     <para>
    ///         The send operation is not in the try. A listener that throws is not a
    ///         failure of this operation, and an error stream that receives such a
    ///         throw hides its source.
    ///     </para>
    /// </summary>
    /// <returns>
    ///     The action that answers the Task of <paramref name="completion" />, or null
    ///     where there is no Task. The caller runs it when the transaction commits.
    /// </returns>
    private Action? Publish(
        PendingEnd end,
        MapAsyncResult<TResult> operationResult,
        ExecuteCompletion<TResult>? completion)
    {
        TResult result;

        try
        {
            result = operationResult.GetResult();
        }
        catch (OperationCanceledException) when (end.TokenToCheck is { IsCancellationRequested: true })
        {
            return completion is null ? null : completion.TrySetCanceled;
        }
        catch (Exception e)
        {
            end.SentError = MaybeInternal.Some(e);
            this.errors.SendImpl(e);

            return completion is null ? null : () => completion.TrySetException(e);
        }

        // The stream first, then the Task. The Task gives the same object that the results stream
        // gets, thus a caller of Execute and a listener of that stream see one result.
        this.results.SendImpl(result);

        return completion?.PrepareResult(result);
    }

    // ---- The records of the tracked items. They are private to the execution engine, and a
    // strategy cannot read them. ----

    // One item that ended, with what its operation gave and the token to test at the publish.
    private sealed class PendingEnd(
        in AsyncQueuedItem<TStrategyInput> item,
        in AsyncOutcome<MapAsyncResult<TResult>> outcome,
        in CancellationToken? tokenToCheck)
    {
        public AsyncQueuedItem<TStrategyInput> Item { get; } = item;

        public AsyncOutcome<MapAsyncResult<TResult>> Outcome { get; } = outcome;

        public CancellationToken? TokenToCheck { get; } = tokenToCheck;

        // The exception that the flush of this end sent to the errors, where it sent one. The
        // flush writes it in its transaction. Recover reads it after that transaction fails, on the
        // same thread, and puts it in the report.
        public MaybeInternal<Exception> SentError { get; set; }

        // True when a flush published the outcome of this end. The second end reads it, because the
        // strategy can give a different decision on its second call. SwitchLatest does that.
        public bool Published { get; set; }
    }

    // One value that this pipeline admits: the value, and the ExecuteCompletion of the Execute
    // call that gave it. Completion is null for a value from the source stream, which answers
    // through the results stream and the errors stream alone.
    //
    // A readonly struct, because the pipeline makes one of these for each value that it admits. The
    // type is private, thus no code can make a default instance of it. That is what made the
    // protected-internal records of AsyncMapBase into sealed classes, and it does not apply here.
    private readonly struct Admission(in TInput value, in ExecuteCompletion<TResult>? completion)
    {
        public TInput Value { get; } = value;

        public ExecuteCompletion<TResult>? Completion { get; } = completion;
    }

    private sealed class Entry(
        in TInput value,
        in AsyncQueuedItem<TInput> item,
        in AsyncTrackedItem<TStrategyInput> tracked,
        in ExecuteCompletion<TResult>? completion)
    {
        // The ExecuteCompletion of the Execute call that gave this value, and null for a value
        // from the source stream. Flush answers it at the end of this item.
        public ExecuteCompletion<TResult>? Completion { get; } = completion;

        public TInput Value { get; } = value;

        public AsyncQueuedItem<TInput> Item { get; } = item;

        // The same item in the types of the strategy, with its status. This is what the queue
        // that Admit and OnCompleted read is made of, and the status lives here alone.
        public AsyncTrackedItem<TStrategyInput> Tracked { get; } = tracked;

        public AsyncItemStatus Status => this.Tracked.Status;

        public Entry WithStatus(AsyncItemStatus status) =>
            new(
                value: this.Value,
                item: this.Item,
                tracked: this.Tracked.WithStatus(status),
                completion: this.Completion);
    }

    /// <summary>
    ///     The queue of the pipeline, as a strategy reads it. It holds the array that
    ///     the tracked cell has now and takes the item of each entry, thus a call of
    ///     Admit or OnCompleted copies no list. The array is immutable after the
    ///     transaction that made it, which is what makes this safe to give away.
    /// </summary>
    // ReSharper disable once InheritdocConsiderUsage
    private sealed class TrackedItems(in Entry[] entries) : IReadOnlyList<AsyncTrackedItem<TStrategyInput>>
    {
        private readonly Entry[] entries = entries;

        public int Count => this.entries.Length;

        public AsyncTrackedItem<TStrategyInput> this[int index] => this.entries[index].Tracked;

        public IEnumerator<AsyncTrackedItem<TStrategyInput>> GetEnumerator() =>
            this.entries.Select(static entry => entry.Tracked).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    private sealed class Mutation(in Guid[] remove, in Guid[] promote, in Entry[] add)
    {
        public Guid[] Remove { get; } = remove;

        public Guid[] Promote { get; } = promote;

        public Entry[] Add { get; } = add;
    }

    // Finds the position of an entry by its id. A search of the array for each id costs the product
    // of the number of ids and the length of the queue. The cancellation of a full queue searches for
    // each item, and that cost was quadratic. Thus, for more than a few ids this makes a dictionary
    // of the queue one time, and each search after it costs a constant. For a few ids, the search
    // of the array costs less than the dictionary, and it allocates nothing.
    private readonly struct EntryIndex
    {
        private readonly Entry[] entries;
        private readonly Dictionary<Guid, int>? positions;

        public EntryIndex(Entry[] entries, int lookups)
        {
            this.entries = entries;

            if (lookups <= LinearSearchLimit || entries.Length <= LinearSearchLimit)
            {
                return;
            }

            this.positions = new Dictionary<Guid, int>(entries.Length);

            for (int i = 0; i < entries.Length; i++)
            {
                this.positions[entries[i].Item.Id] = i;
            }
        }

        // The position of the entry with this id, or -1.
        public int IndexOf(Guid id)
        {
            if (this.positions != null)
            {
                // ReSharper disable once CanSimplifyDictionaryTryGetValueWithGetValueOrDefault - GetValueOrDefault
                // does not exist on net472 and netstandard2.0, and this compiles for them as well.
                return this.positions.TryGetValue(key: id, value: out int position) ? position : -1;
            }

            for (int i = 0; i < this.entries.Length; i++)
            {
                if (this.entries[i].Item.Id == id)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    // Tells if an id is one of a set of ids. For more than a few ids it uses a HashSet, as EntryIndex
    // uses a dictionary.
    private readonly struct IdSet(in Guid[] ids)
    {
        private readonly Guid[] ids = ids;
        private readonly HashSet<Guid>? set = ids.Length > LinearSearchLimit ? [.. ids] : null;

        public bool Contains(Guid id) =>
            this.set?.Contains(id) ?? Array.IndexOf(array: this.ids, value: id) >= 0;
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
Subclass AsyncConcurrencyStrategy<TInput, TState> and implement
CreateState/Admit/OnCompleted/OnCanceled as pure reporting — they have no access to the
result/error sinks and don't start Tasks themselves; they just describe what should happen and let
the execution engine carry it out.
CreateState is called once per MapAsync call, so TState is where all of a strategy's mutable
bookkeeping lives instead of on the strategy instance itself — that's what makes a single
strategy instance safe to reuse across multiple MapAsync calls without their scheduling state
bleeding into each other. The one thing you can do imperatively is item.Cancel(), to cancel an
item you're managing (queued or running); it still ends normally, through OnCanceled, so you can
chain from there. OnCompleted is for the two ends that can publish, a return or a throw, and it
reads one item; OnCanceled reads the items of one cancellation together, and returns only what to
start, because a canceled item publishes nothing. Every value you don't immediately start stays
tracked as Queued automatically, and each of these methods is handed the whole queue —
every item the pipeline tracks, Queued or Running, in admission order — so a strategy that
schedules purely on that order doesn't have to keep a queue of its own in TState at all. Keep an
AsyncQueuedItem (not just its Value) in TState when you want to recognize a particular item later
rather than scan for it: AsyncQueuedItem is a sealed class you can't construct, and you're always
handed back the very instance you were given, so ReferenceEquals — or comparing Id, as the
built-in SwitchLatest strategy does — is enough for "is this still current?" checks, with no
separate handle needed. Two details about that list: in Admit it does not contain the value being
admitted, and in OnCompleted and OnCanceled it does not contain the items that just ended either,
because the pipeline removes them first — so a strategy picking "the next one" needs no test
against them. A strategy that
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
the operation returned or threw, and the pipeline makes the result afterward and only for an item
it publishes — see ResultFactory.Construct. That is why there
is no TStrategyResult and no resultConverter: they existed to give the strategy a value, and the
strategy no longer reads one.
*/
