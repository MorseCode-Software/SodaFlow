using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SodaFlow.Bindable.ObjectModel.Tests;

// Gives each action to a thread of its own, which runs it immediately. The contract on
// IBindingScheduler.Post permits this, and the thread can run the action while the
// transaction propagates.
internal sealed class ThreadPerActionScheduler : IBindingScheduler
{
    private readonly TaskCompletionSource<bool> started = new();
    private readonly TaskCompletionSource<bool> finished = new();
    private readonly List<Thread> threads = [];

    /// <summary>Completes when a thread is about to run its action.</summary>
    internal Task AnActionStarted => this.started.Task;

    /// <summary>Completes when a thread ran its action.</summary>
    internal Task AnActionFinished => this.finished.Task;

    /// <inheritdoc />
    public bool CheckAccess() => true;

    /// <inheritdoc />
    public void Post(Action action)
    {
        Thread thread = new(() =>
        {
            this.started.TrySetResult(true);
            action();
            this.finished.TrySetResult(true);
        });

        this.threads.Add(thread);
        thread.Start();
    }

    internal void Join()
    {
        foreach (Thread thread in this.threads)
        {
            thread.Join();
        }

        this.threads.Clear();
    }
}
