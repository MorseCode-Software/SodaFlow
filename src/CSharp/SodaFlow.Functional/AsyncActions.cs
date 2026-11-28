using System;
using System.Threading.Tasks;

namespace SodaFlow.Functional;

// Runs the one asynchronous action that a match of a Maybe or an Either chose. The match picks the
// case first, thus no other action gets a closure or a delegate. Each method is async, thus a throw
// from the action before it gives its task becomes a fault on the task that the method gives, and
// does not leave the call. A task that the action gives complete makes the method complete at once,
// with a task that the runtime keeps for that purpose.
internal static class AsyncActions
{
    internal static async Task Run(Func<Task> action) => await action().ConfigureAwait(false);

    internal static async Task Run<T>(Func<T, Task> action, T value) => await action(value).ConfigureAwait(false);
}
