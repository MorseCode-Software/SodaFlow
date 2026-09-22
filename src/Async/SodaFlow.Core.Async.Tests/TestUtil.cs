using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace SodaFlow.Async.Tests;

internal static class TestUtil
{
    /// <summary>Reads <paramref name="condition" /> until it is true, or fails the test at a
    /// timeout.</summary>
    public static void WaitUntil([InstantHandle] Func<bool> condition, int timeoutMs = 5000)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            Thread.Sleep(10);
        }
    }
}

/// <summary>
///     An async operation, with the input as its key, whose end a test controls with
///     <see cref="Release" /> and <see cref="Fail" />. Thus, a test does not race the true clock.
///     It also records the inputs that the pipeline called. Thus, a test can show that an operation
///     has the Running status, and not only an admission, before the release. That difference
///     shows the behavior of Queue against the behavior of Parallel.
/// </summary>
internal sealed class ControlledOperation<TInput, TResult>
    where TInput : notnull
{
    private readonly ConcurrentDictionary<TInput, TaskCompletionSource<TResult>> gates = new();
    private readonly ConcurrentDictionary<TInput, bool> startedInputs = new();

    public MapAsyncOperation<TInput, TResult> Operation => this.Run;

    public bool HasStarted(TInput input) => this.startedInputs.ContainsKey(input);

    public void Release(TInput input, TResult result) => this.GateFor(input).TrySetResult(result);

    public void Fail(TInput input, Exception error) => this.GateFor(input).TrySetException(error);

    private async Task<MapAsyncResult<TResult>> Run(
        TInput input,
        ResultFactory<TResult> resultFactory,
        CancellationToken token)
    {
        this.startedInputs[input] = true;

        TaskCompletionSource<TResult> tcs = this.GateFor(input);

        // ReSharper disable once UseAwaitUsing - CancellationTokenRegistration is only
        // IAsyncDisposable from .NET Core 3.0, and this compiles for net472 as well.
        using CancellationTokenRegistration registration =
            token.Register(() => tcs.TrySetCanceled(token));

        TResult result = await tcs.Task.ConfigureAwait(false);

        return resultFactory.FromValue(result);
    }

    private TaskCompletionSource<TResult> GateFor(TInput input) =>
        this.gates.GetOrAdd(key: input, valueFactory: static _ => new TaskCompletionSource<TResult>());
}
