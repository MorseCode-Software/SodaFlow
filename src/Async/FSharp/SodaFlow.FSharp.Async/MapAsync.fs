/// <summary>
///     Connects an impure asynchronous operation to the FRP graph. These functions listen on a
///     <c>Stream&lt;'TInput&gt;</c>, run an async operation for each send, put the result into a
///     <c>StreamSink&lt;'TResult&gt;</c>, and give the Queued items and the Running items. They can
///     also connect to streams that cancel Queued work and Running work. The
///     <c>AsyncMapStatus&lt;'TInput, 'TResult&gt;</c> that each function here returns is
///     IDisposable, and a disposal of it stops the full pipeline.
/// </summary>
/// <remarks>
///     This module is the F# equivalent of AsyncStreamExtensions and AsyncConcurrencyStrategy in
///     the C# wrapper SodaFlow.Async. It uses the F# <c>unit</c> and not
///     <c>SodaFlow.Functional.Unit</c> for a value that a strategy does not use. F# has no
///     overload and no optional parameter on a let-bound function. Thus, the nine MapAsync
///     overloads in C# are four functions with different names here, and each cancellation
///     argument is explicit and has no default.
/// </remarks>
module SodaFlow.Async

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Threading
open System.Threading.Tasks
open SodaFlow
open SodaFlow.Async

// The three short classes below are the equivalent of AsyncConcurrencyStrategy,
// AsyncConcurrencyStrategy<TState>, and AsyncConcurrencyStrategy<TInput,TState> in the C# wrapper.
// They have no relation to the strategies of the library below, which call the shared generic
// AsyncConcurrencyStrategyFactory in Core directly and do not use these classes.

/// <summary>
///     The state type of a custom strategy that has no state of its own.
/// </summary>
/// <remarks>
///     This type is here because F# cannot override an abstract member whose signature becomes a
///     bare <c>unit -&gt; unit</c> segment. Such a signature is ambiguous between a member with no
///     parameter and a member with a unit parameter, and <c>CreateState</c> has that signature
///     when a strategy uses <c>unit</c> as its own state type. A type with its own name prevents
///     that. For that cause the <see cref="T:SodaFlow.Async.AsyncConcurrencyStrategy" /> below,
///     which is not generic, uses this type and not <c>unit</c>.
/// </remarks>
[<Struct>]
type EmptyState = EmptyState

/// <summary>
///     A short base class for a custom strategy that reads no input, because the input type is
///     <c>unit</c>, and that keeps a scheduling state of its own.
/// </summary>
/// <typeparam name="TState">
///     The scheduling state of this strategy for each call. It is opaque to a caller, and it is
///     never part of a mapAsync signature.
/// </typeparam>
[<AbstractClass>]
type AsyncConcurrencyStrategy<'TState>() =
    inherit AsyncConcurrencyStrategy<unit, 'TState>()

/// <summary>
///     A short base class for a custom strategy that reads no input, and that keeps no state of
///     its own. See <see cref="T:SodaFlow.Async.EmptyState" /> for the cause of a
///     named state type and not <c>unit</c>.
/// </summary>
[<AbstractClass>]
type AsyncConcurrencyStrategy() =
    inherit AsyncConcurrencyStrategy<unit, EmptyState>()

let private parallelInstance = AsyncConcurrencyStrategyFactory.Parallel()
let private queueInstance = AsyncConcurrencyStrategyFactory.Queue<unit>()
let private switchLatestInstance = AsyncConcurrencyStrategyFactory.SwitchLatest()

/// <summary>
///     Each send starts its own operation immediately. The results come in the sequence of their
///     ends.
/// </summary>
/// <returns>
///     A strategy for <c>mapAsync</c>. It holds no state of its own, thus each number of
///     <c>mapAsync</c> calls can use the same instance safely, at the same time. Each call gets
///     its own scheduling state.
/// </returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let parallelStrategy () : AsyncConcurrencyStrategyBase<unit> = parallelInstance

/// <summary>
///     One operation or no operation runs at a time. A subsequent send goes to the queue, and the
///     queue runs in sequence.
/// </summary>
/// <returns>
///     A strategy for <c>mapAsync</c>. More than one call can use it, on the conditions of
///     <c>parallelStrategy</c>.
/// </returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let queueStrategy () : AsyncConcurrencyStrategyBase<unit> = queueInstance

/// <summary>
///     A new send cancels the operation that runs and replaces it. The pipeline never publishes
///     the result of the run that it replaces, at each state of the cancellation token in that
///     operation.
/// </summary>
/// <returns>
///     A strategy for <c>mapAsync</c>. More than one call can use it, on the conditions of
///     <c>parallelStrategy</c>.
/// </returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let switchLatestStrategy () : AsyncConcurrencyStrategyBase<unit> = switchLatestInstance

