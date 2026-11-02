using System;
using System.Threading;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public sealed class UnitExtensionMethodsTests
{
    private static readonly TimeSpan BlockedWait = TimeSpan.FromSeconds(2);

    // A caller on a single-threaded context, such as a UI thread, blocks on the task from a wrapped
    // action. While it waits, it runs nothing that its context receives. The wrapper awaits the action
    // and then gives Unit.Value, and the action here completes on a different thread. Thus, the
    // wrapper's task completes only if its await does not post that continuation to the caller's
    // context. One test per overload, because each one has its own await.
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    [Arguments(16)]
    public async Task ToAsyncFuncCompletesForACallerThatBlocksOnItsContext(int arity)
    {
        BlockedContext context = new();
        TaskCompletionSource<bool> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Thread caller =
            new(() =>
            {
                SynchronizationContext.SetSynchronizationContext(context);
                TaskCompletionSource<Unit> actionResult = new();
                Task<Unit> task = Wrap(arity: arity, action: () => actionResult.Task)();
                ThreadPool.QueueUserWorkItem(_ => actionResult.SetResult(Unit.Value));
                completed.SetResult(task.Wait(BlockedWait));
            }) { IsBackground = true };

        caller.Start();

        await Assert.That(await completed.Task).IsTrue();
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    // Each arm calls one ToAsyncFunc overload with an action that ignores its arguments.
    // ReSharper disable ArgumentsStyleLiteral - every argument is a placeholder that the action ignores, and a
    // name on each one would say nothing.
    private static Func<Task<Unit>> Wrap(int arity, Func<Task> action) =>
        arity switch
        {
            0 => action.ToAsyncFunc(),
            1 => () => new Func<int, Task>(_ => action()).ToAsyncFunc()(1),
            2 => () => new Func<int, int, Task>((_, _) => action()).ToAsyncFunc()(1, 1),
            3 => () => new Func<int, int, int, Task>((_, _, _) => action()).ToAsyncFunc()(1, 1, 1),
            4 => () => new Func<int, int, int, int, Task>((_, _, _, _) => action()).ToAsyncFunc()(1, 1, 1, 1),
            5 => () => new Func<int, int, int, int, int, Task>((_, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1),
            6 => () => new Func<int, int, int, int, int, int, Task>((_, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1),
            7 => () => new Func<int, int, int, int, int, int, int, Task>((_, _, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1),
            8 => () => new Func<int, int, int, int, int, int, int, int, Task>((_, _, _, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1, 1),
            9 => () => new Func<int, int, int, int, int, int, int, int, int, Task>(
                    (_, _, _, _, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1, 1, 1),
            10 => () => new Func<int, int, int, int, int, int, int, int, int, int, Task>(
                    (_, _, _, _, _, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            11 => () => new Func<int, int, int, int, int, int, int, int, int, int, int, Task>(
                    (_, _, _, _, _, _, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            12 => () => new Func<int, int, int, int, int, int, int, int, int, int, int, int, Task>(
                    (_, _, _, _, _, _, _, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            13 => () => new Func<int, int, int, int, int, int, int, int, int, int, int, int, int, Task>(
                    (_, _, _, _, _, _, _, _, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            14 => () => new Func<int, int, int, int, int, int, int, int, int, int, int, int, int, int, Task>(
                    (_, _, _, _, _, _, _, _, _, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            15 => () => new Func<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, Task>(
                    (_, _, _, _, _, _, _, _, _, _, _, _, _, _, _) => action())
                .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            16 => () =>
                new Func<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, Task>(
                        (_, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _) => action())
                    .ToAsyncFunc()(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
            _ => throw new ArgumentOutOfRangeException(paramName: nameof(arity), actualValue: arity, message: null)
        };
    // ReSharper restore ArgumentsStyleLiteral

    // Counts the posts it receives and runs none of them, as a UI thread does while it blocks.
    private sealed class BlockedContext : SynchronizationContext
    {
        private int posts;

        public int Posts => Volatile.Read(ref this.posts);

        public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref this.posts);

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("Nothing sends to a blocked context.");
    }
}
