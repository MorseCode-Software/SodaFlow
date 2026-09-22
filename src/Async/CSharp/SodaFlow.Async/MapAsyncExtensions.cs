using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using SodaFlow.Functional;

namespace SodaFlow.Async;

/// <summary>
///     Extension methods that connect an impure asynchronous operation to the FRP graph. They
///     listen on a Stream&lt;TInput&gt;, run an async operation for each send, put the result into
///     a StreamSink&lt;TResult&gt;, and give the Queued items and the Running items. They can also
///     connect to streams that cancel Queued work and Running work. The
///     <see cref="AsyncMapStatus{TInput}" /> that they return is IDisposable, and a disposal of it
///     stops the full pipeline.
///     <para>
///         The overloads are different only in the path from the <c>TInput</c> and the
///         <c>TResult</c> of the call to the types of the <c>strategy</c> argument. Select the
///         overload for your strategy. A strategy that only schedules, such as Parallel, Queue,
///         or SwitchLatest, uses no type of the two, thus it needs no converter. A strategy that
///         reads the input, such as QueuePerGroup, needs a <c>TInput</c> that is its input type
///         or that a converter can change into its input type. Each overload sends the call to
///         the last overload, which takes the two converters explicitly and needs no relation
///         between the types.
///     </para>
/// </summary>
[PublicAPI]
public static class AsyncStreamExtensions
{
    /// <summary>
    ///     For a strategy that uses only the schedule, and not
    ///     <typeparamref name="TInput" /> or <typeparamref name="TResult" />. Those strategies are
    ///     Parallel, Queue, and SwitchLatest. This overload changes the two types to
    ///     <see cref="Unit" /> before the strategy, thus it needs no converter. See the
    ///     canonical
    ///     <see
    ///         cref="MapAsync{TInput,TResult,TStrategyInput,TStrategyResult}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Func{TInput,CancellationToken,Task{TResult}},AsyncConcurrencyStrategyBase{TStrategyInput,TStrategyResult},Func{TInput,TStrategyInput},Func{TResult,TStrategyResult},Stream{Unit},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     overload for the full parameter contract.
    /// </summary>
    /// <typeparam name="TInput">The type in the source stream.</typeparam>
    /// <typeparam name="TResult">The type that <paramref name="operation" /> gives when it succeeds.</typeparam>
    /// <param name="source">The stream of inputs.</param>
    /// <param name="results">
    ///     The destination of the value of each run that succeeded, in the sequence of their ends.
    /// </param>
    /// <param name="errors">The destination of the exception of each run with an error.</param>
    /// <param name="operation">The asynchronous work for each input.</param>
    /// <param name="strategy">The control of operations that overlap.</param>
    /// <param name="cancelAll">This is optional. Each send cancels each tracked operation.</param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations with a given input value.
    /// </param>
    /// <param name="cancelOnDispose">True when a disposal also cancels the tracked items. The default is true.</param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" /> that gives the Queued items and the Running
    ///     items. A disposal of it stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />, <paramref name="errors" />,
    ///     <paramref name="operation" />, or <paramref name="strategy" /> is null.
    /// </exception>
    public static AsyncMapStatus<TInput> MapAsync<TInput, TResult>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        AsyncConcurrencyStrategyBase<Unit, Unit> strategy,
        Stream<Unit>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true) =>
        source.MapAsyncImpl(
            results: results,
            errors: errors,
            operation: operation,
            strategy: strategy,
            inputConverter: static _ => Unit.Value,
            resultConverter: static _ => Unit.Value,
            cancelAll: ToUnitInternalStream(cancelAll),
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose);

    /// <summary>
    ///     For a strategy that reads the input and publishes no result, because this overload
    ///     changes the result type to <see cref="Unit" />. Here
    ///     <typeparamref name="TInput" /> is the <typeparamref name="TStrategyInput" /> of the
    ///     strategy, thus this overload needs no converter. See the canonical
    ///     <see
    ///         cref="MapAsync{TInput,TResult,TStrategyInput,TStrategyResult}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Func{TInput,CancellationToken,Task{TResult}},AsyncConcurrencyStrategyBase{TStrategyInput,TStrategyResult},Func{TInput,TStrategyInput},Func{TResult,TStrategyResult},Stream{Unit},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     overload for the full parameter contract.
    /// </summary>
    /// <typeparam name="TInput">The type in the source stream.</typeparam>
    /// <typeparam name="TResult">The type that <paramref name="operation" /> gives when it succeeds.</typeparam>
    /// <typeparam name="TStrategyInput">
    ///     The input type of <paramref name="strategy" />. The compiler infers it from the type
    ///     of <paramref name="strategy" />, and no caller gives it explicitly. It is usually
    ///     <typeparamref name="TInput" />. The <c>where TInput : TStrategyInput</c> constraint
    ///     also lets more than one MapAsync call share a strategy that uses a base class or an
    ///     interface, with its own <typeparamref name="TInput" /> in each call. The
    ///     strategy then uses only <typeparamref name="TStrategyInput" />.
    /// </typeparam>
    /// <param name="source">The stream of inputs.</param>
    /// <param name="results">
    ///     The destination of the value of each run that succeeded, in the sequence of their ends.
    /// </param>
    /// <param name="errors">The destination of the exception of each run with an error.</param>
    /// <param name="operation">The asynchronous work for each input.</param>
    /// <param name="strategy">The control of operations that overlap.</param>
    /// <param name="cancelAll">This is optional. Each send cancels each tracked operation.</param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations with a given input value.
    /// </param>
    /// <param name="cancelOnDispose">True when a disposal also cancels the tracked items. The default is true.</param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" /> that gives the Queued items and the Running
    ///     items. A disposal of it stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />, <paramref name="errors" />,
    ///     <paramref name="operation" />, or <paramref name="strategy" /> is null.
    /// </exception>
    public static AsyncMapStatus<TInput> MapAsync<TInput, TResult, TStrategyInput>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        AsyncConcurrencyStrategyBase<TStrategyInput, Unit> strategy,
        Stream<Unit>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true)
        where TInput : TStrategyInput =>
        source.MapAsyncImpl(
            results: results,
            errors: errors,
            operation: operation,
            strategy: strategy,
            inputConverter: static v => v,
            resultConverter: static _ => Unit.Value,
            cancelAll: ToUnitInternalStream(cancelAll),
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose);

    /// <summary>
    ///     For a strategy that reads the input and publishes no result, because this overload
    ///     changes the result type to <see cref="Unit" />. Here
    ///     <paramref name="inputConverter" /> makes the value that the strategy reads. For
    ///     example, QueuePerGroup makes its group key. See the canonical
    ///     <see
    ///         cref="MapAsync{TInput,TResult,TStrategyInput,TStrategyResult}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Func{TInput,CancellationToken,Task{TResult}},AsyncConcurrencyStrategyBase{TStrategyInput,TStrategyResult},Func{TInput,TStrategyInput},Func{TResult,TStrategyResult},Stream{Unit},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     overload for the full parameter contract.
    /// </summary>
    /// <typeparam name="TInput">The type in the source stream.</typeparam>
    /// <typeparam name="TResult">The type that <paramref name="operation" /> gives when it succeeds.</typeparam>
    /// <typeparam name="TStrategyInput">
    ///     The input type of <paramref name="strategy" />. The compiler infers it from the type
    ///     of <paramref name="strategy" />, and no caller gives it explicitly. It needs no
    ///     inheritance relation to <typeparamref name="TInput" />, because
    ///     <paramref name="inputConverter" /> makes it explicitly.
    /// </typeparam>
    /// <param name="source">The stream of inputs.</param>
    /// <param name="results">
    ///     The destination of the value of each run that succeeded, in the sequence of their ends.
    /// </param>
    /// <param name="errors">The destination of the exception of each run with an error.</param>
    /// <param name="operation">The asynchronous work for each input.</param>
    /// <param name="strategy">The control of operations that overlap.</param>
    /// <param name="inputConverter">
    ///     Changes each <typeparamref name="TInput" /> value to the
    ///     <typeparamref name="TStrategyInput" /> of <paramref name="strategy" />, before the
    ///     admission.
    /// </param>
    /// <param name="cancelAll">This is optional. Each send cancels each tracked operation.</param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations with a given input value.
    /// </param>
    /// <param name="cancelOnDispose">True when a disposal also cancels the tracked items. The default is true.</param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" /> that gives the Queued items and the Running
    ///     items. A disposal of it stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />, <paramref name="errors" />,
    ///     <paramref name="operation" />, or <paramref name="strategy" /> is null.
    /// </exception>
    public static AsyncMapStatus<TInput> MapAsync<TInput, TResult, TStrategyInput>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        AsyncConcurrencyStrategyBase<TStrategyInput, Unit> strategy,
        Func<TInput, TStrategyInput> inputConverter,
        Stream<Unit>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true) =>
        source.MapAsyncImpl(
            results: results,
            errors: errors,
            operation: operation,
            strategy: strategy,
            inputConverter: inputConverter,
            resultConverter: static _ => Unit.Value,
            cancelAll: ToUnitInternalStream(cancelAll),
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose);

    /// <summary>
    ///     For a strategy that reads the result and not the input, because this overload changes
    ///     the input type to <see cref="Unit" />. Here <typeparamref name="TResult" /> is the
    ///     <typeparamref name="TStrategyResult" /> of the strategy, thus this overload needs no
    ///     converter. See the canonical
    ///     <see
    ///         cref="MapAsync{TInput,TResult,TStrategyInput,TStrategyResult}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Func{TInput,CancellationToken,Task{TResult}},AsyncConcurrencyStrategyBase{TStrategyInput,TStrategyResult},Func{TInput,TStrategyInput},Func{TResult,TStrategyResult},Stream{Unit},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     overload for the full parameter contract.
    /// </summary>
    /// <typeparam name="TInput">The type in the source stream.</typeparam>
    /// <typeparam name="TResult">The type that <paramref name="operation" /> gives when it succeeds.</typeparam>
    /// <typeparam name="TStrategyResult">
    ///     The result type of <paramref name="strategy" />. The compiler infers it from the type
    ///     of <paramref name="strategy" />, and no caller gives it explicitly. It is usually
    ///     <typeparamref name="TResult" />. The <c>where TResult : TStrategyResult</c> constraint
    ///     also lets more than one MapAsync call share a strategy that uses a base class or an
    ///     interface, with its own <typeparamref name="TResult" /> in each call.
    /// </typeparam>
    /// <param name="source">The stream of inputs.</param>
    /// <param name="results">
    ///     The destination of the value of each run that succeeded, in the sequence of their ends.
    /// </param>
    /// <param name="errors">The destination of the exception of each run with an error.</param>
    /// <param name="operation">The asynchronous work for each input.</param>
    /// <param name="strategy">The control of operations that overlap.</param>
    /// <param name="cancelAll">This is optional. Each send cancels each tracked operation.</param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations with a given input value.
    /// </param>
    /// <param name="cancelOnDispose">True when a disposal also cancels the tracked items. The default is true.</param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" /> that gives the Queued items and the Running
    ///     items. A disposal of it stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />, <paramref name="errors" />,
    ///     <paramref name="operation" />, or <paramref name="strategy" /> is null.
    /// </exception>
    public static AsyncMapStatus<TInput> MapAsync<TInput, TResult, TStrategyResult>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        AsyncConcurrencyStrategyBase<Unit, TStrategyResult> strategy,
        Stream<Unit>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true)
        where TResult : TStrategyResult =>
        source.MapAsyncImpl(
            results: results,
            errors: errors,
            operation: operation,
            strategy: strategy,
            inputConverter: static _ => Unit.Value,
            resultConverter: static v => v,
            cancelAll: ToUnitInternalStream(cancelAll),
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose);

    /// <summary>
    ///     For a strategy that reads the result and not the input, because this overload changes
    ///     the input type to <see cref="Unit" />. Here <paramref name="resultConverter" /> makes
    ///     the value that the strategy reads. See the canonical
    ///     <see
    ///         cref="MapAsync{TInput,TResult,TStrategyInput,TStrategyResult}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Func{TInput,CancellationToken,Task{TResult}},AsyncConcurrencyStrategyBase{TStrategyInput,TStrategyResult},Func{TInput,TStrategyInput},Func{TResult,TStrategyResult},Stream{Unit},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     overload for the full parameter contract.
    /// </summary>
    /// <typeparam name="TInput">The type in the source stream.</typeparam>
    /// <typeparam name="TResult">The type that <paramref name="operation" /> gives when it succeeds.</typeparam>
    /// <typeparam name="TStrategyResult">
    ///     The result type of <paramref name="strategy" />. The compiler infers it from the type
    ///     of <paramref name="strategy" />, and no caller gives it explicitly. It needs no
    ///     inheritance relation to <typeparamref name="TResult" />, because
    ///     <paramref name="resultConverter" /> makes it explicitly.
    /// </typeparam>
    /// <param name="source">The stream of inputs.</param>
    /// <param name="results">
    ///     The destination of the value of each run that succeeded, in the sequence of their ends.
    /// </param>
    /// <param name="errors">The destination of the exception of each run with an error.</param>
    /// <param name="operation">The asynchronous work for each input.</param>
    /// <param name="strategy">The control of operations that overlap.</param>
    /// <param name="resultConverter">
    ///     Changes each <typeparamref name="TResult" /> that succeeded to the
    ///     <typeparamref name="TStrategyResult" /> of <paramref name="strategy" />, before
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TResult,TState}.OnCompleted" /> reads it.
    ///     A run with an error and a canceled run have no result, thus this code does not call
    ///     it for them.
    /// </param>
    /// <param name="cancelAll">This is optional. Each send cancels each tracked operation.</param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations with a given input value.
    /// </param>
    /// <param name="cancelOnDispose">True when a disposal also cancels the tracked items. The default is true.</param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" /> that gives the Queued items and the Running
    ///     items. A disposal of it stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />, <paramref name="errors" />,
    ///     <paramref name="operation" />, or <paramref name="strategy" /> is null.
    /// </exception>
    public static AsyncMapStatus<TInput> MapAsync<TInput, TResult, TStrategyResult>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        AsyncConcurrencyStrategyBase<Unit, TStrategyResult> strategy,
        Func<TResult, TStrategyResult> resultConverter,
        Stream<Unit>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true) =>
        source.MapAsyncImpl(
            results: results,
            errors: errors,
            operation: operation,
            strategy: strategy,
            inputConverter: static _ => Unit.Value,
            resultConverter: resultConverter,
            cancelAll: ToUnitInternalStream(cancelAll),
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose);

    /// <summary>
    ///     For a strategy that reads the input and the result. Here
    ///     <typeparamref name="TInput" /> and <typeparamref name="TResult" /> are the types of the
    ///     strategy, thus this overload needs no converter. See the canonical
    ///     <see
    ///         cref="MapAsync{TInput,TResult,TStrategyInput,TStrategyResult}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Func{TInput,CancellationToken,Task{TResult}},AsyncConcurrencyStrategyBase{TStrategyInput,TStrategyResult},Func{TInput,TStrategyInput},Func{TResult,TStrategyResult},Stream{Unit},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     overload for the full parameter contract.
    /// </summary>
    /// <typeparam name="TInput">The type in the source stream.</typeparam>
    /// <typeparam name="TResult">The type that <paramref name="operation" /> gives when it succeeds.</typeparam>
    /// <typeparam name="TStrategyInput">
    ///     The input type of <paramref name="strategy" />. The compiler infers it from the type
    ///     of <paramref name="strategy" />, and no caller gives it explicitly. It is usually
    ///     <typeparamref name="TInput" />. The <c>where TInput : TStrategyInput</c> constraint
    ///     also lets more than one MapAsync call share a strategy that uses a base class or an
    ///     interface, with its own <typeparamref name="TInput" /> in each call.
    /// </typeparam>
    /// <typeparam name="TStrategyResult">
    ///     The result type of <paramref name="strategy" />, as
    ///     <typeparamref name="TStrategyInput" /> is for the input. The compiler infers it, it is
    ///     usually <typeparamref name="TResult" />, and the
    ///     <c>where TResult : TStrategyResult</c> constraint connects the two.
    /// </typeparam>
    /// <param name="source">The stream of inputs.</param>
    /// <param name="results">
    ///     The destination of the value of each run that succeeded, in the sequence of their ends.
    /// </param>
    /// <param name="errors">The destination of the exception of each run with an error.</param>
    /// <param name="operation">The asynchronous work for each input.</param>
    /// <param name="strategy">The control of operations that overlap.</param>
    /// <param name="cancelAll">This is optional. Each send cancels each tracked operation.</param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations with a given input value.
    /// </param>
    /// <param name="cancelOnDispose">True when a disposal also cancels the tracked items. The default is true.</param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" /> that gives the Queued items and the Running
    ///     items. A disposal of it stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />, <paramref name="errors" />,
    ///     <paramref name="operation" />, or <paramref name="strategy" /> is null.
    /// </exception>
    public static AsyncMapStatus<TInput> MapAsync<TInput, TResult, TStrategyInput, TStrategyResult>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        AsyncConcurrencyStrategyBase<TStrategyInput, TStrategyResult> strategy,
        Stream<Unit>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true)
        where TInput : TStrategyInput
        where TResult : TStrategyResult =>
        source.MapAsyncImpl(
            results: results,
            errors: errors,
            operation: operation,
            strategy: strategy,
            inputConverter: static v => v,
            resultConverter: static v => v,
            cancelAll: ToUnitInternalStream(cancelAll),
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose);

    /// <summary>
    ///     For a strategy that reads the input and the result. Here
    ///     <paramref name="inputConverter" /> makes the input that the strategy reads, and
    ///     <typeparamref name="TResult" /> is the <typeparamref name="TStrategyResult" />. See the
    ///     canonical
    ///     <see
    ///         cref="MapAsync{TInput,TResult,TStrategyInput,TStrategyResult}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Func{TInput,CancellationToken,Task{TResult}},AsyncConcurrencyStrategyBase{TStrategyInput,TStrategyResult},Func{TInput,TStrategyInput},Func{TResult,TStrategyResult},Stream{Unit},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     overload for the full parameter contract.
    /// </summary>
    /// <typeparam name="TInput">The type in the source stream.</typeparam>
    /// <typeparam name="TResult">The type that <paramref name="operation" /> gives when it succeeds.</typeparam>
    /// <typeparam name="TStrategyInput">
    ///     The input type of <paramref name="strategy" />. The compiler infers it from the type
    ///     of <paramref name="strategy" />, and no caller gives it explicitly. It needs no
    ///     inheritance relation to <typeparamref name="TInput" />, because
    ///     <paramref name="inputConverter" /> makes it explicitly.
    /// </typeparam>
    /// <typeparam name="TStrategyResult">
    ///     The result type of <paramref name="strategy" />. The compiler infers it, it is usually
    ///     <typeparamref name="TResult" />, and the <c>where TResult : TStrategyResult</c>
    ///     constraint connects the two.
    /// </typeparam>
    /// <param name="source">The stream of inputs.</param>
    /// <param name="results">
    ///     The destination of the value of each run that succeeded, in the sequence of their ends.
    /// </param>
    /// <param name="errors">The destination of the exception of each run with an error.</param>
    /// <param name="operation">The asynchronous work for each input.</param>
    /// <param name="strategy">The control of operations that overlap.</param>
    /// <param name="inputConverter">
    ///     Changes each <typeparamref name="TInput" /> value to the
    ///     <typeparamref name="TStrategyInput" /> of <paramref name="strategy" />, before the
    ///     admission.
    /// </param>
    /// <param name="cancelAll">This is optional. Each send cancels each tracked operation.</param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations with a given input value.
    /// </param>
    /// <param name="cancelOnDispose">True when a disposal also cancels the tracked items. The default is true.</param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" /> that gives the Queued items and the Running
    ///     items. A disposal of it stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />, <paramref name="errors" />,
    ///     <paramref name="operation" />, or <paramref name="strategy" /> is null.
    /// </exception>
    public static AsyncMapStatus<TInput> MapAsync<TInput, TResult, TStrategyInput, TStrategyResult>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        AsyncConcurrencyStrategyBase<TStrategyInput, TStrategyResult> strategy,
        Func<TInput, TStrategyInput> inputConverter,
        Stream<Unit>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true)
        where TResult : TStrategyResult =>
        source.MapAsyncImpl(
            results: results,
            errors: errors,
            operation: operation,
            strategy: strategy,
            inputConverter: inputConverter,
            resultConverter: static v => v,
            cancelAll: ToUnitInternalStream(cancelAll),
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose);

    /// <summary>
    ///     For a strategy that reads the input and the result. Here
    ///     <typeparamref name="TInput" /> is the <typeparamref name="TStrategyInput" />, and
    ///     <paramref name="resultConverter" /> makes the result that the strategy reads. See the
    ///     canonical
    ///     <see
    ///         cref="MapAsync{TInput,TResult,TStrategyInput,TStrategyResult}(Stream{TInput},StreamSink{TResult},StreamSink{Exception},Func{TInput,CancellationToken,Task{TResult}},AsyncConcurrencyStrategyBase{TStrategyInput,TStrategyResult},Func{TInput,TStrategyInput},Func{TResult,TStrategyResult},Stream{Unit},Stream{IReadOnlyCollection{TInput}},bool)" />
    ///     overload for the full parameter contract.
    /// </summary>
    /// <typeparam name="TInput">The type in the source stream.</typeparam>
    /// <typeparam name="TResult">The type that <paramref name="operation" /> gives when it succeeds.</typeparam>
    /// <typeparam name="TStrategyInput">
    ///     The input type of <paramref name="strategy" />. The compiler infers it, it is usually
    ///     <typeparamref name="TInput" />, and the <c>where TInput : TStrategyInput</c> constraint
    ///     connects the two.
    /// </typeparam>
    /// <typeparam name="TStrategyResult">
    ///     The result type of <paramref name="strategy" />. The compiler infers it from the type
    ///     of <paramref name="strategy" />, and no caller gives it explicitly. It needs no
    ///     inheritance relation to <typeparamref name="TResult" />, because
    ///     <paramref name="resultConverter" /> makes it explicitly.
    /// </typeparam>
    /// <param name="source">The stream of inputs.</param>
    /// <param name="results">
    ///     The destination of the value of each run that succeeded, in the sequence of their ends.
    /// </param>
    /// <param name="errors">The destination of the exception of each run with an error.</param>
    /// <param name="operation">The asynchronous work for each input.</param>
    /// <param name="strategy">The control of operations that overlap.</param>
    /// <param name="resultConverter">
    ///     Changes each <typeparamref name="TResult" /> that succeeded to the
    ///     <typeparamref name="TStrategyResult" /> of <paramref name="strategy" />, before
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TResult,TState}.OnCompleted" /> reads it.
    ///     A run with an error and a canceled run have no result, thus this code does not call
    ///     it for them.
    /// </param>
    /// <param name="cancelAll">This is optional. Each send cancels each tracked operation.</param>
    /// <param name="cancelMatching">
    ///     This is optional. Each send cancels the tracked operations with a given input value.
    /// </param>
    /// <param name="cancelOnDispose">True when a disposal also cancels the tracked items. The default is true.</param>
    /// <returns>
    ///     An <see cref="AsyncMapStatus{TInput}" /> that gives the Queued items and the Running
    ///     items. A disposal of it stops the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source" />, <paramref name="results" />, <paramref name="errors" />,
    ///     <paramref name="operation" />, or <paramref name="strategy" /> is null.
    /// </exception>
    public static AsyncMapStatus<TInput> MapAsync<TInput, TResult, TStrategyInput, TStrategyResult>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        AsyncConcurrencyStrategyBase<TStrategyInput, TStrategyResult> strategy,
        Func<TResult, TStrategyResult> resultConverter,
        Stream<Unit>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true)
        where TInput : TStrategyInput =>
        source.MapAsyncImpl(
            results: results,
            errors: errors,
            operation: operation,
            strategy: strategy,
            inputConverter: static v => v,
            resultConverter: resultConverter,
            cancelAll: ToUnitInternalStream(cancelAll),
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose);

    /// <summary>
    ///     The fully general shape that each other MapAsync overload sends its call to. It is the
    ///     only overload with no necessary relation between <typeparamref name="TInput" /> and
    ///     <typeparamref name="TResult" />, and <typeparamref name="TStrategyInput" /> and
    ///     <typeparamref name="TStrategyResult" />. The caller gives
    ///     <paramref name="inputConverter" /> and <paramref name="resultConverter" /> explicitly.
    ///     Use a narrower overload where one applies, because those overloads let most calls omit
    ///     the two converters. Use this overload when the types of
    ///     <paramref name="strategy" /> have no inheritance relation to the
    ///     <typeparamref name="TInput" /> and the <typeparamref name="TResult" /> of the call, in
    ///     the two directions at the same time.
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
    /// <typeparam name="TStrategyResult">
    ///     The result type of <paramref name="strategy" />. The compiler infers it from the type
    ///     of <paramref name="strategy" />, and no caller gives it explicitly. It needs no
    ///     inheritance relation to <typeparamref name="TResult" />, because
    ///     <paramref name="resultConverter" /> makes it explicitly.
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
    /// <param name="resultConverter">
    ///     Changes each <typeparamref name="TResult" /> that succeeded to the
    ///     <typeparamref name="TStrategyResult" /> of <paramref name="strategy" />, before
    ///     <see cref="AsyncConcurrencyStrategy{TInput,TResult,TState}.OnCompleted" /> reads it.
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
    public static AsyncMapStatus<TInput> MapAsync<TInput, TResult, TStrategyInput, TStrategyResult>(
        this Stream<TInput> source,
        StreamSink<TResult> results,
        StreamSink<Exception> errors,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        AsyncConcurrencyStrategyBase<TStrategyInput, TStrategyResult> strategy,
        Func<TInput, TStrategyInput> inputConverter,
        Func<TResult, TStrategyResult> resultConverter,
        Stream<Unit>? cancelAll = null,
        Stream<IReadOnlyCollection<TInput>>? cancelMatching = null,
        bool cancelOnDispose = true) =>
        source.MapAsyncImpl(
            results: results,
            errors: errors,
            operation: operation,
            strategy: strategy,
            inputConverter: inputConverter,
            resultConverter: resultConverter,
            cancelAll: ToUnitInternalStream(cancelAll),
            cancelMatching: cancelMatching,
            cancelOnDispose: cancelOnDispose);

    // SodaFlow.Core.Async takes its cancelAll as a Stream<UnitInternal>, because Core has no
    // public type of its own for a value that a caller does not use. This code maps and does not
    // cast, and that keeps UnitInternal out of each signature above. UnitInternal is internal to
    // SodaFlow.Core, and code that consumes this library cannot name it. Thus,
    // SodaFlow.Functional.Unit is the only unit type that a C# caller sees. The F# wrapper does
    // the same for its own unit. See toUnitInternalStream there.
    private static Stream<UnitInternal>? ToUnitInternalStream(Stream<Unit>? cancelAll) =>
        cancelAll?.MapImpl(static _ => UnitInternal.Value);
}