/// <summary>
///     One queue for each group, and each queue operates independently. In one group, a
///     subsequent send goes behind the sends before it, as <c>queueStrategy</c> does. Two different
///     groups do not wait for each other. This is <c>queuePerGroupStrategy</c> with an explicit
///     comparer for the group keys.
/// </summary>
/// <param name="groupComparer">The equality comparer for the group keys.</param>
/// <param name="getGroup">
///     Calculates the group key of an input value. It must be deterministic, because the strategy
///     calls it at the admission of the value and again at its end. The two calls must agree, or
///     the strategy cannot find the queue of the item.
/// </param>
/// <returns>
///     A strategy for <c>mapAsyncWithInputConverter</c>. It reads the input, thus a caller cannot
///     give it to <c>mapAsync</c>. More than one call can use it, on the conditions of
///     <c>parallelStrategy</c>.
/// </returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let queuePerGroupStrategyWithComparer (groupComparer: IEqualityComparer<'TGroup>) (getGroup: 'TInput -> 'TGroup) =
    AsyncConcurrencyStrategyFactory.QueuePerGroup<unit, _, _>(getGroup, groupComparer)

/// <summary>
///     One queue for each group, with the default equality comparer for <c>'TGroup</c> as the key
///     comparer. See <c>queuePerGroupStrategyWithComparer</c> to give your own comparer.
/// </summary>
/// <param name="getGroup">
///     Calculates the group key of an input value. It must be deterministic. See
///     <c>queuePerGroupStrategyWithComparer</c>.
/// </param>
/// <returns>
///     A strategy for <c>mapAsyncWithInputConverter</c>. It reads the input, thus a caller cannot
///     give it to <c>mapAsync</c>. More than one call can use it, on the conditions of
///     <c>parallelStrategy</c>.
/// </returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let queuePerGroupStrategy (getGroup: 'TInput -> 'TGroup) =
    getGroup |> queuePerGroupStrategyWithComparer EqualityComparer<'TGroup>.Default

// The MapAsyncImpl in Core takes its cancelAll as a Stream<UnitInternal>, because Core has no
// public type for a value that a caller does not use. This code maps and does not cast, and that
// keeps UnitInternal out of each signature in this module. UnitInternal is internal to
// SodaFlow.Core, and code that consumes this library cannot name it. This code gives `null` for
// None, because the parameter in Core is a usual nullable reference.
[<MethodImpl(MethodImplOptions.NoInlining)>]
let private toUnitInternalStream (cancelAll: Stream<unit> option) : Stream<UnitInternal> =
    match cancelAll with
    | Some s -> s.MapImpl(Func<_, _>(fun (_: unit) -> UnitInternal.Value))
    | None -> null

// The four mapAsync functions below are different only in the path from the 'TInput and the
// 'TResult of the call to the types of `strategy`. The overloads in the C# wrapper are different
// along the same axis. These are functions with different names, because F# has no optional
// parameter and no overload on a let binding.
//
//   mapAsync                     strategy reads no type      (parallelStrategy, queueStrategy,
//                                                             switchLatestStrategy)
//   mapAsyncWithInputConverter   strategy reads the input    (queuePerGroupStrategy)
//   mapAsyncWithResultConverter  strategy reads the result
//   mapAsyncWithConverters       strategy reads the two types
//
// There is no function for a 'TInput that is a subtype of the input type of the strategy. F#
// cannot give that constraint between two open type parameters, and `fun v -> v` as the converter
// gives the same result. `source` is last in each function, thus these functions compose with |>.
// cancelAll, cancelMatching, and cancelOnDispose are necessary arguments here and are optional in
// C#. Give None, None, and true for the usual condition.

