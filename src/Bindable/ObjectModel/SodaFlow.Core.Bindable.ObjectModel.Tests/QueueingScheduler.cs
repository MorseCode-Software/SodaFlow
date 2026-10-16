using System;
using System.Collections.Generic;

namespace SodaFlow.Bindable.ObjectModel.Tests;

/// <summary>
///     This takes the place of a dispatcher. It queues work, which is the
///     important part. A real scheduler gives work to the message loop of a
///     different thread and returns. Thus, work that a write posts is in the queue
///     when the setter returns.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage
internal sealed class QueueingScheduler : IBindingScheduler
{
    private readonly Queue<Action> queue = new();

    /// <inheritdoc />
    public bool CheckAccess() => true;

    /// <inheritdoc />
    public void Post(Action action) => this.queue.Enqueue(action);

    /// <summary>
    ///     Runs each action in the queue, and also an action that another action adds
    ///     while this method runs.
    /// </summary>
    /// <returns>The number of actions that ran.</returns>
    internal int RunAll()
    {
        int ran = 0;

        while (this.queue.Count > 0)
        {
            this.queue.Dequeue()();
            ran++;
        }

        return ran;
    }
}