/// <summary>
///     Runs <paramref name="operation" /> for each send of <paramref name="source" />. It sends a
///     value that succeeded to <paramref name="results" /> and a value with an error to
///     <paramref name="errors" />, and <paramref name="strategy" /> selects the time of each run.
///     This function is for a strategy that reads no input and no result:
///     <c>parallelStrategy</c>, <c>queueStrategy</c>, and <c>switchLatestStrategy</c>.
/// </summary>
/// <param name="results">
///     The pipeline sends the return value of each operation that succeeded here, in the sequence
///     of their ends and not in the sequence of the inputs. The pipeline does not send a result
///     from a run that a different run replaced, or from a run that a cancellation stopped.
/// </param>
/// <param name="errors">
///     The pipeline sends each operation with an error here. There is no call of this function
///     with no destination for the errors.
/// </param>
/// <param name="operation">
///     The asynchronous work for each input. The pipeline calls it inline, thus it does not go to
///     a thread pool before its own await. It receives a CancellationToken that combines the
///     cancellation of this item with each token from the strategy. An operation that obeys that
///     token lets <paramref name="cancelAll" />, <paramref name="cancelMatching" />, and
///     <paramref name="cancelOnDispose" /> stop work that started. An operation that ignores the
///     token continues to its end, and the cancellation then only stops the publication of its
///     result.
/// </param>
/// <param name="strategy">
///     The control of operations that overlap. It holds no state of its own, and more than one
///     call can use it safely, at the same time.
/// </param>
/// <param name="cancelAll">
///     <c>Some</c> stream where each send cancels each tracked operation, Queued or Running, or
///     <c>None</c>. The pipeline does not start a canceled Queued item at its turn. A Running
///     operation stops only if it monitors its CancellationToken.
/// </param>
/// <param name="cancelMatching">
///     <c>Some</c> stream where each send cancels the tracked operations, Queued or Running, whose
///     input value is in the collection of that send, or <c>None</c>. This uses the default
///     equality comparer for <c>'TInput</c>. The limits of <paramref name="cancelAll" /> also
///     apply here.
/// </param>
/// <param name="cancelOnDispose">
///     True when a disposal of the status also cancels each item that the pipeline tracks at that
///     time, Queued or Running. At each value, a disposal always stops the admission of more
///     values. A disposal does not stop the output. Each operation that runs continues to its end
///     and then publishes to <paramref name="results" /> or to <paramref name="errors" />.
/// </param>
/// <param name="source">
///     The stream of inputs. The pipeline gives each send to <paramref name="strategy" />, and the
///     strategy starts it immediately or makes it wait. After a disposal of the status, the
///     pipeline ignores each subsequent send.
/// </param>
/// <returns>
///     An <c>AsyncMapStatus&lt;'TInput, 'TResult&gt;</c>. <c>IsRunning</c> is a
///     <c>Cell&lt;bool&gt;</c> that is true while one call or more has the Running status, and a
///     Queued item does not make it true. It updates with no glitch, in the transaction of the
///     event that changes it. <c>Items</c> gives each tracked value with its status.
///     <c>Execute</c> puts one value in, for an operation that drives a second pipeline. A
///     disposal of it stops the pipeline.
/// </returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let mapAsync
    (results: StreamSink<'TResult>)
    (errors: StreamSink<exn>)
    (operation: 'TInput -> ResultFactory<'TResult> -> CancellationToken -> Task<MapAsyncResult<'TResult>>)
    (strategy: AsyncConcurrencyStrategyBase<unit>)
    (cancelAll: Stream<unit> option)
    (cancelMatching: Stream<IReadOnlyCollection<'TInput>> option)
    (cancelOnDispose: bool)
    (source: Stream<'TInput>)
    : AsyncMapStatus<'TInput, 'TResult> =
    AsyncStreamUtility.MapAsyncImpl<'TInput, 'TResult, unit>(
        source,
        results,
        errors,
        MapAsyncOperation<_, _> operation,
        strategy,
        Func<_, _>(fun (_: 'TInput) -> ()),
        (cancelAll |> toUnitInternalStream),
        (cancelMatching |> Option.toObj),
        cancelOnDispose
    )

/// <summary>
///     This is <c>mapAsync</c> for a strategy that reads the input, such as
///     <c>queuePerGroupStrategy</c>. <paramref name="inputConverter" /> makes the value that the
///     strategy reads. Give <c>fun v -&gt; v</c> where <c>'TInput</c> is that type. See
///     <c>mapAsync</c> for the full contract of the parameters that the two functions share.
/// </summary>
/// <param name="results">The destination of the return value of each operation that succeeded.</param>
/// <param name="errors">The destination of each operation with an error.</param>
/// <param name="operation">The asynchronous work for each input.</param>
/// <param name="strategy">The control of operations that overlap.</param>
/// <param name="inputConverter">
///     Changes each <c>'TInput</c> to the <c>'TStrategyInput</c> of the strategy, before the
///     admission.
/// </param>
/// <param name="cancelAll"><c>Some</c> stream that cancels each tracked operation, or <c>None</c>.</param>
/// <param name="cancelMatching">
///     <c>Some</c> stream that cancels the tracked operations with a given input value, or
///     <c>None</c>.
/// </param>
/// <param name="cancelOnDispose">
///     True when a disposal of the status also cancels each item that the pipeline tracks at that
///     time.
/// </param>
/// <param name="source">The stream of inputs.</param>
/// <returns>
///     An <c>AsyncMapStatus&lt;'TInput, 'TResult&gt;</c> that gives the Queued items, the Running
///     items, and <c>Execute</c>. A disposal of it stops the pipeline.
/// </returns>
[<MethodImpl(MethodImplOptions.NoInlining)>]
let mapAsyncWithInputConverter
    (results: StreamSink<'TResult>)
    (errors: StreamSink<exn>)
    (operation: 'TInput -> ResultFactory<'TResult> -> CancellationToken -> Task<MapAsyncResult<'TResult>>)
    (strategy: AsyncConcurrencyStrategyBase<'TStrategyInput>)
    (inputConverter: 'TInput -> 'TStrategyInput)
    (cancelAll: Stream<unit> option)
    (cancelMatching: Stream<IReadOnlyCollection<'TInput>> option)
    (cancelOnDispose: bool)
    (source: Stream<'TInput>)
    : AsyncMapStatus<'TInput, 'TResult> =
    AsyncStreamUtility.MapAsyncImpl<'TInput, 'TResult, 'TStrategyInput>(
        source,
        results,
        errors,
        MapAsyncOperation<_, _> operation,
        strategy,
        Func<_, _> inputConverter,
        (cancelAll |> toUnitInternalStream),
        (cancelMatching |> Option.toObj),
        cancelOnDispose
    )
